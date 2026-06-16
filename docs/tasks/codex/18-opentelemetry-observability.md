# Task 18 — OpenTelemetry: real metrics/tracing, retire the hand-rolled exporter, fix inert alerts

Read `README.md` in this folder first for repo context. **Land after Tasks 10 and 11** (they touch the same metrics code).

## Goal

All services expose `/metrics` via the OpenTelemetry Prometheus exporter; ingestion metrics use `System.Diagnostics.Metrics`; HTTP/DB/Wolverine traces exist (exported only when configured); every Prometheus alert references a metric that is actually emitted; scraping no longer runs ad-hoc SQL per request.

## Problem

- No tracing anywhere: `ServiceIdentity` derives correlation ids from `Activity.Current`, but nothing creates or exports activities.
- `/metrics` is a hand-built string (`src/Alpha.Scada.ServiceDefaults/MinimalApi.cs`) and **every scrape runs `select count(*) from wolverine.wolverine_dead_letters`** plus a Timescale row estimate — per service, per 15s scrape.
- `alpha_scada_telemetry_samples_written_total` is a gauge named like a counter, and is an approximate row count, not "written samples".
- `ops/prometheus/alerts.yml` alerts on `alpha_scada_wolverine_outbox_depth`, which **no code emits** — that alert can never fire. Two NATS rules are admitted-inert.
- `TelemetryIngestionMetrics` reimplements counters/histogram with `Interlocked` + manual exposition.

## Implementation steps

1. **Packages** (via `Directory.Packages.props`): `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `Npgsql.OpenTelemetry`, `OpenTelemetry.Exporter.Prometheus.AspNetCore` (prerelease acceptable — pin the version), `OpenTelemetry.Exporter.OpenTelemetryProtocol`. Test project: `Microsoft.Extensions.Diagnostics.Testing` (for `MetricCollector<T>`).
2. **`AddAlphaObservability(this WebApplicationBuilder builder, string serviceName)`** in ServiceDefaults:
   - Resource: `service.name = serviceName`.
   - Metrics: AspNetCore + HttpClient instrumentation, `AddMeter("Alpha.Scada.*")`, Wolverine's meter (Wolverine emits metrics natively — verify the meter name from the Wolverine docs/source at the pinned version and subscribe to it), Prometheus exporter. Add an explicit-bucket histogram View for the ingestion duration metric matching today's buckets `[0.01 … 10]`.
   - Tracing: AspNetCore + HttpClient + `AddNpgsql()` + Wolverine's ActivitySource; `AddOtlpExporter()` **only when** `OTEL_EXPORTER_OTLP_ENDPOINT` is set (off by default).
   - Call it from all nine `Program.cs` files.
3. **Replace the scrape endpoint**: in `AlphaOperationalEndpoints.MapAlphaOperationalEndpoints`, replace the `MinimalApi.MetricsAsync` route with `app.MapPrometheusScrapingEndpoint("/metrics")`; keep `/health` and `/ready` as-is. Delete `MinimalApi.MetricsAsync`, `IAlphaMetricsProvider`, and the `PrometheusLabels`/`EscapeLabel` helper (post-Task-10 location).
4. **Port `TelemetryIngestionMetrics`** to a `Meter("Alpha.Scada.Telemetry")`: one counter `alpha.scada.telemetry.ingestion.messages` with an `outcome` tag (replaces six fields), an UpDownCounter for in-flight, a Histogram for processing seconds, plus the max-deliveries and unknown-tag counters (Tasks 03/14). Keep the class's public API (`Begin()`/measurement `Complete(outcome)`/`Record*`) so the worker doesn't change. Same for the Alarm outbox gauges from Task 11: convert to `ObservableGauge`s on a `Meter("Alpha.Scada.Alarm")` backed by a cached sampler, not per-scrape SQL.
5. **Wolverine depth gauges, sampled**: a ServiceDefaults `BackgroundService` polling every 30s with two indexed counts (`wolverine_dead_letters`, `wolverine_outgoing_envelopes`) into `ObservableGauge`s `alpha.scada.wolverine.error_queue.depth` / `alpha.scada.wolverine.outbox.depth`, each tagged `service=<name>` to preserve alert-label continuity. Swallow-and-log when the tables don't exist yet (first boot).
6. **Fix the alert/dashboard layer**: scrape one service's `/metrics`, list the **actual exported names** (the OTel Prometheus exporter normalizes: dots→underscores, unit suffixes, `_total` on counters), then rewrite `ops/prometheus/alerts.yml` expressions to those names — the outbox-backlog alert must now reference the real gauge. Delete the two inert NATS rules, or (optional bonus) add a `prometheus-nats-exporter` sidecar to the compose `ops` profile + scrape config and keep them with corrected metric names. Update both Grafana dashboards (`ops/grafana/dashboards/*.json`) to the new names; drop panels for retired metrics (`alpha_scada_telemetry_samples_written_total`, `<service>_up` — Prometheus's own `up` series covers liveness).

## Tests

- Rewrite `TelemetryIngestionMetricsTests` against the Meter using `MetricCollector<long>`/`<double>`: outcome counter increments per outcome tag, histogram records, in-flight returns to zero.
- Update `ServiceDefaultsEndpointTests.Operational_endpoints_map_health_ready_and_metrics`: `/metrics` returns 200 and contains at least one `alpha_scada_` series and one `http_server_` series (don't over-assert exact names).
- A smoke assertion that `AddAlphaObservability` + `MapPrometheusScrapingEndpoint` boot together on a TestServer.

## Constraints

- Metric *semantics* preserved: every alert/dashboard query must have a working equivalent after the rename — include a before→after name table in the PR description.
- No OTLP collector container in this task; export stays opt-in via env var.
- Don't instrument the Edge simulator beyond the shared defaults.

## Verification

```bash
dotnet build Alpha.Scada.slnx && dotnet test Alpha.Scada.slnx
docker compose --profile ops up --build
curl -s localhost:5202/metrics | head -50          # OTel exposition, includes alpha_scada_* and http_server_* series
# Prometheus (ops profile) shows all 9 targets up; the outbox-depth alert expression returns data (value 0+, not "no data").
# Grafana messaging dashboard renders with live series.
# Hammer /metrics while watching pg_stat_activity: no per-scrape query storms (only the 30s sampler).
```
