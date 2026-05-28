using Alpha.Scada.Contracts;
using Npgsql;

namespace Alpha.Scada.Telemetry.Infrastructure;

public sealed class TelemetryRepository(NpgsqlDataSource dataSource)
{
    public async Task IngestAsync(TelemetryIngestRequest request, CancellationToken cancellationToken)
    {
        await IngestIntoAsync("telemetry_samples", "tag_current", request, cancellationToken);
    }

    public async Task IngestShadowAsync(TelemetryIngestRequest request, CancellationToken cancellationToken)
    {
        await IngestIntoAsync("telemetry_samples_shadow", "tag_current_shadow", request, cancellationToken);
    }

    private async Task IngestIntoAsync(
        string samplesTable,
        string currentTable,
        TelemetryIngestRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var sample in request.Samples)
        {
            await using var command = new NpgsqlCommand($"""
                insert into {samplesTable} (tenant_id, unit_id, tag_id, tag_key, timestamp_utc, value_double, quality, source_timestamp_utc, received_at_utc)
                values (@tenant_id, @unit_id, @tag_id, @tag_key, @timestamp_utc, @value, @quality, @source_timestamp_utc, now())
                on conflict (tag_id, timestamp_utc) do nothing;

                insert into {currentTable} (tenant_id, unit_id, tag_id, tag_key, value_double, quality, timestamp_utc)
                values (@tenant_id, @unit_id, @tag_id, @tag_key, @value, @quality, @timestamp_utc)
                on conflict (tag_id) do update
                set value_double = excluded.value_double,
                    quality = excluded.quality,
                    timestamp_utc = excluded.timestamp_utc;
                """, connection, tx);
            command.Parameters.AddWithValue("tenant_id", request.TenantId);
            command.Parameters.AddWithValue("unit_id", request.UnitId);
            command.Parameters.AddWithValue("tag_id", sample.TagId);
            command.Parameters.AddWithValue("tag_key", sample.TagKey);
            command.Parameters.AddWithValue("timestamp_utc", sample.SourceTimestampUtc);
            command.Parameters.AddWithValue("source_timestamp_utc", sample.SourceTimestampUtc);
            command.Parameters.AddWithValue("value", sample.Value);
            command.Parameters.AddWithValue("quality", sample.Quality);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

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
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            with minute_samples as (
                select tag_key, date_trunc('minute', timestamp_utc) as minute_utc, avg(value_double) as value_avg
                from telemetry_samples
                where unit_id = @unit_id and to_char(timestamp_utc, 'YYYY-MM') = @period
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
        command.Parameters.AddWithValue("period", period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new ReportAggregateDto(
            Math.Round(reader.GetDouble(0), 1),
            Math.Round(reader.GetDouble(1), 1),
            Math.Round(reader.GetDouble(2), 1),
            Math.Round(reader.GetDouble(3), 1));
    }
}
