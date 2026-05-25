# Floyd SCADA: MVP To Complete Platform Roadmap

Status: review draft  
Date: 2026-05-25  
Purpose: separate the simple MVP from the complete platform so implementation can start small and grow deliberately.

## 1. Core Decision

Implement the **Simple MVP first**.

Use the previous full architecture as the **Complete Version target**, not as the starting point.

That means:

- MVP proves value for one F60 unit.
- MVP avoids platform complexity.
- Complete Version adds multi-site, multi-tenant, regional, resilient, and advanced analytics capabilities after the MVP is working.

## 2. Stage Overview

| Stage | Name | Goal | Build Now? |
|---|---|---|---|
| 0 | Discovery | Confirm F60 controller, tags, gateway, demo goals | Yes |
| 1 | Simple MVP | One F60 monitored end-to-end with alarms, trends, report | Yes |
| 2 | Production Pilot | Harden MVP for first real customer operation | After MVP |
| 3 | Complete Platform | Multi-tenant, multi-site, regional SCADA platform | Later |
| 4 | Advanced Energy Platform | BESS, MRV, PdM, predictive control, integrations | Later |

## 3. Stage 0: Discovery

Duration: 2-5 days.

Goal: prevent architecture guesses.

Confirm:

- Actual F60 controller.
- Exposed protocol: OPC UA, Modbus TCP, S7, Beckhoff ADS, Schneider, or other.
- Available tag list.
- Safety signals exposed to SCADA.
- Whether a Linux gateway exists.
- Whether demo needs real hardware or simulator first.
- Whether Japanese labels are needed for the first demo.
- Whether customer demo requires BESS or only CHP.

Exit gate:

- We can read live or simulated F60 data.
- MVP scope is frozen.

## 4. Stage 1: Simple MVP

Duration: 6-10 weeks.  
Team: 3-4 people.  
Estimated effort: 80-160 PD.

Architecture:

```text
F60 controller
  -> Node-RED edge gateway
  -> Mosquitto
  -> one ASP.NET Core backend
  -> PostgreSQL
  -> React/Vite HMI
```

Build:

- Docker Compose deployment.
- F60 simulator.
- Node-RED edge flow or small edge connector.
- MQTT publish path.
- One ASP.NET Core backend service.
- PostgreSQL schema.
- Web HMI.
- Current values and history.
- Threshold alarms.
- Email notification.
- Monthly PDF report.
- Local users.
- Basic backup/restore note.

Screens:

- Login.
- F60 overview.
- Live tag list.
- Trends.
- Active alarms.
- Monthly report.
- Basic admin/config.

Data model:

```text
unit
  subsystem
    tag
```

Keep `tenant_name` and `site_name` as simple labels only. Do not build full multi-tenancy or full site hierarchy yet.

MVP non-goals:

- Kubernetes.
- TimescaleDB.
- EMQX.
- Sparkplug B strict compliance.
- OIDC/SSO.
- Full RBAC.
- Multi-tenancy.
- Multi-region.
- BESS workflows.
- Site hierarchy.
- Fuel inventory workflow.
- Biochar MRV workflow.
- Ticketing.
- Predictive maintenance.
- Remote control.
- OTA updates.

Exit gate:

- A live or simulated F60 can be monitored from browser.
- Alarms work.
- Trends work.
- Monthly report works.
- Demo can be shown to Floyd/customer.

## 5. Stage 2: Production Pilot

Duration: 6-10 weeks after MVP.

Goal: turn the demo into something safe enough for first customer operation without jumping to the complete platform.

Add:

- Hardened edge agent if Node-RED is not acceptable for field operation.
- Edge store-and-forward.
- Better reconnect behavior.
- Per-unit credentials.
- OIDC if required by first customer.
- Role split: Admin, Operator, Viewer.
- Audit log for login/config changes.
- Better backup/restore.
- Basic Prometheus/Grafana.
- F60 tag template.
- Simple maintenance notes.
- Simple fuel checks.
- Simple biochar entries.
- Load test for 5-20 simulated units.

Architecture evolution:

```text
Node-RED edge
  -> optional Go edge agent

Plain MQTT payloads
  -> stable versioned telemetry schema

Local users
  -> local users + optional OIDC

single-unit model
  -> unit template + optional site label
```

Still defer:

