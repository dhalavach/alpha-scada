# Task 01 — Service-to-service auth: close all anonymous `/internal/v1` endpoints

Read `README.md` in this folder first for repo context.

## Goal

Every `/internal/v1/*` endpoint in every service requires authentication. Endpoints that exist only for service-to-service calls additionally require a new `Service` role. Services authenticate to each other with a short-lived self-minted JWT, attached automatically by the shared HTTP client infrastructure.

## Problem

User-facing internal endpoints require a JWT, but every endpoint that other *services* call was mapped outside the `RequireAuthorization()` group — because services have no credentials of their own. Current anonymous surface:

| Service | Endpoint | File |
|---|---|---|
| Telemetry | `POST /internal/v1/telemetry/units/{unitId}/report-aggregate` | `src/Alpha.Scada.Telemetry/Program.cs:49` |
| Alarm | `GET /internal/v1/alarms/count` | `src/Alpha.Scada.Alarm/Program.cs:51` |
| Tenant | `GET /internal/v1/tenants/resolve/{tenantKey}` and `GET /internal/v1/tenants/{tenantId}` | `src/Alpha.Scada.Tenant/Program.cs:24,30` |
| TagCatalog | `POST /internal/v1/tags/resolve` and `GET /internal/v1/report-config/units/{unitId}` | `src/Alpha.Scada.TagCatalog/Program.cs:25,28` |
| Asset | `GET /internal/v1/units/resolve`, `GET /internal/v1/units/{unitId}/route`, `GET /internal/v1/units/stale` | `src/Alpha.Scada.Asset/Program.cs:40–53` |

Anyone with network reach can enumerate tenants, resolve topology, read alarm counts, and run aggregate queries for any tenant.

The internal callers that will need the new service token (all already go through `IHttpClientFactory` clients registered by `AddAlphaServiceClients`, and currently send **no** Authorization header):
- `src/Alpha.Scada.Telemetry/Application/CatalogCache.cs` (Tenant, Asset, TagCatalog)
- `src/Alpha.Scada.Alarm/Application/ThresholdCache.cs` (TagCatalog) and `UnitKeyResolver.cs` (Asset, Tenant)
- `src/Alpha.Scada.Asset/Application/TenantKeyResolver.cs` (Tenant)
- `src/Alpha.Scada.Reporting/Application/ReportingService.cs` (TagCatalog, Telemetry, Alarm)

The Gateway forwards the **user's** token on its calls (`GatewayAuth.WithBearerToken` / `HttpUserContext.ForwardAuthorizationFrom`) — that flow must keep working unchanged.

## Implementation steps

### 1. Add the role

In `src/Alpha.Scada.Contracts/Auth/AuthContracts.cs`:
- `Roles`: add `public const string Service = "Service";`
- `RoleRules`: add `public static bool IsService(string role) => role == Roles.Service;`

### 2. `ServiceTokenProvider` in ServiceDefaults

New file `src/Alpha.Scada.ServiceDefaults/ServiceTokenProvider.cs`:

```csharp
public sealed class ServiceTokenProvider(JwtTokenService tokens)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RenewalSkew = TimeSpan.FromMinutes(1);
    private readonly Guid principalId = Guid.NewGuid();
    private readonly object gate = new();
    private LoginResponse? current;

    public string GetToken()
    {
        lock (gate)
        {
            if (current is null || current.ExpiresAtUtc - RenewalSkew <= DateTimeOffset.UtcNow)
            {
                var serviceName = Assembly.GetEntryAssembly()?.GetName().Name ?? "alpha-service";
                current = tokens.Issue(
                    new UserDto(principalId, Guid.Empty, $"{serviceName}@internal", serviceName, Roles.Service),
                    Lifetime);
            }
            return current.AccessToken;
        }
    }
}
```

Notes: `JwtTokenService.Issue` already exists and signs with the shared secret every service holds, so validation works everywhere with zero key distribution changes. `tenant_id = Guid.Empty` is deliberate: `AuthenticatedUser.BindAsync` requires a parseable GUID, and tenant-scoped queries (`@is_support or tenant_id = @tenant_id`) will correctly match nothing if a service token is ever used against a user-scoped endpoint.

### 3. Delegating handler that attaches the token

New file `src/Alpha.Scada.ServiceDefaults/ServiceAuthorizationHandler.cs`:

```csharp
public sealed class ServiceAuthorizationHandler(ServiceTokenProvider tokenProvider) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.GetToken());
        }
        return base.SendAsync(request, cancellationToken);
    }
}
```

The `if` guard is what preserves the gateway's user-token forwarding: when `ForwardAuthorization` already set a header, the handler must not touch it. Note `HttpUserContext.ForwardAuthorization` uses `TryAddWithoutValidation`, which stores the header but `request.Headers.Authorization` still returns it — verify with a quick unit test (see step 7) that a forwarded user token is NOT overwritten.

### 4. Wire into `AddAlphaServiceClients`

