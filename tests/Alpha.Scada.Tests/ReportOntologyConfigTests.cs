/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/ReportOntologyConfigTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.Reporting.Application;
using Alpha.Scada.Reporting.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.TagCatalog.Infrastructure;
using Alpha.Scada.Telemetry.Infrastructure;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ReportOntologyConfigTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Tag_catalog_seeds_complete_report_profile_for_demo_unit()
    {
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new TagCatalogMigrator(dataSource, NullLogger<TagCatalogMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new TagCatalogRepository(dataSource);

            var profile = await repository.GetReportProfileAsync(TenantId, UnitId, CancellationToken.None);
            var missing = await repository.GetReportProfileAsync(TenantId, Guid.NewGuid(), CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.NotNull(profile);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Null(missing);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(99.5, profile.AvailabilityNoAlarmsPercent);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(98.5, profile.AvailabilityWithAlarmsPercent);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(0.00045, profile.BiocharYieldM3PerKg);
            Assert.Contains(profile.MetricBindings, binding => binding.MetricKey == ReportMetricKeys.ElectricalKwh);
            Assert.Contains(profile.MetricBindings, binding => binding.MetricKey == ReportMetricKeys.ThermalKwh);
            Assert.Contains(profile.MetricBindings, binding => binding.MetricKey == ReportMetricKeys.WoodChipsKg);
            Assert.Contains(profile.MetricBindings, binding => binding.MetricKey == ReportMetricKeys.RuntimeHours && binding.Threshold == 0);
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Telemetry_migrator_creates_timescale_storage_objects()
    {
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new TelemetryMigrator(dataSource, NullLogger<TelemetryMigrator>.Instance).MigrateAsync(CancellationToken.None);

            var hypertableCount = await ScalarLongAsync(
                connectionString,
                """
                select count(*)
                from timescaledb_information.hypertables
                where hypertable_schema = 'public'
                  and hypertable_name = 'telemetry_samples'
                """);
            var continuousAggregateCount = await ScalarLongAsync(
                connectionString,
                """
                select count(*)
                from timescaledb_information.continuous_aggregates
                where view_schema = 'public'
                  and view_name = 'telemetry_minute'
                """);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, hypertableCount);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, continuousAggregateCount);
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Telemetry_report_aggregate_uses_configured_tag_ids_not_tag_keys()
    {
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new TelemetryMigrator(dataSource, NullLogger<TelemetryMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new TelemetryRepository(dataSource);
            var electricalTagId = Guid.NewGuid();
            var alternateElectricalTagId = Guid.NewGuid();
            var thermalTagId = Guid.NewGuid();
            var woodTagId = Guid.NewGuid();
            var start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

// LEARN: starts a counted loop.
            for (var minute = 0; minute < 6; minute++)
            {
                var timestamp = start.AddMinutes(minute);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await repository.IngestAsync(
                    new TelemetryIngestRequest(TenantId, UnitId, [
                        Sample(electricalTagId, "not.the.electrical.key", 60, timestamp),
                        Sample(alternateElectricalTagId, "engine.electrical_output_kw", 30, timestamp),
                        Sample(thermalTagId, "also.not.the.thermal.key", 120, timestamp),
                        Sample(woodTagId, "also.not.the.wood.key", 30, timestamp)
                    ]),
                    CancellationToken.None);
            }

            var aggregate = await repository.GetReportAggregateAsync(
                UnitId,
                new ReportAggregateRequest("2026-06", [
                    Binding(ReportMetricKeys.ElectricalKwh, electricalTagId, "sum_per_minute", null),
                    Binding(ReportMetricKeys.ThermalKwh, thermalTagId, "sum_per_minute", null),
                    Binding(ReportMetricKeys.WoodChipsKg, woodTagId, "sum_per_minute", null),
                    Binding(ReportMetricKeys.RuntimeHours, electricalTagId, "runtime_hours", 0)
                ]),
                CancellationToken.None);

            var remappedAggregate = await repository.GetReportAggregateAsync(
                UnitId,
                new ReportAggregateRequest("2026-06", [
                    Binding(ReportMetricKeys.ElectricalKwh, alternateElectricalTagId, "sum_per_minute", null),
                    Binding(ReportMetricKeys.ThermalKwh, thermalTagId, "sum_per_minute", null),
                    Binding(ReportMetricKeys.WoodChipsKg, woodTagId, "sum_per_minute", null),
                    Binding(ReportMetricKeys.RuntimeHours, electricalTagId, "runtime_hours", 0)
                ]),
                CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(6, aggregate.ElectricalKwh);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(12, aggregate.ThermalKwh);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(3, aggregate.EstimatedWoodChipsKg);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(0.1, aggregate.RuntimeHours);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(3, remappedAggregate.ElectricalKwh);
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Reporting_service_uses_profile_factors_for_availability_and_biochar()
    {
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new ReportingMigrator(dataSource, NullLogger<ReportingMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var profile = new ReportProfileDto(
                TenantId,
                UnitId,
                97.7,
                91.2,
                0.002,
                [Binding(ReportMetricKeys.ElectricalKwh, Guid.NewGuid(), "sum_per_minute", null)]);
            var handler = new RoutingJsonHandler(request =>
            {
                var path = request.RequestUri?.AbsolutePath ?? "";
// LEARN: branches only when the boolean condition is true.
                if (path == $"/internal/v1/report-config/units/{UnitId}")
                {
                    return profile;
                }

// LEARN: branches only when the boolean condition is true.
                if (path == $"/internal/v1/telemetry/units/{UnitId}/report-aggregate")
                {
                    return new ReportAggregateDto(10, 20, 30, 500);
                }

// LEARN: branches only when the boolean condition is true.
                if (path == "/internal/v1/alarms/count")
                {
                    return 3;
                }

                return null;
            });
            var service = new ReportingService(new StaticHttpClientFactory(handler), new ReportingRepository(dataSource));

            var report = await service.RunQueuedMonthlyAsync(TenantId, UnitId, "2026-06", CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(91.2, report.AvailabilityPercent);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1.0, report.EstimatedBiocharM3);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(handler.AggregateRequests);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(profile.MetricBindings, handler.AggregateRequests.Single().MetricBindings);
        });
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static ResolvedTelemetrySample Sample(Guid tagId, string tagKey, double value, DateTimeOffset timestamp) =>
        new(tagId, tagKey, tagKey, "Report", "unit", null, null, value, "good", timestamp);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static ReportMetricBindingDto Binding(string metricKey, Guid tagId, string aggregationType, double? threshold) =>
        new(metricKey, tagId, aggregationType, 1, threshold);

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri($"http://{name}.test") };
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class RoutingJsonHandler(Func<HttpRequestMessage, object?> responder) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public List<ReportAggregateRequest> AggregateRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
// LEARN: branches only when the boolean condition is true.
            if (request.Content is not null && request.RequestUri?.AbsolutePath.Contains("/report-aggregate", StringComparison.Ordinal) == true)
            {
                var aggregateRequest = await request.Content.ReadFromJsonAsync<ReportAggregateRequest>(JsonOptions, cancellationToken);
// LEARN: branches only when the boolean condition is true.
                if (aggregateRequest is not null)
                {
                    AggregateRequests.Add(aggregateRequest);
                }
            }

            var response = responder(request);
// LEARN: branches only when the boolean condition is true.
            if (response is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response, options: JsonOptions)
            };
        }
    }

    private static async Task WithPostgresAsync(Func<string, Task> run)
    {
        var postgres = new ContainerBuilder()
            .WithImage(TestImages.Postgres)
            .WithEnvironment("POSTGRES_DB", "alpha_test")
            .WithEnvironment("POSTGRES_USER", "alpha")
            .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .Build();

// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await postgres.StartAsync();
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (DockerUnavailableException ex)
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await postgres.DisposeAsync();
            throw SkipException.ForSkip($"Docker is not available for report ontology integration test: {ex.Message}");
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (postgres)
        {
            var connectionString =
                $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await WaitForPostgresAsync(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await run(connectionString);
        }
    }

    private static async Task WaitForPostgresAsync(string connectionString)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
// LEARN: starts a loop that continues while its condition remains true.
        while (true)
        {
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
                await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await connection.OpenAsync();
                return;
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch when (DateTimeOffset.UtcNow < deadline)
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await Task.Delay(500);
            }
        }
    }

    private static async Task<long> ScalarLongAsync(string connectionString, string sql)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }
}
