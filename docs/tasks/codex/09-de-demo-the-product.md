# Task 09 — De-demo the product: login prefill, demo seed gating, honest availability

Read `README.md` in this folder first for repo context.

## Goal

A production deployment contains zero demo artifacts: no pre-filled admin credentials in the login form, no demo tenants/sites/units/tags unless explicitly enabled, and a monthly-report availability figure computed from data instead of a configured constant.

Three parts, **three commits** (each independently revertable).

## Part 1 — Remove pre-filled admin credentials from the login form

`src/Alpha.Scada.Web/src/screens/Login.tsx:8-9` initializes state with `"admin@alpha.local"` / `"ChangeMe!123"`. Every deployment ships a login form pre-populated with admin credentials.

Change to empty strings; add `autoComplete="username"` / `autoComplete="current-password"` and `required` to the inputs. Document the demo credentials only in `docs/dev-setup.md` (already there).

## Part 2 — Gate demo seed data behind configuration

**Problem.** Only Identity gates demo data (`Seed:DemoUsers`, defaulting to on in Development — see `IdentityMigrator.SeedAsync` for the exact pattern). Three other services seed demo data **unconditionally**, two of them inside *versioned migrations*:

- `src/Alpha.Scada.Tenant/Infrastructure/TenantMigrator.cs` — `001_initial` INSERTs `demo-operator` / `field-operator` tenants.
- `src/Alpha.Scada.Asset/Infrastructure/AssetMigrator.cs` — `001_initial` INSERTs demo sites and units.
- `src/Alpha.Scada.TagCatalog/Infrastructure/TagCatalogMigrator.cs` — `SeedAsync` seeds 15 demo tags, report profiles, and metric bindings for hardcoded unit GUIDs, unconditionally.

**Change.**
1. Move the tenant/site/unit INSERTs out of the `001_initial` migration SQL into `SeedAsync` overrides on `TenantMigrator` and `AssetMigrator`. This is safe for existing databases: `SqlDatabaseMigrator` tracks applied migrations by `(migrator, migration_id)` row, not by SQL content, so editing `001_initial`'s text does not re-run it; and all seed INSERTs are `on conflict do nothing`.
2. Gate all three `SeedAsync` demo blocks behind a single flag, same pattern as Identity: `configuration.GetValue<bool?>("Seed:DemoData") ?? environment.IsDevelopment()`. The migrators will need `IConfiguration` + `IHostEnvironment` constructor parameters — copy `IdentityMigrator`'s constructor shape.
3. **Keep `report_metric_definitions` seeding unconditional** in TagCatalog — that's reference data (metric ontology), not demo data. Only tags/profiles/bindings for the demo units are demo data.
4. `docker-compose.yml`: add `Seed__DemoData: "true"` to the `tenant`, `asset`, and `tag-catalog` services (Identity already has `Seed__DemoUsers`). Consider renaming Identity's flag to the same `Seed__DemoData` key for uniformity — if you do, change both the code and compose, and note it in the commit message.
5. `ops/k3s/config.yaml`: nothing to add (flags absent = off outside Development) — but verify `ASPNETCORE_ENVIRONMENT` is not set to `Development` anywhere in k3s.

## Part 3 — Compute availability honestly

**Problem.** `src/Alpha.Scada.Reporting/Application/ReportingService.cs:52`:
```csharp
var availability = alarmCount > 0 ? profile.AvailabilityWithAlarmsPercent : profile.AvailabilityNoAlarmsPercent;
```
The headline availability KPI in a customer-facing monthly report is a **configured constant** (99.5 / 98.5 seeded in `report_profiles`), not a measurement.

**Change.**
1. Compute: `availability = runtimeHours / hoursInPeriod * 100`, where `hoursInPeriod = (period.EndUtc - period.StartUtc).TotalHours` via `MonthPeriod.Parse(period)` (already referenced by ServiceDefaults). Clamp to `[0, 100]`, round to 1 decimal. `aggregate.RuntimeHours` is already fetched in `GenerateMonthlyAsync`.
2. Remove the two constants end-to-end:
   - `ReportProfileDto`: drop `AvailabilityNoAlarmsPercent` / `AvailabilityWithAlarmsPercent` (keep `BiocharYieldM3PerKg` — still used).
   - `TagCatalogRepository.GetReportProfileAsync`: stop selecting them.
   - `TagCatalogMigrator`: new migration `002_drop_availability_constants` with `alter table report_profiles drop column if exists availability_no_alarms_percent, drop column if exists availability_with_alarms_percent;` and update the profile seed INSERT accordingly.
3. Update `tests/Alpha.Scada.Tests/ReportOntologyConfigTests.cs` — the fact `Reporting_service_uses_profile_factors_for_availability_and_biochar` asserts the old behavior; rewrite it to assert computed availability (e.g. seed minute aggregates / runtime for half the period → expect ~50%, and a full-runtime period → 100). Keep the biochar-factor assertion.

## Constraints

- No changes to report storage schema (`report_runs.availability_percent` stays; only its meaning becomes honest).
- `MonthlyReportDto` shape unchanged.
- Existing demo flow must keep working: `docker compose up --build` still yields the demo tenant, seeded tags, a working dashboard, and report generation.

## Verification

```bash
dotnet build Alpha.Scada.slnx && dotnet test Alpha.Scada.slnx
# Fresh boot WITHOUT demo data:
docker compose down -v
# temporarily set Seed__DemoData: "false" (and Seed__DemoUsers: "false") in compose, boot, then:
docker compose exec postgres psql -U alpha -d alpha_tenant -c "select count(*) from tenants"        # 0
docker compose exec postgres psql -U alpha -d alpha_tag_catalog -c "select count(*) from tags"      # 0
docker compose exec postgres psql -U alpha -d alpha_tag_catalog -c "select count(*) from report_metric_definitions"  # 4 (reference data stays)
# Restore the flags, boot the demo stack, log in (form is EMPTY now), run a monthly report:
# availability on the Reports screen reflects actual runtime share of the month, not 99.5/98.5.
```
