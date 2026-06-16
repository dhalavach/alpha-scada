# Task 16 — Auth hardening: RS256 user tokens, issuer/audience, login rate limiting, lockout, PBKDF2 600k

Read `README.md` in this folder first for repo context.

## Goal

- Only Identity can mint **user** tokens (RS256; private key exists nowhere else). The shared symmetric secret is demoted to service-to-service tokens only, enforced at validation time.
- All tokens carry and all services validate `iss`/`aud`.
- The login endpoint is rate-limited at the gateway and protected by per-account lockout in Identity.
- PBKDF2 iterations meet OWASP guidance (600k) with transparent rehash on login.

Token lifetime stays 12h; refresh-token flow is explicitly **out of scope** (future task — note it in the PR description).

## Problem

One symmetric `Jwt:Secret` signs everything and is distributed to all nine services — any compromised service can mint admin tokens for any tenant. `ValidateIssuer = false, ValidateAudience = false` (`src/Alpha.Scada.ServiceDefaults/JwtTokenService.cs`). `POST /api/auth/login` has no rate limit or lockout, and each attempt runs a full PBKDF2 (100k iterations — below the OWASP 2023 recommendation of 600k for PBKDF2-HMAC-SHA256), making it a free CPU-burn and credential-stuffing surface.

## Design — read before coding

Task 01's `ServiceTokenProvider` mints tokens in **every** service, so a naive "RS256 everywhere" breaks S2S auth (only Identity would hold the private key). The design therefore uses **two keys with role binding**:

- **User tokens**: RS256, private key configured only in Identity.
- **Service tokens**: HS256 with the existing shared `Jwt:Secret` (every service keeps minting its own — no new infrastructure).
- **Validation** (all services): `TokenValidationParameters.IssuerSigningKeys = [rsaPublicKey, symmetricKey]`, plus a `JwtBearerEvents.OnTokenValidated` guard: if the incoming token's header `alg` is HS256 **and** its `role` claim is not `Service`, reject. This binds the shared secret to the Service role — a compromised service can forge only what it already has (service access), never a user/admin identity.

## Implementation steps

### 1. Keys and configuration