- HA.
- Multi-region.
- Full site hierarchy.
- Full multi-tenancy.
- Carbon-credit MRV.
- PdM models.
- Remote control.

Exit gate:

- First customer pilot can run with documented operations and support.

## 6. Stage 3: Complete Platform

Duration: 4-6 months after pilot validation.

Goal: grow the system into the full architecture described in:

[floyd-open-scada-architecture-plan.md](/Users/kopfmann/Documents/scada/floyd-open-scada-architecture-plan.md)

Add:

- Full tenant model.
- Full site model.
- Multiple units per site.
- F60 asset template versioning.
- Multi-region deployment target.
- k3s or managed Kubernetes.
- TimescaleDB or stronger time-series partitioning.
- Sparkplug B compliance if fleet/device-state semantics justify it.
- Hardened Go edge agent.
- Edge store-and-forward by default.
- Cloud store-and-forward.
- OIDC as standard.
- Fixed RBAC as standard.
- Audit log expansion.
- Fuel-lot tracking.
- Biochar-lot tracking.
- Maintenance/support workflow.
- Better reporting.
- Observability stack.
- 20+ unit load testing.

Architecture target:

```text
tenant
  site
    unit
      subsystem
        tag
```

Complete Version is where `site-first` becomes real. It should not be forced into Stage 1.

Exit gate:

- Platform can support Floyd plus first customer tenant with strict isolation.
- Platform can support multiple sites/units.
- Deployment and restore are repeatable.
- Operations team can support field units.

## 7. Stage 4: Advanced Energy Platform

Duration: depends on customer demand.

Goal: move beyond SCADA into energy optimization and carbon/maintenance value.

Add only when there is a real buyer:

- BESS monitoring and optimization.
- Peak-shaving analytics.
- Biochar/carbon-credit MRV workflow.
- J-Credit / METI or other regulatory reports.
- Predictive maintenance models.
- Fleet learning.
- CMMS integration.
- ERP/BI integrations.
- Advisory predictive control.
- Closed-loop control only after safety review.
- HA/multi-region active-active if SLA demands it.

## 8. Component Evolution

| Component | Stage 1 MVP | Stage 2 Pilot | Stage 3 Complete |
|---|---|---|---|
| Edge | Node-RED or tiny connector | Hardened Go agent optional | Go edge agent standard |
| Protocols | OPC UA/Modbus, S7 if confirmed | Same, stabilized | Driver plugin model |
| MQTT | Mosquitto, simple schema | Versioned schema | Sparkplug B if justified |
| Backend | One ASP.NET Core service | One service, better modules | Split services only when needed |
| Database | PostgreSQL | PostgreSQL with partitions | TimescaleDB/partition strategy |
| UI | F60 screens | F60 + pilot ops screens | Fleet/site/tenant screens |
| Auth | Local users | OIDC optional | OIDC standard |
| Tenancy | Labels only | Light tenant boundary | Full tenant isolation |
| Site model | Label only | Optional site label | Full site hierarchy |
| Reports | One monthly PDF | Better report inputs | Report templates |
| Alarms | Threshold + email | More config | Routing/escalation later |
| Deployment | Docker Compose | Compose + runbooks | k3s/managed Kubernetes |
| Observability | Logs + health | Basic metrics | Prometheus/Grafana/Loki |

## 9. Implementation Rule

Do not build a Stage 3 feature during Stage 1 unless it is required to avoid rework and costs less than one day.

Good early hooks:

- Use UUID primary keys.
- Keep `tenant_name` and `site_name` labels.
- Put all tag definitions in config.
- Version the telemetry payload.
- Keep backend modules internally separated.
- Keep UI routes clean.

Do not build early:

- Tenant isolation.
- Site hierarchy.
- Kubernetes.
- Sparkplug B.
- TimescaleDB.
- Full RBAC.
- Report designer.
- Workflow engines.

## 10. Recommended Next Step

Start implementation from:

[floyd-open-scada-simple-plan.md](/Users/kopfmann/Documents/scada/floyd-open-scada-simple-plan.md)

Use the complete architecture document only as a north star:

[floyd-open-scada-architecture-plan.md](/Users/kopfmann/Documents/scada/floyd-open-scada-architecture-plan.md)

The practical path is:

```text
Simple MVP
  -> Production Pilot
  -> Complete Platform
  -> Advanced Energy Platform
```
