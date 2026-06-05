/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Reporting/Application/ReportingService.cs
- Module role: Alpha.Scada.Reporting is the reporting service. It orchestrates monthly report generation by combining report ontology, telemetry aggregates, and alarm counts.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net.Http.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Reporting.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Reporting.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ReportingService(
// LEARN: performs an outbound HTTP call, usually to another service boundary.
    IHttpClientFactory httpClientFactory,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    ReportingRepository repository)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<MonthlyReportDto> RunMonthlyAsync(ReportRunRequest request, CurrentUserDto user, string authorizationHeader, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var period = string.IsNullOrWhiteSpace(request.Period) ? DateTimeOffset.UtcNow.ToString("yyyy-MM") : request.Period;
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var asset = httpClientFactory.CreateClient(AlphaServiceClients.Asset);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var unitRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{request.UnitId}");
// LEARN: executes one C# statement; semicolons terminate most statements.
        unitRequest.ForwardAuthorization(authorizationHeader);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var unitResponse = await asset.SendAsync(unitRequest, cancellationToken);
// LEARN: branches only when the boolean condition is true.
        if (!unitResponse.IsSuccessStatusCode)
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new InvalidOperationException("Unit not found or not visible to the current user.");
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var unit = await unitResponse.Content.ReadFromJsonAsync<UnitDto>(cancellationToken)
// LEARN: creates a new object or record instance.
            ?? throw new InvalidOperationException("Unit not found.");

// LEARN: returns a value or exits the current method.
        return await GenerateMonthlyAsync(unit.TenantId, request.UnitId, period, cancellationToken);
    }

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<MonthlyReportDto> RunQueuedMonthlyAsync(Guid tenantId, Guid unitId, string period, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        GenerateMonthlyAsync(tenantId, unitId, period, cancellationToken);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task<MonthlyReportDto> GenerateMonthlyAsync(Guid tenantId, Guid unitId, string period, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var profile = await httpClientFactory.CreateClient(AlphaServiceClients.TagCatalog)
// LEARN: performs an outbound HTTP call, usually to another service boundary.
            .GetFromJsonAsync<ReportProfileDto>(
// LEARN: continues an argument/object/collection initializer onto the next line.
                $"/internal/v1/report-config/units/{unitId}?tenantId={tenantId}",
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                cancellationToken)
// LEARN: creates a new object or record instance.
            ?? throw new InvalidOperationException($"Report profile for unit {unitId} is not configured.");

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var telemetry = httpClientFactory.CreateClient(AlphaServiceClients.Telemetry);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var aggregateResponse = await telemetry.PostAsJsonAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
            $"/internal/v1/telemetry/units/{unitId}/report-aggregate",
// LEARN: creates a new object or record instance.
            new ReportAggregateRequest(period, profile.MetricBindings),
// LEARN: executes one C# statement; semicolons terminate most statements.
            cancellationToken);
// LEARN: executes one C# statement; semicolons terminate most statements.
        aggregateResponse.EnsureSuccessStatusCode();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var aggregate = await aggregateResponse.Content.ReadFromJsonAsync<ReportAggregateDto>(cancellationToken)
// LEARN: creates a new object or record instance.
            ?? throw new InvalidOperationException($"Telemetry aggregate for unit {unitId} was empty.");

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var alarm = httpClientFactory.CreateClient(AlphaServiceClients.Alarm);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var alarmCount = await alarm.GetFromJsonAsync<int>($"/internal/v1/alarms/count?unitId={unitId}&period={period}", cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var availability = alarmCount > 0 ? profile.AvailabilityWithAlarmsPercent : profile.AvailabilityNoAlarmsPercent;

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var report = new MonthlyReportDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
            Guid.Empty,
// LEARN: continues an argument/object/collection initializer onto the next line.
            tenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            unitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            period,
// LEARN: continues an argument/object/collection initializer onto the next line.
            aggregate.ElectricalKwh,
// LEARN: continues an argument/object/collection initializer onto the next line.
            aggregate.ThermalKwh,
// LEARN: continues an argument/object/collection initializer onto the next line.
            aggregate.RuntimeHours,
// LEARN: continues an argument/object/collection initializer onto the next line.
            availability,
// LEARN: continues an argument/object/collection initializer onto the next line.
            aggregate.EstimatedWoodChipsKg,
// LEARN: continues an argument/object/collection initializer onto the next line.
            Math.Round(aggregate.EstimatedWoodChipsKg * profile.BiocharYieldM3PerKg, 2),
// LEARN: continues an argument/object/collection initializer onto the next line.
            alarmCount,
// LEARN: executes one C# statement; semicolons terminate most statements.
            DateTimeOffset.UtcNow);

// LEARN: returns a value or exits the current method.
        return await repository.SaveAsync(report, cancellationToken);
    }
}
