#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" != "--yes" ]]; then
  echo "This removes the built-in demo tenants, assets, tags, telemetry, alarms, reports, and users."
  echo "Re-run with --yes after taking a backup."
  exit 2
fi

psql_db() {
  local database="$1"
  local sql="$2"
  docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U alpha -d "$database" -c "$sql"
}

TENANTS="'10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002'"
UNITS="'30000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000002'"
SITES="'20000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000002'"
USERS="'40000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000002','40000000-0000-0000-0000-000000000003','40000000-0000-0000-0000-000000000004'"

psql_db alpha_reporting "delete from report_runs where tenant_id in ($TENANTS) or unit_id in ($UNITS);"
psql_db alpha_alarm "delete from alarm_outbox where payload->>'tenantId' in ($TENANTS); delete from alarm_events where tenant_id in ($TENANTS) or unit_id in ($UNITS);"
psql_db alpha_telemetry "delete from telemetry_samples where tenant_id in ($TENANTS) or unit_id in ($UNITS); delete from tag_current where tenant_id in ($TENANTS) or unit_id in ($UNITS);"
psql_db alpha_tag_catalog "delete from report_metric_bindings where tenant_id in ($TENANTS) or unit_id in ($UNITS); delete from report_profiles where tenant_id in ($TENANTS) or unit_id in ($UNITS); delete from tags where tenant_id in ($TENANTS) or unit_id in ($UNITS);"
psql_db alpha_asset "delete from units where id in ($UNITS); delete from sites where id in ($SITES);"
psql_db alpha_tenant "delete from tenants where id in ($TENANTS);"
psql_db alpha_identity "delete from users where id in ($USERS);"

echo "Demo data removed. Reference report metric definitions were retained."
