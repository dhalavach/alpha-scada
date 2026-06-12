using Alpha.Scada.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Alpha.Scada.Tests;

[Collection(ContainerCollection.Name)]
public sealed class TelemetryHistoryTests(PostgresContainerFixture postgres)
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherTenantId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task History_uses_raw_then_minute_aggregate_and_preserves_tenant_scope()
    {
        var connectionString = await postgres.CreateDatabaseAsync(nameof(TelemetryHistoryTests));
        await WaitForPostgresAsync(connectionString);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new TelemetryMigrator(dataSource, NullLogger<TelemetryMigrator>.Instance).MigrateAsync(CancellationToken.None);
        var repository = new TelemetryRepository(dataSource);
        var start = DateTimeOffset.UtcNow.AddHours(-4).AddMinutes(1);
        var samples = Enumerable.Range(0, 240)
            .Select(index => Sample(start.AddMinutes(index), index, index % 2 == 0 ? "good" : "uncertain"))
            .ToArray();
        await repository.IngestAsync(new TelemetryIngestRequest(TenantId, UnitId, samples), CancellationToken.None);

        var owner = User(TenantId, Roles.Viewer);
        var raw = await repository.GetHistoryAsync(TagId, TimeSpan.FromMinutes(30), owner, CancellationToken.None);
        var aggregate = await repository.GetHistoryAsync(TagId, TimeSpan.FromHours(4), owner, CancellationToken.None);
        var hidden = await repository.GetHistoryAsync(TagId, TimeSpan.FromHours(4), User(OtherTenantId, Roles.Viewer), CancellationToken.None);
        var support = await repository.GetHistoryAsync(TagId, TimeSpan.FromHours(4), User(OtherTenantId, Roles.SupportEngineer), CancellationToken.None);

        Assert.NotEmpty(raw);
        Assert.All(raw, point => Assert.Contains(point.Quality, new[] { "good", "uncertain" }));
        Assert.NotEmpty(aggregate);
        Assert.All(aggregate, point => Assert.Equal("aggregated", point.Quality));
        Assert.True(IsChronological(aggregate));
        Assert.Empty(hidden);
        Assert.Equal(aggregate.Count, support.Count);
    }

    [Fact]
    public async Task Raw_history_returns_latest_two_thousand_points_in_chronological_order()
    {
        var connectionString = await postgres.CreateDatabaseAsync($"{nameof(TelemetryHistoryTests)}Cap");
        await WaitForPostgresAsync(connectionString);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new TelemetryMigrator(dataSource, NullLogger<TelemetryMigrator>.Instance).MigrateAsync(CancellationToken.None);
        var repository = new TelemetryRepository(dataSource);
        var start = DateTimeOffset.UtcNow.AddMinutes(-20);
        var samples = Enumerable.Range(0, TelemetryRepository.HistoryPointLimit + 105)
            .Select(index => Sample(start.AddMilliseconds(index * 500), index, "good"))
            .ToArray();
        await repository.IngestAsync(new TelemetryIngestRequest(TenantId, UnitId, samples), CancellationToken.None);

        var history = await repository.GetHistoryAsync(
            TagId,
            TimeSpan.FromMinutes(30),
            User(TenantId, Roles.Viewer),
            CancellationToken.None);

        Assert.Equal(TelemetryRepository.HistoryPointLimit, history.Count);
        Assert.True(IsChronological(history));
        Assert.Equal(105, history.First().Value);
        Assert.Equal(TelemetryRepository.HistoryPointLimit + 104, history.Last().Value);
    }

    private static ResolvedTelemetrySample Sample(DateTimeOffset timestamp, double value, string quality) =>
        new(TagId, "engine.output", "Output", "Engine", "kW", null, null, value, quality, timestamp);

    private static CurrentUserDto User(Guid tenantId, string role) =>
        new(Guid.NewGuid(), tenantId, "user@example.test", "User", role);

    private static bool IsChronological(IEnumerable<TelemetryHistoryPointDto> points)
    {
        var timestamps = points.Select(point => point.TimestampUtc).ToArray();
        return timestamps.SequenceEqual(timestamps.Order());
    }

    private static async Task WaitForPostgresAsync(string connectionString)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                return;
            }
            catch when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
        }
    }
}
