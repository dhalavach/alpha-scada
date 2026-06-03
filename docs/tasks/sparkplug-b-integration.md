# TASK: Introduce Sparkplug B Support

**Status:** Draft — for review (not yet approved)
**Date:** 2026-05-29
**Revision:** v2 — incorporates technical review (corrects alias/STATE/QoS facts; adds rebirth-recovery, report-by-exception statefulness, historical-data handling, tenant isolation, and TCK conformance).
**Driver:** Prospective industrial energy customer deployments; broader interop with Ignition / Cirrus Link / Node-RED Sparkplug edge stacks.
**Spec basis:** Eclipse Sparkplug **3.0.0** (Nov 2022). See Sources.

---

## 1. Context & Goal

Today the platform ingests telemetry through the **NATS MQTT listener** using a custom JSON MQTT contract (`alpha/{tenantKey}/{siteKey}/{unitKey}/telemetry`, `TelemetryEnvelopeV1`) normalized by the Telemetry service. Sparkplug B is currently listed as a known gap in `system-overview.md`.

**Goal:** Accept telemetry from Sparkplug B–compliant edge nodes/devices **without rearchitecting the platform** — by translating Sparkplug into the existing canonical pipeline. The internal `TelemetryEnvelopeV1` contract and all downstream services (Telemetry, Alarm, Asset, Reporting, Gateway/SignalR) remain unchanged.

**Scope of outbound messages.** Cloud-to-device **control and setpoints stay deferred** — that means **DCMD** and any **NCMD that writes device metrics are out of scope** for v1. The **one exception is `NCMD Node Control/Rebirth`**, which the adapter must be able to publish: it is a benign request that asks a node to re-announce its BIRTH, and it is *required* for correct ingestion recovery (see §4.1 and finding in §5/Phase 1). It does not control the process, so it is consistent with "no cloud-to-device control."

---

## 2. Recommended Approach — Stateful adapter, not a rewrite

Add a **Sparkplug-to-canonical adapter** that subscribes to the Sparkplug namespace, decodes protobuf, and re-emits the existing internal `TelemetryEnvelopeV1` / `TelemetryBatchStored`-shaped flow. This keeps Sparkplug **optional per deployment** and isolates protobuf/state complexity in one place.

**This is a *stateful* translator, not a pure format shim.** Sparkplug is report-by-exception with aliasing (see §4.1), so the adapter must hold, per edge node/device:
- the **alias → metric-name** map learned from BIRTH, and
- the **last-known value** of every metric,

and **merge** each incoming DATA message against that state before emitting a canonical batch.

```
                          ┌──────────────────────────────────────────────┐
spBv1.0/# (protobuf) ────▶│ Sparkplug Adapter                            │
   NBIRTH/DBIRTH          │  • alias map + last-known-value per device   │──alpha/{t}/{s}/{u}/telemetry──▶ Telemetry ──▶ (unchanged downstream)
   NDATA/DDATA (RBE)      │  • merge RBE DATA against state              │
   NDEATH/DDEATH/Will ────│  • ID + metric mapping                       │── NDEATH/DDEATH ─▶ Asset (explicit unit offline)
                          │                                              │── NCMD Node Control/Rebirth ─▶ (recover missing BIRTH)
   STATE (JSON, retained) │  • optional Primary Host session            │◀─ STATE (JSON, retained) if Alpha is Primary Host
                          └──────────────────────────────────────────────┘
```

Mirrors the existing "Edge is an adapter host" framing. Downstream services don't know Sparkplug exists.

---

## 3. Sparkplug 3.0 conformance checklist (what we must honor)

