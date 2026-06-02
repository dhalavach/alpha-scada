using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Npgsql;
using NpgsqlTypes;

namespace Alpha.Scada.Telemetry.Infrastructure;

public sealed class TelemetryRepository(NpgsqlDataSource dataSource)
{
    public async Task IngestAsync(TelemetryIngestRequest request, CancellationToken cancellationToken)
    {
        var count = request.Samples.Count;
        if (count == 0)
        {
            return;
        }

        var tagIds = new Guid[count];
        var tagKeys = new string[count];
        var timestamps = new DateTimeOffset[count];
        var values = new double[count];
        var qualities = new string[count];

        var index = 0;
        foreach (var sample in request.Samples)
        {
            tagIds[index] = sample.TagId;
            tagKeys[index] = sample.TagKey;
            timestamps[index] = sample.SourceTimestampUtc;
            values[index] = sample.Value;
            qualities[index] = sample.Quality;
            index++;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            insert into telemetry_samples (tenant_id, unit_id, tag_id, tag_key, timestamp_utc, value_double, quality, source_timestamp_utc, received_at_utc)
            select @tenant_id, @unit_id, s.tag_id, s.tag_key, s.timestamp_utc, s.value_double, s.quality, s.timestamp_utc, now()
            from unnest(@tag_ids, @tag_keys, @timestamps, @values, @qualities)
                as s(tag_id, tag_key, timestamp_utc, value_double, quality)
            on conflict (tag_id, timestamp_utc) do nothing;

            insert into tag_current (tenant_id, unit_id, tag_id, tag_key, value_double, quality, timestamp_utc)
            select distinct on (s.tag_id) @tenant_id, @unit_id, s.tag_id, s.tag_key, s.value_double, s.quality, s.timestamp_utc
            from unnest(@tag_ids, @tag_keys, @timestamps, @values, @qualities)
                as s(tag_id, tag_key, timestamp_utc, value_double, quality)
            order by s.tag_id, s.timestamp_utc desc
            on conflict (tag_id) do update
            set value_double = excluded.value_double,
                quality = excluded.quality,
                timestamp_utc = excluded.timestamp_utc;
            """, connection, tx);
        command.Parameters.AddWithValue("tenant_id", request.TenantId);
        command.Parameters.AddWithValue("unit_id", request.UnitId);
        command.Parameters.Add(new NpgsqlParameter("tag_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = tagIds });
        command.Parameters.Add(new NpgsqlParameter("tag_keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = tagKeys });
        command.Parameters.Add(new NpgsqlParameter("timestamps", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = timestamps });
        command.Parameters.Add(new NpgsqlParameter("values", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = values });
        command.Parameters.Add(new NpgsqlParameter("qualities", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = qualities });
        await command.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TagValueDto>> GetCurrentAsync(Guid unitId, CurrentUserDto user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select tag_id, tenant_id, unit_id, value_double, quality, timestamp_utc
            from tag_current
            where unit_id = @unit_id and (@is_support or tenant_id = @tenant_id)
            """, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        var results = new List<TagValueDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TagValueDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetDouble(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return results;
    }

    public async Task<IReadOnlyCollection<TelemetryHistoryPointDto>> GetHistoryAsync(Guid tagId, TimeSpan window, CurrentUserDto user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select timestamp_utc, value_double, quality
            from telemetry_samples
            where tag_id = @tag_id
              and timestamp_utc >= @cutoff
              and (@is_support or tenant_id = @tenant_id)
            order by timestamp_utc
            """, connection);
        command.Parameters.AddWithValue("tag_id", tagId);
        command.Parameters.AddWithValue("cutoff", DateTimeOffset.UtcNow.Subtract(window));
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        var results = new List<TelemetryHistoryPointDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TelemetryHistoryPointDto(reader.GetFieldValue<DateTimeOffset>(0), reader.GetDouble(1), reader.GetString(2)));
        }

        return results;
    }

    public async Task<ReportAggregateDto> GetReportAggregateAsync(Guid unitId, string period, CancellationToken cancellationToken)
    {
        var range = MonthPeriod.Parse(period);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            with minute_samples as (
                select tag_key, date_trunc('minute', timestamp_utc) as minute_utc, avg(value_double) as value_avg
                from telemetry_samples
                where unit_id = @unit_id
                  and timestamp_utc >= @period_start
                  and timestamp_utc < @period_end
                group by tag_key, date_trunc('minute', timestamp_utc)
            )
            select
                coalesce(sum(case when tag_key = 'engine.electrical_output_kw' then value_avg / 60.0 else 0 end), 0) as electrical_kwh,
                coalesce(sum(case when tag_key = 'heat.thermal_output_kw' then value_avg / 60.0 else 0 end), 0) as thermal_kwh,
                count(distinct minute_utc) / 60.0 as runtime_hours,
                coalesce(sum(case when tag_key = 'fuel.wood_chip_feed_kg_h' then value_avg / 60.0 else 0 end), 0) as wood_kg
            from minute_samples
            """, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("period_start", range.StartUtc);
        command.Parameters.AddWithValue("period_end", range.EndUtc);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new ReportAggregateDto(
            Math.Round(reader.GetDouble(0), 1),
            Math.Round(reader.GetDouble(1), 1),
            Math.Round(reader.GetDouble(2), 1),
            Math.Round(reader.GetDouble(3), 1));
    }
}
