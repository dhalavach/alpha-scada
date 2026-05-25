using Alpha.Scada.Api.Contracts;
using Alpha.Scada.Api.Modules.Auth;
using Npgsql;

namespace Alpha.Scada.Api.Data;

public sealed class PlatformRepository(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyCollection<TenantDto>> GetTenantsAsync(CurrentUser user, CancellationToken cancellationToken)
    {
        const string sql = """
            select id, key, name, region
            from tenants
            where @is_support or id = @tenant_id
            order by name
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", user.Role == Roles.SupportEngineer);

        var results = new List<TenantDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TenantDto(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return results;
    }

    public async Task<IReadOnlyCollection<SiteDto>> GetSitesAsync(CurrentUser user, CancellationToken cancellationToken)
    {
        const string sql = """
            select id, tenant_id, key, name, region, status
            from sites
            where @is_support or tenant_id = @tenant_id
            order by name
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", user.Role == Roles.SupportEngineer);

        var results = new List<SiteDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SiteDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return results;
    }

    public async Task<IReadOnlyCollection<UnitDto>> GetUnitsForSiteAsync(Guid siteId, CurrentUser user, CancellationToken cancellationToken)
    {
        const string sql = """
            select id, tenant_id, site_id, key, name, model, status, last_seen_utc
            from units
            where site_id = @site_id and (@is_support or tenant_id = @tenant_id)
            order by name
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", user.Role == Roles.SupportEngineer);

        return await ReadUnitsAsync(command, cancellationToken);
    }

    public async Task<UnitDto?> GetUnitAsync(Guid unitId, CurrentUser user, CancellationToken cancellationToken)
    {
        const string sql = """
            select id, tenant_id, site_id, key, name, model, status, last_seen_utc
            from units
            where id = @unit_id and (@is_support or tenant_id = @tenant_id)
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", user.Role == Roles.SupportEngineer);

        var units = await ReadUnitsAsync(command, cancellationToken);
        return units.FirstOrDefault();
    }

    public async Task<IReadOnlyCollection<TagCurrentDto>> GetCurrentTagsAsync(Guid unitId, CurrentUser user, CancellationToken cancellationToken)
    {
        const string sql = """
            select t.id, t.tenant_id, t.unit_id, t.key, t.name, t.subsystem, t.engineering_unit,
                   c.value_double, c.quality, c.timestamp_utc
            from tags t
            left join tag_current c on c.tag_id = t.id
            where t.unit_id = @unit_id and (@is_support or t.tenant_id = @tenant_id)
            order by t.subsystem, t.name
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", user.Role == Roles.SupportEngineer);

        var results = new List<TagCurrentDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TagCurrentDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(7) ? 0 : reader.GetDouble(7),
                reader.GetString(6),
                reader.IsDBNull(8) ? "stale" : reader.GetString(8),
                reader.IsDBNull(9) ? DateTimeOffset.MinValue : reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return results;
    }

    public async Task<IReadOnlyCollection<TelemetryHistoryPointDto>> GetHistoryAsync(Guid tagId, TimeSpan window, CurrentUser user, CancellationToken cancellationToken)
    {
        const string sql = """
            select s.timestamp_utc, s.value_double, s.quality
            from telemetry_samples s
            join tags t on t.id = s.tag_id
            where s.tag_id = @tag_id
              and s.timestamp_utc >= @cutoff
              and (@is_support or s.tenant_id = @tenant_id)
            order by s.timestamp_utc
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tag_id", tagId);
        command.Parameters.AddWithValue("cutoff", DateTimeOffset.UtcNow.Subtract(window));
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", user.Role == Roles.SupportEngineer);

        var results = new List<TelemetryHistoryPointDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TelemetryHistoryPointDto(
                reader.GetFieldValue<DateTimeOffset>(0),
                reader.GetDouble(1),
                reader.GetString(2)));
        }

        return results;
    }

    public async Task<IReadOnlyCollection<AlarmDto>> GetActiveAlarmsAsync(CurrentUser user, CancellationToken cancellationToken)
    {
        const string sql = """
            select id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
            from alarm_events
            where state in ('active', 'acknowledged') and (@is_support or tenant_id = @tenant_id)
            order by raised_at_utc desc
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", user.Role == Roles.SupportEngineer);

        return await ReadAlarmsAsync(command, cancellationToken);
    }

    public async Task<bool> AcknowledgeAlarmAsync(Guid alarmId, CurrentUser user, CancellationToken cancellationToken)
    {
        const string sql = """
            update alarm_events
            set state = 'acknowledged', acknowledged_at_utc = now(), acknowledged_by_user_id = @user_id
            where id = @alarm_id and state = 'active' and (@is_support or tenant_id = @tenant_id)
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("alarm_id", alarmId);
        command.Parameters.AddWithValue("user_id", user.UserId);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", user.Role == Roles.SupportEngineer);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (changed)
        {
            await WriteAuditAsync(user.TenantId, user.UserId, "alarm.acknowledged", $"Alarm {alarmId} acknowledged", cancellationToken);
        }

        return changed;
    }

    public async Task<MonthlyReportDto> GenerateMonthlyReportAsync(Guid unitId, string? period, CurrentUser user, CancellationToken cancellationToken)
    {
        var reportPeriod = string.IsNullOrWhiteSpace(period) ? DateTimeOffset.UtcNow.ToString("yyyy-MM") : period;
        var unit = await GetUnitAsync(unitId, user, cancellationToken) ?? throw new InvalidOperationException("Unit not found.");
        var report = await CalculateReportAsync(unit, reportPeriod, cancellationToken);

        const string sql = """
            insert into report_runs (id, tenant_id, unit_id, period, electrical_kwh, thermal_kwh, runtime_hours,
                                     availability_percent, estimated_wood_chips_kg, estimated_biochar_m3, alarm_count, generated_at_utc)
            values (@id, @tenant_id, @unit_id, @period, @electrical_kwh, @thermal_kwh, @runtime_hours,
                    @availability_percent, @wood, @biochar, @alarm_count, @generated_at_utc)
            on conflict (unit_id, period) do update
            set electrical_kwh = excluded.electrical_kwh,
                thermal_kwh = excluded.thermal_kwh,
                runtime_hours = excluded.runtime_hours,
                availability_percent = excluded.availability_percent,
                estimated_wood_chips_kg = excluded.estimated_wood_chips_kg,
                estimated_biochar_m3 = excluded.estimated_biochar_m3,
                alarm_count = excluded.alarm_count,
                generated_at_utc = excluded.generated_at_utc
            returning id
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        var id = report.Id == Guid.Empty ? Guid.NewGuid() : report.Id;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant_id", report.TenantId);
        command.Parameters.AddWithValue("unit_id", report.UnitId);
        command.Parameters.AddWithValue("period", report.Period);
        command.Parameters.AddWithValue("electrical_kwh", report.ElectricalKwh);
        command.Parameters.AddWithValue("thermal_kwh", report.ThermalKwh);
        command.Parameters.AddWithValue("runtime_hours", report.RuntimeHours);
        command.Parameters.AddWithValue("availability_percent", report.AvailabilityPercent);
        command.Parameters.AddWithValue("wood", report.EstimatedWoodChipsKg);
        command.Parameters.AddWithValue("biochar", report.EstimatedBiocharM3);
        command.Parameters.AddWithValue("alarm_count", report.AlarmCount);
        command.Parameters.AddWithValue("generated_at_utc", report.GeneratedAtUtc);
        var savedId = (Guid)(await command.ExecuteScalarAsync(cancellationToken) ?? id);

        return report with { Id = savedId };
    }

    public async Task<IReadOnlyCollection<MonthlyReportDto>> GetMonthlyReportsAsync(CurrentUser user, CancellationToken cancellationToken)
    {
        const string sql = """
            select id, tenant_id, unit_id, period, electrical_kwh, thermal_kwh, runtime_hours,
                   availability_percent, estimated_wood_chips_kg, estimated_biochar_m3, alarm_count, generated_at_utc
            from report_runs
            where @is_support or tenant_id = @tenant_id
            order by generated_at_utc desc
            limit 50
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", user.Role == Roles.SupportEngineer);

        var results = new List<MonthlyReportDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadReport(reader));
        }

        return results;
    }

    public async Task IngestTelemetryAsync(string tenantKey, string siteKey, string unitKey, EdgeTelemetryEnvelope envelope, CancellationToken cancellationToken)
    {
        const string unitSql = """
            select u.id, u.tenant_id
            from units u
            join sites s on s.id = u.site_id
            join tenants t on t.id = u.tenant_id
            where t.key = @tenant_key and s.key = @site_key and u.key = @unit_key
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await using var unitCommand = new NpgsqlCommand(unitSql, connection, tx);
        unitCommand.Parameters.AddWithValue("tenant_key", tenantKey);
        unitCommand.Parameters.AddWithValue("site_key", siteKey);
        unitCommand.Parameters.AddWithValue("unit_key", unitKey);

        Guid unitId;
        Guid tenantId;
        await using (var reader = await unitCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"MQTT topic is not allow-listed for {tenantKey}/{siteKey}/{unitKey}.");
            }

            unitId = reader.GetGuid(0);
            tenantId = reader.GetGuid(1);
        }

        foreach (var sample in envelope.Samples)
        {
            await IngestSampleAsync(connection, tx, tenantId, unitId, sample, cancellationToken);
        }

        await using var statusCommand = new NpgsqlCommand(
            "update units set status = 'online', last_seen_utc = now() where id = @unit_id",
            connection,
            tx);
        statusCommand.Parameters.AddWithValue("unit_id", unitId);
        await statusCommand.ExecuteNonQueryAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    public async Task EvaluateAlarmsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            insert into alarm_events (id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc)
            select gen_random_uuid(), t.tenant_id, t.unit_id, t.id,
                   case when t.subsystem = 'Safety' then 'critical' else 'warning' end,
                   case
                     when c.quality <> 'good' then t.name || ' quality is ' || c.quality
                     when t.alarm_low is not null and c.value_double < t.alarm_low then t.name || ' below low threshold'
                     when t.alarm_high is not null and c.value_double > t.alarm_high then t.name || ' above high threshold'
                   end,
                   'active',
                   now()
            from tags t
            join tag_current c on c.tag_id = t.id
            where (
                c.quality <> 'good'
                or (t.alarm_low is not null and c.value_double < t.alarm_low)
                or (t.alarm_high is not null and c.value_double > t.alarm_high)
            )
            and not exists (
                select 1 from alarm_events a
                where a.tag_id = t.id and a.state in ('active', 'acknowledged')
            );

            update alarm_events a
            set state = 'cleared', cleared_at_utc = now()
            from tags t
            join tag_current c on c.tag_id = t.id
            where a.tag_id = t.id
              and a.state in ('active', 'acknowledged')
              and c.quality = 'good'
              and (t.alarm_low is null or c.value_double >= t.alarm_low)
              and (t.alarm_high is null or c.value_double <= t.alarm_high);

            insert into alarm_events (id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc)
            select gen_random_uuid(), u.tenant_id, u.id, null, 'critical', u.name || ' communication lost', 'active', now()
            from units u
            where u.last_seen_utc < now() - interval '2 minutes'
              and not exists (
                  select 1 from alarm_events a
                  where a.unit_id = u.id and a.tag_id is null and a.state in ('active', 'acknowledged')
              );
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SeedUser?> GetUserByEmailAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = """
            select id, tenant_id, email, display_name, password_hash, role
            from users
            where lower(email) = lower(@email)
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("email", email);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new SeedUser(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5))
            : null;
    }

    public async Task CreateSessionAsync(Guid userId, string tokenHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        const string sql = "insert into sessions (token_hash, user_id, expires_at_utc) values (@token_hash, @user_id, @expires)";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("expires", expiresAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteSessionAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("delete from sessions where token_hash = @token_hash", connection);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CurrentUser?> GetUserBySessionAsync(string tokenHash, CancellationToken cancellationToken)
    {
        const string sql = """
            select u.id, u.tenant_id, u.email, u.display_name, u.role
            from sessions s
            join users u on u.id = s.user_id
            where s.token_hash = @token_hash and s.expires_at_utc > now()
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CurrentUser(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4))
            : null;
    }

    public async Task WriteAuditAsync(Guid? tenantId, Guid? userId, string eventType, string message, CancellationToken cancellationToken)
    {
        const string sql = """
            insert into audit_events (id, tenant_id, user_id, event_type, message, created_at_utc)
            values (gen_random_uuid(), @tenant_id, @user_id, @event_type, @message, now())
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", (object?)tenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("user_id", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("message", message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task IngestSampleAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenantId, Guid unitId, EdgeTelemetrySample sample, CancellationToken cancellationToken)
    {
        const string sql = """
            with tag_match as (
                select id from tags
                where tenant_id = @tenant_id and unit_id = @unit_id and key = @tag_key
            ),
            sample_insert as (
                insert into telemetry_samples (tenant_id, unit_id, tag_id, timestamp_utc, value_double, quality, source_timestamp_utc, received_at_utc)
                select @tenant_id, @unit_id, id, @timestamp_utc, @value, @quality, @source_timestamp_utc, now()
                from tag_match
                on conflict (tag_id, timestamp_utc) do nothing
                returning tag_id
            )
            insert into tag_current (tenant_id, unit_id, tag_id, value_double, quality, timestamp_utc)
            select @tenant_id, @unit_id, id, @value, @quality, @timestamp_utc
            from tag_match
            on conflict (tag_id) do update
            set value_double = excluded.value_double,
                quality = excluded.quality,
                timestamp_utc = excluded.timestamp_utc
            """;

        await using var command = new NpgsqlCommand(sql, connection, tx);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("tag_key", sample.TagKey);
        command.Parameters.AddWithValue("timestamp_utc", sample.SourceTimestampUtc);
        command.Parameters.AddWithValue("source_timestamp_utc", sample.SourceTimestampUtc);
        command.Parameters.AddWithValue("value", sample.Value);
        command.Parameters.AddWithValue("quality", sample.Quality);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<MonthlyReportDto> CalculateReportAsync(UnitDto unit, string period, CancellationToken cancellationToken)
    {
        const string sql = """
            select
                coalesce(sum(case when t.key = 'engine.electrical_output_kw' then s.value_double / 60.0 else 0 end), 0) as electrical_kwh,
                coalesce(sum(case when t.key = 'heat.thermal_output_kw' then s.value_double / 60.0 else 0 end), 0) as thermal_kwh,
                count(distinct date_trunc('minute', s.timestamp_utc)) / 60.0 as runtime_hours,
                coalesce(sum(case when t.key = 'fuel.wood_chip_feed_kg_h' then s.value_double / 60.0 else 0 end), 0) as wood_kg,
                (select count(*) from alarm_events a where a.unit_id = @unit_id and to_char(a.raised_at_utc, 'YYYY-MM') = @period) as alarm_count
            from telemetry_samples s
            join tags t on t.id = s.tag_id
            where s.unit_id = @unit_id and to_char(s.timestamp_utc, 'YYYY-MM') = @period
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("unit_id", unit.Id);
        command.Parameters.AddWithValue("period", period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        var electrical = reader.GetDouble(0);
        var thermal = reader.GetDouble(1);
        var runtime = reader.GetDouble(2);
        var wood = reader.GetDouble(3);
        var alarmCount = reader.GetInt32(4);
        var availability = alarmCount > 0 ? 98.5 : 99.5;

        return new MonthlyReportDto(
            Guid.Empty,
            unit.TenantId,
            unit.Id,
            period,
            Math.Round(electrical, 1),
            Math.Round(thermal, 1),
            Math.Round(runtime, 1),
            availability,
            Math.Round(wood, 1),
            Math.Round(wood * 0.00045, 2),
            alarmCount,
            DateTimeOffset.UtcNow);
    }

    private static async Task<IReadOnlyCollection<UnitDto>> ReadUnitsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var results = new List<UnitDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new UnitDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return results;
    }

    private static async Task<IReadOnlyCollection<AlarmDto>> ReadAlarmsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var results = new List<AlarmDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new AlarmDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return results;
    }

    private static MonthlyReportDto ReadReport(NpgsqlDataReader reader)
    {
        return new MonthlyReportDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetDouble(4),
            reader.GetDouble(5),
            reader.GetDouble(6),
            reader.GetDouble(7),
            reader.GetDouble(8),
            reader.GetDouble(9),
            reader.GetInt32(10),
            reader.GetFieldValue<DateTimeOffset>(11));
    }
}

public sealed record SeedUser(Guid Id, Guid TenantId, string Email, string DisplayName, string PasswordHash, string Role);

public static class Roles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";
    public const string SupportEngineer = "SupportEngineer";
}