| Area | Requirement | v1 handling |
|---|---|---|
| Topic namespace | `spBv1.0/{GroupID}/{MsgType}/{EdgeNodeID}/{DeviceID}` | Subscribe `spBv1.0/#` (or scoped groups) |
| Message types | NBIRTH, DBIRTH, NDATA, DDATA, NDEATH, DDEATH, STATE, NCMD, DCMD | Consume BIRTH/DATA/DEATH/STATE; publish **only** `NCMD Node Control/Rebirth`; **DCMD + device-write NCMD deferred** |
| Encoding | Google **Protobuf** for all payloads **except STATE** | Generate C# from `sparkplug_b.proto` (Eclipse Tahu) |
| **STATE message** | **UTF-8 JSON** `{"online":<bool>,"timestamp":<ms>}` on `spBv1.0/STATE/{HostID}`, **QoS 1, retain=true** — the *only* non-protobuf Sparkplug message | Parse/emit as JSON; **never** route through the protobuf decoder |
| Aliases | Metric **name→alias** declared in BIRTH; DATA may carry **alias only**. `alias` is a protobuf **uint64** (no fixed upper bound) | Cache alias map per node/device from BIRTH; resolve on every DATA |
| **Report-by-exception** | DATA carries **only metrics that changed** since the last report | Merge each DATA against per-device last-known state before emitting (see §4.1) |
| Session — node | NBIRTH/NDEATH carry a **bdSeq** metric (a `long`, conventionally wrapping 0–255 in Tahu) so a death correlates to its birth; MQTT **Will** = NDEATH | Track bdSeq; treat NDEATH/Will as offline |
| Sequence | Per-node **seq** (0–255, wrapping) — BIRTH seq=0, incremented on each subsequent N/DDATA | Validate continuity; on gap or unknown alias → publish **Rebirth NCMD** |
| Primary Host | Host publishes **STATE** (see above). Edge nodes configured with a Primary Host ID only publish while that host is online | Only if Alpha is the Primary Host *(decision §6.2)* |
| QoS / retain | BIRTH/DEATH **QoS 1, retain=false**; **DATA QoS 0**; STATE **QoS 1, retain=true** | Adapter MQTT client must set QoS/retain per message type |
| Complex datatypes | Scalars (INT8…UINT64, FLOAT, DOUBLE, BOOLEAN, STRING, DATETIME, TEXT, UUID, BYTES, FILE) **plus** Templates (UDTs), DataSets, and array types | Map numeric scalars→`value_double`; **decode-and-ignore** Templates/DataSets/arrays and non-numeric scalars in v1 *(decision §6.6)* |
| Historical data | Metrics may carry `is_historical=true` (store-and-forward backfill flushed on reconnect) | Route to **history only** — do **not** overwrite current value or raise live alarms (see §4) |

---

## 4. Mapping specification (Sparkplug → canonical)

| Sparkplug | Canonical | Notes |
|---|---|---|
| `GroupID` / `EdgeNodeID` / `DeviceID` | `tenantKey` / `siteKey` / `unitKey` | **Won't match natively** → needs a mapping (see §6.3) |
| Metric name (alias-resolved) | `tagKey` | Must align with Tag Catalog keys; unknown tags ignored (existing behavior) |
| Metric value (numeric scalar) | `value` (`value_double`) | Numeric only in v1; Templates/DataSets/arrays decoded-and-ignored |
| Metric timestamp (ms epoch) | `sourceTimestampUtc` | Convert to `DateTimeOffset` |
| Metric quality property | `quality` | Default "good"; map quality/STALE properties if present |
| Metric `is_historical=true` | **history only** | Backfill: persist to telemetry history; **must not** update `tag_current` or trigger live alarm evaluation |
| NBIRTH/DBIRTH | unit **online** + alias/metadata refresh | Establishes the alias map and full metric set for the device |
| NDEATH/DDEATH/Will | unit **offline** | Explicit signal → richer than Asset's timeout-based comm-loss monitor |

### 4.1 Report-by-exception + aliasing (the core complexity)

Sparkplug DATA messages are **not** self-contained: a DDATA typically carries **only the metrics that changed**, often **by numeric alias only** (no name). The platform's `TelemetryEnvelopeV1`, `tag_current`, and alarm evaluation all assume **fully-named, self-contained batches**. The adapter therefore must:

