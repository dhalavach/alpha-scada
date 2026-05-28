-- Run against alpha_telemetry to compare real and shadow telemetry rows.
select 'telemetry_missing_from_shadow' as check_name, count(*) as rows_different
from telemetry_samples real
left join telemetry_samples_shadow shadow
  on shadow.tag_id = real.tag_id
 and shadow.timestamp_utc = real.timestamp_utc
where real.received_at_utc >= now() - interval '30 seconds'
  and shadow.tag_id is null
union all
select 'telemetry_extra_in_shadow' as check_name, count(*) as rows_different
from telemetry_samples_shadow shadow
left join telemetry_samples real
  on real.tag_id = shadow.tag_id
 and real.timestamp_utc = shadow.timestamp_utc
where shadow.received_at_utc >= now() - interval '30 seconds'
  and real.tag_id is null;

-- Run against alpha_alarm to compare real and shadow alarm rows by unit/tag/message/state.
select 'alarm_missing_from_shadow' as check_name, count(*) as rows_different
from alarm_events real
left join alarm_events_shadow shadow
  on shadow.unit_id = real.unit_id
 and shadow.tag_id is not distinct from real.tag_id
 and shadow.message = real.message
 and shadow.state = real.state
where real.raised_at_utc >= now() - interval '30 seconds'
  and shadow.id is null
union all
select 'alarm_extra_in_shadow' as check_name, count(*) as rows_different
from alarm_events_shadow shadow
left join alarm_events real
  on real.unit_id = shadow.unit_id
 and real.tag_id is not distinct from shadow.tag_id
 and real.message = shadow.message
 and real.state = shadow.state
where shadow.raised_at_utc >= now() - interval '30 seconds'
  and real.id is null;