- New config keys: `Jwt:PublicKeyB64` (all services — base64-encoded SPKI/PKCS#8 public key PEM), `Jwt:PrivateKeyB64` (Identity only). Base64-wrapping keeps multiline PEMs env-var-friendly. Keep `Jwt:Secret` everywhere (service tokens).
- `ops/scripts/dev-setup.sh`: generate once into `.env` (keep `ensure_value` idempotency):
  ```bash
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$tmp_key"
  ensure_value "JWT_PRIVATE_KEY_B64" "$(base64 < "$tmp_key" | tr -d '\n')"
  ensure_value "JWT_PUBLIC_KEY_B64" "$(openssl pkey -in "$tmp_key" -pubout | base64 | tr -d '\n')"
  ```
- `docker-compose.yml`: `Jwt__PublicKeyB64: ${JWT_PUBLIC_KEY_B64:?...}` on **all** backend services; `Jwt__PrivateKeyB64` on **identity only**. Update the `kubectl create secret` template comment in `ops/k3s/config.yaml` accordingly.

### 2. `JwtTokenService` restructure (`src/Alpha.Scada.ServiceDefaults/JwtTokenService.cs`)

- `CreateValidationParameters(configuration)`: build `RsaSecurityKey` from the decoded public PEM (`RSA.Create()` + `ImportFromPem`) — **required**, throw a clear error if missing; `IssuerSigningKeys = [rsaKey, symmetricKey]`; `ValidIssuer = "alpha-scada-identity"`, `ValidAudience = "alpha-scada"`, `ValidateIssuer = ValidateAudience = true`. Keep clock skew and claim-type mappings.
- Split issuance: `IssueUserToken(UserDto, TimeSpan)` — RS256, lazily loads the private key from `Jwt:PrivateKeyB64`, throwing `InvalidOperationException("Jwt:PrivateKeyB64 is required to issue user tokens — only the Identity service should do this.")` when absent; `IssueServiceToken(UserDto, TimeSpan)` — HS256 with the secret (current behavior). Both set `Issuer`/`Audience` on the descriptor. Update callers: `AuthService` → `IssueUserToken`; `ServiceTokenProvider` → `IssueServiceToken`. Remove/replace the now-ambiguous `Issue`.
- `AlphaAuthentication.AddAlphaJwtAuthentication`: add the `OnTokenValidated` algorithm/role guard described above (compose with the existing SignalR `OnMessageReceived` — both events must coexist; the gateway already supplies its own `JwtBearerEvents`, so set the guard in shared code and have the gateway *augment*, not replace — restructure so shared code assigns events first and the optional `configure` callback may only add `OnMessageReceived`).

### 3. Gateway login rate limiting

- `builder.Services.AddRateLimiter(...)`: policy `"login"`, fixed window 1 minute, 10 permits, no queue, partitioned by client IP. For the IP to be real behind nginx: add `proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;` to `src/Alpha.Scada.Web/nginx.conf`'s `/api/` block, and in the gateway enable `UseForwardedHeaders` (XForwardedFor) with `KnownNetworks`/`KnownProxies` cleared. **Comment the tradeoff**: a caller hitting the gateway directly (port 5202) can spoof XFF to dodge per-IP partitioning; the account lockout below is the real backstop.
- `app.UseRateLimiter()` after `UseForwardedHeaders`; `.RequireRateLimiting("login")` on `POST /api/auth/login`. 429 responses need no body contract.

### 4. Identity lockout

- Migration `002_lockout` on `IdentityMigrator`: `alter table users add column if not exists failed_login_count int not null default 0, add column if not exists locked_until_utc timestamptz;`
- `AuthService.LoginAsync`: if `locked_until_utc > now()` → audit `auth.login_locked`, return null (indistinguishable 401 — no lockout oracle). On wrong password: atomically increment `failed_login_count`; when it reaches 5, set `locked_until_utc = now() + interval '15 minutes'` and reset the counter. On success: reset counter and `locked_until_utc = null`. Implement as single UPDATE statements in `IdentityRepository` (no read-modify-write races).

### 5. PBKDF2 600k + transparent rehash

- `PasswordHasher.Iterations` → `600_000`; add `public static bool NeedsRehash(string hash)` (parses the iteration field, true when `< Iterations`).
- After a successful verify with `NeedsRehash`, re-hash the presented password and persist via a new `IdentityRepository.UpdatePasswordHashAsync`. Seeded demo users upgrade silently on first login.

## Tests

- RS256 round-trip: token from `IssueUserToken` validates through the JwtBearer pipeline (TestServer, pattern: `ServiceAuthTests`); a forged HS256 token with role `Admin` is **rejected**; an HS256 token with role `Service` passes; wrong `iss`/`aud` rejected. Provide a shared `TestJwt` helper (static lazily-generated RSA keypair + config dictionary) and migrate the existing `ConfigurationWithSecret()` helpers in `ServiceAuthTests`, `ServiceDefaultsEndpointTests`, `ServiceDefaultsSecurityTests`, `ContractTests`, and the integration hosts (`CommunicationLossAlarmTests`, `BackendRepositoryTests`, `TelemetryPrimaryIngestionTests`, `AlarmOutboxTests`) — they all need `Jwt:PublicKeyB64` now for validation-parameter construction.
- Lockout (Testcontainers PG, pattern: `BackendRepositoryTests`): 5 wrong passwords → 6th attempt with the **correct** password still null until expiry; distinct audit event written; success resets.
- Rehash: seed a user hashed at 100k, login, assert stored hash now reports 600k and login still works.
- Rate limit: TestServer with the limiter policy — 11th request inside the window → 429.

## Constraints

- Token payload claims unchanged (`sub`, `tenant_id`, `email`, `name`, `role`) — the SPA and `AuthenticatedUser.BindAsync` must work untouched.
- 12h lifetime unchanged; no refresh tokens, no revocation list in this task.
- `Jwt:Secret` requirements stay (all services); do not weaken Task 01's service auth.

## Verification

```bash
rm -f .env && ops/scripts/dev-setup.sh && dotnet build Alpha.Scada.slnx && dotnet test Alpha.Scada.slnx
docker compose up --build
# Login via UI works; /api/me works; report run round-trip works (service tokens unaffected).
# 11 rapid logins from one client → 429. 5 wrong passwords → correct password rejected for 15 min (check audit_events).
# Forge check: mint an HS256 token with role Admin using JWT_SECRET from .env → any /api call returns 401.
```