1. **Resolve aliases** to names using the map learned from the device's BIRTH.
2. **Merge** the changed metrics into the per-device **last-known-value** state.
3. **Emit** either the changed tags or a merged snapshot (decision below) as a canonical batch.

**Recovery / cold start:** BIRTH messages are published **retain=false**, so when the adapter starts (or first connects to already-running nodes) it has **no alias map** and **cannot decode alias-only DATA**. A healthy node will not spontaneously re-BIRTH. The spec's remedy — and the adapter's required behavior — is to publish **`NCMD Node Control/Rebirth`** to force the node to re-announce. This is why Rebirth is in scope (§1). Until a BIRTH (or rebirth) is received for a device, its DATA is buffered or dropped, not guessed.

**Open design decision (Phase 1):** when only a subset of metrics changes, does the adapter emit *only the changed tags* (cheaper, but alarm evaluation sees partial batches) or a *merged full snapshot* per emit (heavier, but matches current full-batch assumptions)? Recommend **merged snapshot** for v1 to keep downstream semantics identical.

---

## 5. Work breakdown

> Rough sizing is order-of-magnitude for one engineer; Primary Host support (Phase 2) roughly doubles that phase.

**Phase 0 — Spike / proof (1–2 days)**
- [ ] Generate C# from `sparkplug_b.proto`; decode a real NBIRTH+DDATA captured from Ignition/Tahu.
- [ ] Confirm transport choice: Sparkplug protobuf payloads are raw MQTT messages, so the Sparkplug listener should consume from the NATS MQTT/JetStream path and hand decoded batches to the canonical telemetry pipeline. Validate exact client choice. *(decision §6.4)*

**Phase 1 — Adapter ingestion, stateful (core — ~1–1.5 weeks)**
- [ ] New host project `Alpha.Scada.Edge.Sparkplug` (or extend Edge) — own MQTTnet client, `spBv1.0/#` subscription, correct per-type QoS.
- [ ] Protobuf decode + per-(group,node,device) **alias map** and **last-known-value** state built from BIRTH.
- [ ] **RBE merge:** resolve aliases, merge changed metrics into state, emit merged snapshot (see §4.1).
- [ ] **Cold-start recovery:** publish `NCMD Node Control/Rebirth` when a device's BIRTH/alias map is missing or a sequence gap is detected.
- [ ] ID mapping resolver (config/table) → tenant/site/unit keys.
- [ ] Metric→tag mapping; route `is_historical` to history-only; emit canonical `TelemetryEnvelopeV1` into existing ingestion.
- [ ] bdSeq/seq tracking; structured logging on gaps.

**Phase 2 — Lifecycle & state (~1 week; ~2 weeks if Primary Host)**
- [ ] NDEATH/DDEATH/Will → drive Asset unit-offline (new internal event or reuse status path).
- [ ] BIRTH → unit-online + metadata refresh.
- [ ] (If Primary Host, §6.2) publish retained **JSON STATE**; manage host online/offline, MQTT Will as offline STATE, and re-establish on reconnect.

**Phase 3 — Ops, security, docs (~3–5 days)**
- [ ] **Tenant isolation:** per-edge NATS/MQTT credentials and subject permissions so an edge can only publish into its own Group namespace (the Sparkplug Group space is flat; without this, one tenant's node could publish into another's group).
- [ ] Adapter ACLs for `spBv1.0/#`, STATE topic (if host), and Rebirth NCMD topics.
- [ ] Config flags to enable/disable Sparkplug per deployment; Compose + k3s wiring.
- [ ] **ADR** (`docs/architecture-decisions/00X-sparkplug-b.md`) + update `system-overview.md` (move out of Known Limitations) and README MQTT contract section.
- [ ] Metrics/dashboards: decoded msgs, alias-cache misses, rebirth requests, seq gaps, deaths.

---

## 6. Decisions needed from you (resolve during review)

