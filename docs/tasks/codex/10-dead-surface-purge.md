# Task 10 — Dead-surface purge

Read `README.md` in this folder first for repo context.

## Goal

Delete every symbol, table, and contract that has zero production callers, and consolidate the three duplicated helpers. Pure deletions/consolidation — if removing something breaks the build or a test, it wasn't dead; stop and re-check rather than forcing it.

**Method requirement:** before deleting each item, run `grep -rn "<symbol>" --include="*.cs" src tests` and confirm the only hits are the definition (and, where noted, tests that test the dead thing itself — delete those tests with it).

## Inventory

### A. Dead methods / overloads

The repositories grew paired overloads — `(connection, transaction)` variants plus "convenience" own-transaction variants — and the convenience variants have no callers:

| Symbol | Location |
|---|---|
| `AlarmRepository.EvaluateAsync(AlarmEvaluationRequest, CancellationToken)` (own-tx overload) | `src/Alpha.Scada.Alarm/Infrastructure/AlarmRepository.cs:19` |
| `AlarmRepository.RaiseCommunicationLostAsync(UnitDto, CancellationToken)` (own-tx overload) | same file ~line 98 |
| `AlarmRepository.AcknowledgeAsync(Guid, CurrentUserDto, CancellationToken)` (own-tx overload) | same file ~line 172 |
| `AssetRepository.SetUnitOnlineAsync(Guid, CancellationToken)` (own-tx overload) | `src/Alpha.Scada.Asset/Infrastructure/AssetRepository.cs:101` |
| `AssetRepository.MarkStaleUnitsOfflineAsync(int, CancellationToken)` (own-tx overload) | same file ~line 139 |
| `AlarmService.RaiseCommunicationLostAsync(UnitDto, CancellationToken)` (2-arg delegating overload) | `src/Alpha.Scada.Alarm/Application/AlarmService.cs:54` |
| `AssetService.SetUnitOnlineAsync(Guid, CancellationToken)` (2-arg delegating overload) | `src/Alpha.Scada.Asset/Application/AssetService.cs:46` |
| `ReportingService.RunMonthlyAsync(...)` (never mapped to any endpoint) | `src/Alpha.Scada.Reporting/Application/ReportingService.cs:12` |
| `RoleRules.CanManageConfiguration` | `src/Alpha.Scada.Contracts/Auth/AuthContracts.cs` |

Note: `TelemetryRepository.IngestAsync(request, ct)` (own-tx) **is used** (via `TelemetryService`) — leave it.

### B. Dead contracts and topic helpers

- `EdgeStatusEnvelope` — never produced or consumed (`src/Alpha.Scada.Contracts/Edge/EdgeContracts.cs`).
- `EdgeTelemetryEnvelope` / `EdgeTelemetrySample` — byte-identical duplicates of `TelemetryEnvelopeV1` / `TelemetrySampleV1` (`src/Alpha.Scada.Contracts/Messaging/TelemetryEnvelopeV1.cs`). Switch `ChpUnitSimulatorWorker` to `TelemetryEnvelopeV1` (constructor: `new TelemetryEnvelopeV1(TelemetryEnvelopeV1.SchemaVersion, unitKey, now, samples)`; the `[JsonPropertyName("schemaVersion")]` attribute keeps the wire field identical, so the adapter parses it unchanged), then delete `EdgeContracts.cs` entirely. Check `tests/Alpha.Scada.Tests/ContractTests.cs` and `TelemetryPrimaryIngestionTests.cs` for references and migrate them to `TelemetryEnvelopeV1`.
- In `src/Alpha.Scada.ServiceDefaults/Messaging/Topics.cs`, grep each member and delete the unused ones. Expected dead (verify each): `EdgeMqttTelemetry`, `EdgeMqttStatus`, `AlarmWildcard`, `TelemetryStoredWildcard`, and the per-unit subject helpers `Status(...)`, `AlarmRaised(...)`, `AlarmCleared(...)`, `AlarmAcknowledged(...)`, `TelemetryStored(...)` (domain events publish to the constant subjects, not per-unit ones). Expected **alive**: `Telemetry(...)` (simulator), `TelemetryWildcard`, `SparkplugWildcard`, `StatusWildcard` (used by `NatsSecurityTests`), all `*Event` constants, stream names, `Dlq(...)`, `DlqWildcard`. If `StatusWildcard` survives only because the test's `CreateEdgeStreamAsync` includes it while the production stream definition does not, align the test's stream subjects with `AlphaMessaging.ConfigureNats` and then delete `StatusWildcard` too.

### C. Dead schema

- `edge_devices` table: `credential_hash` etc. are never read or written. Delete `src/Alpha.Scada.Edge/Infrastructure/EdgeMigrator.cs`, remove `AddAlphaMigrator<EdgeMigrator>()` and the `ApplyAlphaMigrationsAsync` call from `src/Alpha.Scada.Edge/Program.cs` (keep `AddServiceDatabase` + `MapAlphaOperationalEndpoints` — `/ready` still probes Postgres). Add a one-line note to `docs/dev-setup.md`: existing dev volumes retain the orphaned `edge_devices` table; `drop table if exists edge_devices` or recreate the volume.

### D. Duplicated helpers → consolidate

- SHA-256 deterministic message id: `ChpUnitSimulatorWorker.DeterministicMessageId` and `TelemetryAdapterIngestionWorker.ResolveMessageId`'s hashing block are the same algorithm (subject + 0x1f + payload → SHA256 → Guid). Extract `public static string DeterministicMessageId(string subject, ReadOnlySpan<byte> payload)` into a new `src/Alpha.Scada.ServiceDefaults/Messaging/MessageIds.cs`; use it from both.
- `EscapeLabel`: duplicated in `MinimalApi.cs` and `TelemetryIngestionMetrics.cs`. Extract `internal static class PrometheusLabels { public static string Escape(string value) ... }` in ServiceDefaults and use it from both. (Task 18 will delete this entirely; consolidation still pays for itself until then.)

## Constraints

- No behavior changes. The simulator's wire output must remain byte-compatible (same JSON field names, same message-id derivation).
- Do not delete `RoleRules.IsSupport`/`CanAcknowledge`/`IsService` or anything in active use.
- One commit per inventory section (A–D) so review is mechanical.

## Verification

```bash
dotnet build Alpha.Scada.slnx    # 0 warnings (TreatWarningsAsErrors after Task 08)
dotnet test Alpha.Scada.slnx     # green
# Stack check: simulator still ingests (UI live values), proving the envelope/message-id swap is wire-compatible:
docker compose up --build
# Each deleted symbol:
grep -rn "EdgeTelemetryEnvelope\|EdgeStatusEnvelope\|CanManageConfiguration\|RunMonthlyAsync\|GetStaleUnitsAsync\|EdgeMqtt" --include="*.cs" src tests   # no hits
```
