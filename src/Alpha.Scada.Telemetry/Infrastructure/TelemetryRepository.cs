using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Npgsql;
using NpgsqlTypes;

namespace Alpha.Scada.Telemetry.Infrastructure;

public sealed class TelemetryRepository
{
    private readonly NpgsqlDataSource dataSource;

    public TelemetryRepository(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
    }

    public async Task IngestAsync(
        TelemetryIngestRequest request,
        CancellationToken cancellationToken)
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
                timestamp_utc = excluded.timestamp_utc
            where tag_current.timestamp_utc <= excluded.timestamp_utc;
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

    public async Task<ReportAggregateDto> GetReportAggregateAsync(Guid unitId, ReportAggregateRequest request, CancellationToken cancellationToken)
    {
        ValidateReportBindings(request.MetricBindings);
        var range = MonthPeriod.Parse(request.Period);
        var bindings = request.MetricBindings.ToArray();
        var metricKeys = bindings.Select(binding => binding.MetricKey).ToArray();
        var tagIds = bindings.Select(binding => binding.TagId).ToArray();
        var aggregationTypes = bindings.Select(binding => binding.AggregationType).ToArray();
        var scales = bindings.Select(binding => binding.Scale).ToArray();
        var thresholds = bindings.Select(binding => binding.Threshold ?? 0).ToArray();
        var hasThresholds = bindings.Select(binding => binding.Threshold.HasValue).ToArray();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            with bindings as (
                select *
                from unnest(@metric_keys, @tag_ids, @aggregation_types, @scales, @thresholds, @has_thresholds)
                    as b(metric_key, tag_id, aggregation_type, scale, threshold, has_threshold)
            ),
            minute_samples as (
                select tag_id, date_trunc('minute', timestamp_utc) as minute_utc, avg(value_double) as value_avg
                from telemetry_samples
                where unit_id = @unit_id
                  and timestamp_utc >= @period_start
                  and timestamp_utc < @period_end
                  and tag_id = any(@tag_ids)
                group by tag_id, date_trunc('minute', timestamp_utc)
            )
            select b.metric_key,
                   case b.aggregation_type
                       when 'sum_per_minute' then coalesce(sum(m.value_avg / 60.0 * b.scale), 0)
                       when 'runtime_hours' then count(distinct m.minute_utc) filter (where not b.has_threshold or m.value_avg > b.threshold) / 60.0 * b.scale
                   end as metric_value
            from bindings b
            left join minute_samples m on m.tag_id = b.tag_id
            group by b.metric_key, b.aggregation_type, b.scale, b.threshold, b.has_threshold
            """, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("period_start", range.StartUtc);
        command.Parameters.AddWithValue("period_end", range.EndUtc);
        command.Parameters.Add(new NpgsqlParameter("metric_keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = metricKeys });
        command.Parameters.Add(new NpgsqlParameter("tag_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = tagIds });
        command.Parameters.Add(new NpgsqlParameter("aggregation_types", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = aggregationTypes });
        command.Parameters.Add(new NpgsqlParameter("scales", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = scales });
        command.Parameters.Add(new NpgsqlParameter("thresholds", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = thresholds });
        command.Parameters.Add(new NpgsqlParameter("has_thresholds", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = hasThresholds });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var valuesByMetric = new Dictionary<string, double>();
        while (await reader.ReadAsync(cancellationToken))
        {
            valuesByMetric[reader.GetString(0)] = reader.GetDouble(1);
        }

        return new ReportAggregateDto(
            Math.Round(valuesByMetric.GetValueOrDefault(ReportMetricKeys.ElectricalKwh), 1),
            Math.Round(valuesByMetric.GetValueOrDefault(ReportMetricKeys.ThermalKwh), 1),
            Math.Round(valuesByMetric.GetValueOrDefault(ReportMetricKeys.RuntimeHours), 1),
            Math.Round(valuesByMetric.GetValueOrDefault(ReportMetricKeys.WoodChipsKg), 1));
    }

    private static void ValidateReportBindings(IReadOnlyCollection<ReportMetricBindingDto> bindings)
    {
        var missing = new[]
        {
            ReportMetricKeys.ElectricalKwh,
            ReportMetricKeys.ThermalKwh,
            ReportMetricKeys.RuntimeHours,
            ReportMetricKeys.WoodChipsKg
        }.Where(metric => bindings.All(binding => binding.MetricKey != metric)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Report aggregate config is missing metric bindings: {string.Join(", ", missing)}.");
        }

        var unsupported = bindings
            .Where(binding => binding.AggregationType is not ("sum_per_minute" or "runtime_hours"))
            .Select(binding => $"{binding.MetricKey}:{binding.AggregationType}")
            .ToArray();
        if (unsupported.Length > 0)
        {
            throw new InvalidOperationException($"Report aggregate config contains unsupported aggregation types: {string.Join(", ", unsupported)}.");
        }
    }
}
