/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/ReportOntologyConfigTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net.Http.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Reporting.Application;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Reporting.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.TagCatalog.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Builders;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Containers;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Logging.Abstractions;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Xunit.Sdk;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ReportOntologyConfigTests
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Tag_catalog_seeds_complete_report_profile_for_demo_unit()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new TagCatalogMigrator(dataSource, NullLogger<TagCatalogMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repository = new TagCatalogRepository(dataSource);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var profile = await repository.GetReportProfileAsync(TenantId, UnitId, CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Assert.Contains(profile.MetricBindings, binding => binding.MetricKey == ReportMetricKeys.ElectricalKwh);
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Assert.Contains(profile.MetricBindings, binding => binding.MetricKey == ReportMetricKeys.ThermalKwh);
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Assert.Contains(profile.MetricBindings, binding => binding.MetricKey == ReportMetricKeys.WoodChipsKg);
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Assert.Contains(profile.MetricBindings, binding => binding.MetricKey == ReportMetricKeys.RuntimeHours && binding.Threshold == 0);
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Telemetry_migrator_creates_timescale_storage_objects()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new TelemetryMigrator(dataSource, NullLogger<TelemetryMigrator>.Instance).MigrateAsync(CancellationToken.None);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var hypertableCount = await ScalarLongAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                connectionString,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                """
                select count(*)
                from timescaledb_information.hypertables
                where hypertable_schema = 'public'
                  and hypertable_name = 'telemetry_samples'
                """);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var continuousAggregateCount = await ScalarLongAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                connectionString,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Telemetry_report_aggregate_uses_configured_tag_ids_not_tag_keys()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new TelemetryMigrator(dataSource, NullLogger<TelemetryMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repository = new TelemetryRepository(dataSource);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var electricalTagId = Guid.NewGuid();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var alternateElectricalTagId = Guid.NewGuid();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var thermalTagId = Guid.NewGuid();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var woodTagId = Guid.NewGuid();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

// LEARN: starts a counted loop.
            for (var minute = 0; minute < 6; minute++)
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var timestamp = start.AddMinutes(minute);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await repository.IngestAsync(
// LEARN: creates a new object or record instance.
                    new TelemetryIngestRequest(TenantId, UnitId, [
// LEARN: continues an argument/object/collection initializer onto the next line.
                        Sample(electricalTagId, "not.the.electrical.key", 60, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
                        Sample(alternateElectricalTagId, "engine.electrical_output_kw", 30, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
                        Sample(thermalTagId, "also.not.the.thermal.key", 120, timestamp),
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                        Sample(woodTagId, "also.not.the.wood.key", 30, timestamp)
// LEARN: continues an argument/object/collection initializer onto the next line.
                    ]),
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
                    CancellationToken.None);
            }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var aggregate = await repository.GetReportAggregateAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                UnitId,
// LEARN: creates a new object or record instance.
                new ReportAggregateRequest("2026-06", [
// LEARN: continues an argument/object/collection initializer onto the next line.
                    Binding(ReportMetricKeys.ElectricalKwh, electricalTagId, "sum_per_minute", null),
// LEARN: continues an argument/object/collection initializer onto the next line.
                    Binding(ReportMetricKeys.ThermalKwh, thermalTagId, "sum_per_minute", null),
// LEARN: continues an argument/object/collection initializer onto the next line.
                    Binding(ReportMetricKeys.WoodChipsKg, woodTagId, "sum_per_minute", null),
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                    Binding(ReportMetricKeys.RuntimeHours, electricalTagId, "runtime_hours", 0)
// LEARN: continues an argument/object/collection initializer onto the next line.
                ]),
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
                CancellationToken.None);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var remappedAggregate = await repository.GetReportAggregateAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                UnitId,
// LEARN: creates a new object or record instance.
                new ReportAggregateRequest("2026-06", [
// LEARN: continues an argument/object/collection initializer onto the next line.
                    Binding(ReportMetricKeys.ElectricalKwh, alternateElectricalTagId, "sum_per_minute", null),
// LEARN: continues an argument/object/collection initializer onto the next line.
                    Binding(ReportMetricKeys.ThermalKwh, thermalTagId, "sum_per_minute", null),
// LEARN: continues an argument/object/collection initializer onto the next line.
                    Binding(ReportMetricKeys.WoodChipsKg, woodTagId, "sum_per_minute", null),
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                    Binding(ReportMetricKeys.RuntimeHours, electricalTagId, "runtime_hours", 0)
// LEARN: continues an argument/object/collection initializer onto the next line.
                ]),
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Reporting_service_uses_profile_factors_for_availability_and_biochar()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new ReportingMigrator(dataSource, NullLogger<ReportingMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var profile = new ReportProfileDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
                TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                97.7,
// LEARN: continues an argument/object/collection initializer onto the next line.
                91.2,
// LEARN: continues an argument/object/collection initializer onto the next line.
                0.002,
// LEARN: executes one C# statement; semicolons terminate most statements.
                [Binding(ReportMetricKeys.ElectricalKwh, Guid.NewGuid(), "sum_per_minute", null)]);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var handler = new RoutingJsonHandler(request =>
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var path = request.RequestUri?.AbsolutePath ?? "";
// LEARN: branches only when the boolean condition is true.
                if (path == $"/internal/v1/report-config/units/{UnitId}")
                {
// LEARN: returns a value or exits the current method.
                    return profile;
                }

// LEARN: branches only when the boolean condition is true.
                if (path == $"/internal/v1/telemetry/units/{UnitId}/report-aggregate")
                {
// LEARN: returns a value or exits the current method.
                    return new ReportAggregateDto(10, 20, 30, 500);
                }

// LEARN: branches only when the boolean condition is true.
                if (path == "/internal/v1/alarms/count")
                {
// LEARN: returns a value or exits the current method.
                    return 3;
                }

// LEARN: returns a value or exits the current method.
                return null;
// LEARN: executes one C# statement; semicolons terminate most statements.
            });
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var service = new ReportingService(new StaticHttpClientFactory(handler), new ReportingRepository(dataSource));

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var report = await service.RunQueuedMonthlyAsync(TenantId, UnitId, "2026-06", CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(91.2, report.AvailabilityPercent);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1.0, report.EstimatedBiocharM3);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(handler.AggregateRequests);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(profile.MetricBindings, handler.AggregateRequests.Single().MetricBindings);
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static ResolvedTelemetrySample Sample(Guid tagId, string tagKey, double value, DateTimeOffset timestamp) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        new(tagId, tagKey, tagKey, "Report", "unit", null, null, value, "good", timestamp);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static ReportMetricBindingDto Binding(string metricKey, Guid tagId, string aggregationType, double? threshold) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        new(metricKey, tagId, aggregationType, 1, threshold);

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public HttpClient CreateClient(string name) =>
// LEARN: creates a new object or record instance.
            new(handler, disposeHandler: false) { BaseAddress = new Uri($"http://{name}.test") };
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class RoutingJsonHandler(Func<HttpRequestMessage, object?> responder) : HttpMessageHandler
    {
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public List<ReportAggregateRequest> AggregateRequests { get; } = [];

// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
// LEARN: branches only when the boolean condition is true.
            if (request.Content is not null && request.RequestUri?.AbsolutePath.Contains("/report-aggregate", StringComparison.Ordinal) == true)
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var aggregateRequest = await request.Content.ReadFromJsonAsync<ReportAggregateRequest>(JsonOptions, cancellationToken);
// LEARN: branches only when the boolean condition is true.
                if (aggregateRequest is not null)
                {
// LEARN: executes one C# statement; semicolons terminate most statements.
                    AggregateRequests.Add(aggregateRequest);
                }
            }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var response = responder(request);
// LEARN: branches only when the boolean condition is true.
            if (response is null)
            {
// LEARN: returns a value or exits the current method.
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

// LEARN: returns a value or exits the current method.
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                Content = JsonContent.Create(response, options: JsonOptions)
            };
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task WithPostgresAsync(Func<string, Task> run)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var postgres = new ContainerBuilder()
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithImage(TestImages.Postgres)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithEnvironment("POSTGRES_DB", "alpha_test")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithEnvironment("POSTGRES_USER", "alpha")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithPortBinding(5432, true)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
// LEARN: executes one C# statement; semicolons terminate most statements.
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
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw SkipException.ForSkip($"Docker is not available for report ontology integration test: {ex.Message}");
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (postgres)
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var connectionString =
// LEARN: executes one C# statement; semicolons terminate most statements.
                $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await WaitForPostgresAsync(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await run(connectionString);
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task WaitForPostgresAsync(string connectionString)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: returns a value or exits the current method.
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

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<long> ScalarLongAsync(string connectionString, string sql)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand(sql, connection);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var value = await command.ExecuteScalarAsync();
// LEARN: returns a value or exits the current method.
        return Convert.ToInt64(value);
    }
}
