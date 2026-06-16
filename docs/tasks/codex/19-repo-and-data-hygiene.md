# Task 19 — Repo & data hygiene

Read `README.md` in this folder first for repo context.

## Goal

The repository contains no author-session residue, the identity data layer enforces case-insensitive email uniqueness and bounded audit growth, and the migration bootstrap has no cross-service race.

Four related parts, implemented together in the final task commit.

## Part 1 — Repo file cleanup

Current clutter (check `git status` — some is untracked, some tracked):

- Untracked at root: `build.log` (delete; verify `*.log` is in `.gitignore` — it is via `**/*.log` in `.dockerignore` but check `.gitignore` itself, add if missing), ` Telemetry ingestion sequence diagram-2026-05-29-095351.png` (**leading space in filename**), `code-review-2026-05-29.md`, `code-review-2026-06-10.md`, `cleanup-plan-2026-06-01.md`.
- Untracked in docs: two pre-rename architecture screenshots.
- Tracked with author-session filenames that should become neutral project documentation.

Actions:
1. Create `docs/reviews/` and move the three review/cleanup markdown files there; commit them (they're real engineering records).
2. Create `docs/img/`; move and rename the PNGs (`telemetry-ingestion-sequence-2026-05-29.png`, `architecture-v2-2026-06-05.png`, `architecture-v2-simplified-2026-06-05.png`); commit.
3. Rename tracked docs to neutral names under `docs/`: `project-notes.md`, `architecture-review.md`, `architecture-diagram-notes.md`, and `architecture-diagram-simple-notes.md`. Then search for old author-session names and fix every inbound link.
4. Delete `build.log`.

## Part 2 — Case-insensitive email uniqueness

`users.email` has a case-sensitive `unique` constraint while login looks up `lower(email) = lower(@email)` (`IdentityRepository.cs`): `Admin@x` and `admin@x` can coexist as distinct rows, and login then matches one of them nondeterministically (the query has no `limit`/ordering).

- `IdentityMigrator` new migration (`003_email_ci_unique` or next free number after Task 16's): `create unique index if not exists ux_users_email_lower on users (lower(email));`. If existing data contains case-duplicates the migration fails loudly — acceptable; add one sentence to `docs/dev-setup.md` about resolving duplicates manually before upgrade.
- Normalize on write: seed/bootstrap inserts store `lower(email)`-normalized addresses? **No** — keep stored casing as-entered; the functional index is the guarantee. Just ensure all lookups already use `lower()` (they do).

## Part 3 — Bounded audit log

`audit_events` grows forever and `auth.login_failed` rows embed attacker-controlled strings of unbounded length.

- `IdentityRepository.WriteAuditAsync`: truncate `message` to 500 chars before insert.
- New `AuditRetentionWorker : BackgroundService` in Identity: on start and then every 24h, `delete from audit_events where created_at_utc < now() - make_interval(days => @days)` with `Audit:RetentionDays` config (default 180, validated positive). Log deleted count when > 0. Register in `Program.cs`.
- Index to keep the delete cheap: `create index if not exists ix_audit_created on audit_events (created_at_utc);` (same migration as Part 2 or its own).

## Part 4 — Migration bootstrap race

`SqlDatabaseMigrator.MigrateAsync` (`src/Alpha.Scada.ServiceDefaults/DatabaseMigrations.cs:30-38`) creates `alpha_schema_migrations` **before** taking the advisory lock, and the lock key is per-migrator — two migrators bootstrapping the same fresh database concurrently can race `create table if not exists` (noisy `pg_type` unique-violation errors). Reorder:

```csharp
await ExecuteAsync(connection, transaction, "select pg_advisory_xact_lock(hashtext('alpha_schema_migrations'));", ct);   // constant key: serializes bootstrap
await ExecuteAsync(connection, transaction, "create table if not exists alpha_schema_migrations (...);", ct);
await ExecuteAsync(connection, transaction, "select pg_advisory_xact_lock(hashtext(@lock_key));", ct, ("lock_key", Name)); // existing per-migrator lock
```

(Since Task 05 each service has its own DB, this race mostly matters for multi-migrator services and tests — it's a correctness nicety; keep the change minimal.)

## Constraints

- Part 1 must not break any link: `dotnet build` won't catch markdown links, so the grep step is mandatory and its output goes in the PR description.
- No schema changes beyond the listed index/columns; no changes to audit event content besides truncation.

## Verification

```bash
git status --short                  # clean root: no untracked clutter, no leading-space filenames
git grep -niE "$LEGACY_NAME_PATTERN" -- README.md docs src tests   # no hits
dotnet build Alpha.Scada.slnx && dotnet test Alpha.Scada.slnx
# Email uniqueness (Testcontainers or compose):
docker compose exec postgres psql -U alpha -d alpha_identity -c "insert into users (id, tenant_id, email, display_name, password_hash, role) values (gen_random_uuid(), gen_random_uuid(), 'ADMIN@alpha.local', 'dup', 'x', 'Viewer')"   # fails on ux_users_email_lower
# Audit retention: insert an old row, restart identity, confirm it's gone and recent rows remain.
```