In `src/Alpha.Scada.ServiceDefaults/AlphaServiceClients.cs`, inside `AddAlphaServiceClients`:
- `services.AddJwtTokenService(configuration);` — make registrations idempotent first: in `JwtTokenServiceRegistration.AddJwtTokenService`, switch `AddSingleton` to `TryAddSingleton` (it is currently also called by `AddAlphaJwtAuthentication`, and Reporting/others call both).
- `services.TryAddSingleton<ServiceTokenProvider>();`
- `services.TryAddTransient<ServiceAuthorizationHandler>();` (message handlers must be transient)
- Append `.AddHttpMessageHandler<ServiceAuthorizationHandler>()` to the `AddHttpClient(...)` chain (after `.AddAlphaResilience()` or before — pick one and keep it; before resilience means retries reuse the same token, which is fine within the 10-min lifetime).

This makes every internal caller authenticated with **no call-site changes** in CatalogCache/ThresholdCache/UnitKeyResolver/TenantKeyResolver/ReportingService.

### 5. Authorization policy

In `src/Alpha.Scada.ServiceDefaults/AlphaAuthentication.cs`, replace `services.AddAuthorization()` with:

```csharp
services.AddAuthorization(options =>
    options.AddPolicy(ServiceOnlyPolicy, policy => policy.RequireRole(Roles.Service)));
```

Add `public const string ServiceOnlyPolicy = "ServiceOnly";` on `AlphaAuthentication`. (`RequireRole` works because `TokenValidationParameters.RoleClaimType = "role"` is already set.)

### 6. Lock down the endpoints

Restructure each affected `Program.cs` so **all** internal endpoints hang off one authorized group, with service-only endpoints carrying the extra policy:

```csharp
var internalApi = app.MapGroup("/internal/v1").RequireAuthorization();
// user-facing endpoints: unchanged, on internalApi
internalApi.MapPost("/tags/resolve", ...).RequireAuthorization(AlphaAuthentication.ServiceOnlyPolicy);
```

Per service:
- **Tenant** (`Program.cs`): currently uses an awkward chained `app.MapGroup(...).RequireAuthorization().MapGet(...)`. Restructure to the `var internalApi = ...` shape. `tenants` stays user-level; `tenants/resolve/{tenantKey}` and `tenants/{tenantId:guid}` get `ServiceOnlyPolicy`.
- **TagCatalog**: same restructure. `units/{unitId}/tags` stays user-level; `tags/resolve` and `report-config/units/{unitId}` get `ServiceOnlyPolicy`.
- **Asset**: `sites`, `sites/{id}/units`, `units/{id}` stay user-level; `units/resolve` and `units/{unitId}/route` get `ServiceOnlyPolicy`. **Delete** `GET /internal/v1/units/stale` entirely, plus `AssetService.GetStaleUnitsAsync` and `AssetRepository.GetStaleUnitsAsync` — they have zero callers (verify with `grep -rn "units/stale\|GetStaleUnitsAsync" src tests` before deleting).
- **Alarm**: `alarms/active` and `alarms/{id}/ack` stay user-level; `alarms/count` moves into the group with `ServiceOnlyPolicy`.
- **Telemetry**: `current`/`history` stay user-level; `report-aggregate` moves into the group with `ServiceOnlyPolicy`.

### 7. Tests

Add `tests/Alpha.Scada.Tests/ServiceAuthTests.cs` (model it on the existing in-process host patterns in `ServiceDefaultsSecurityTests.cs` / `ServiceDefaultsEndpointTests.cs` — read those first). Cover:
1. Request without a token to a `ServiceOnly` endpoint → 401.
2. Request with a **user** token (mint via `JwtTokenService.Issue` with role `Viewer`) → 403.
3. Request with the token from `ServiceTokenProvider` → 200.
4. `ServiceAuthorizationHandler` does not overwrite an existing Authorization header (unit test with a stub inner handler: set a header via `HttpUserContext.ForwardAuthorization`, assert it survives).
5. `ServiceTokenProvider` caches: two `GetToken()` calls within the lifetime return the same string.

## Constraints

- Do not change the gateway's user-token forwarding behavior or any public `/api/*` route shapes.
- Do not touch the Edge service (it has no HTTP clients and no JWT config).
- Operational endpoints (`/health`, `/ready`, `/metrics`) stay anonymous.
- Response contracts of all internal endpoints are unchanged.

## Verification

```bash
dotnet build Alpha.Scada.slnx                  # 0 warnings
dotnet test Alpha.Scada.slnx                   # all green (see README pitfall note)
docker compose up --build                      # then:
# 1) log in to the UI (admin@alpha.local / ChangeMe!123), Overview shows live values
# 2) curl -s -o /dev/null -w '%{http_code}' http://localhost:5202/api/sites  -> 401
# 3) from inside the network: anonymous tenants/resolve must be 401:
docker compose exec gateway sh -c "wget -qO- -S http://tenant:8080/internal/v1/tenants/resolve/demo-operator 2>&1 | head -1"  # HTTP/1.1 401
# 4) trigger a monthly report from the UI Reports screen -> completes (proves the
#    Reporting -> TagCatalog/Telemetry/Alarm chain works with the service token)
```

The simulator must keep ingesting (Telemetry resolves tenant/unit/tags via service token): watch `docker compose logs telemetry` for absence of 401-related errors.