1. **Adapter placement** — new `Alpha.Scada.Edge.Sparkplug` project, or extend the existing Edge service? *(Recommend: new project, optional per deployment.)*
2. **Is Alpha a Sparkplug Primary Host** (publishes JSON STATE, manages host session) or a **passive consumer**? *(Passive is simpler for v1; Primary Host is required if edge nodes are configured to gate publishing on consumer availability.)*
3. **ID mapping strategy** — convention (encode tenant/site/unit in Group/Node/Device IDs) vs a **mapping table** (in Asset or Tag Catalog). *(Recommend: mapping table; real-world Sparkplug IDs rarely match our keys.)*
4. **Transport** — confirm raw NATS/MQTT consumption for the Sparkplug side vs forcing it through Wolverine. *(Phase 0 validates.)*
5. **Outbound scope** — confirm DCMD and device-write NCMD stay deferred, while **`NCMD Node Control/Rebirth` is in scope** (required for recovery, §4.1). 
6. **Non-numeric / complex metrics** — decode-and-ignore in v1 (recommended), or store? Storing implies a non-`double` value path, which ripples into `TelemetryEnvelopeV1`, alarm evaluation, and the UI (all assume `double`) — a larger fork than a single column.
7. **RBE emit shape** — merged full snapshot per emit (recommended, preserves downstream semantics) vs changed-tags-only (cheaper, partial batches to alarm eval).

---

## 7. Acceptance criteria

- [ ] An Ignition/Tahu edge node publishing Sparkplug B appears in the UI with live values, history, and alarms — using only existing downstream services.
- [ ] Alias-only DDATA resolves and merges correctly against last-known state; **after an adapter cold start, a Rebirth NCMD recovers the alias map** and decoding resumes.
- [ ] RBE behavior verified: a DDATA with a single changed metric produces a correct canonical batch (merged snapshot) without corrupting other tags' current values.
- [ ] `is_historical` backfill lands in history only — it does not overwrite current values or raise live alarms.
- [ ] NDEATH/Will marks the unit offline within one cycle (faster than the timeout monitor).
- [ ] Sparkplug can be fully disabled by config with zero impact on the JSON contract path.
- [ ] Tenant isolation verified: an edge cannot publish into another tenant's Group namespace.
- [ ] **Conformance:** validate the implementation against the official **Eclipse Sparkplug TCK** (host/consumer profile) — not just home-grown tests.
- [ ] Unit/integration tests: protobuf decode, alias resolution, RBE merge, rebirth-on-cold-start, death→offline, sequence-gap handling.

## 8. Risks

- **Statefulness is the real cost.** RBE + aliasing + cold-start recovery make this a stateful translator with a recovery protocol, not a format shim — the bulk of the effort and the main correctness risk.
- Sparkplug protobuf should stay outside Wolverine's native envelope handling, which likely means a raw NATS/MQTT consumer feeding the canonical pipeline.
- Primary Host + JSON STATE + rebirth handling is the trickiest compliance surface; defer (passive consumer) if acceptable.
- High-frequency Sparkplug data + minute-grained, single-default-partition history → strengthens the case for **TimescaleDB** (currently deferred).
- ID/metric mapping is the main integration-friction point; needs client-specific config.

---

## Sources
- [Sparkplug Specification (Eclipse Foundation)](https://sparkplug.eclipse.org/specification/)
- [Sparkplug Version 3.0](https://sparkplug.eclipse.org/specification/version/3.0/)
- [Sparkplug 3.0.0 specification PDF](https://sparkplug.eclipse.org/specification/version/3.0/documents/sparkplug-specification-3.0.0.pdf)
- [Eclipse Tahu (reference impls + `sparkplug_b.proto`)](https://projects.eclipse.org/projects/iot.sparkplug)
- [Eclipse Sparkplug TCK (Technology Compatibility Kit)](https://github.com/eclipse-sparkplug/sparkplug/tree/master/tck)
