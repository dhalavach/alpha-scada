/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Reporting/Application/ReportingService.cs
- Module role: Alpha.Scada.Reporting is the reporting service. It orchestrates monthly report generation by combining report ontology, telemetry aggregates, and alarm counts.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.Reporting.Infrastructure;
using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Reporting.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ReportingService(
// LEARN: performs an outbound HTTP call, usually to another service boundary.
    IHttpClientFactory httpClientFactory,
    ReportingRepository repository)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<MonthlyReportDto> RunMonthlyAsync(ReportRunRequest request, CurrentUserDto user, string authorizationHeader, CancellationToken cancellationToken)
    {
        var period = string.IsNullOrWhiteSpace(request.Period) ? DateTimeOffset.UtcNow.ToString("yyyy-MM") : request.Period;
        var asset = httpClientFactory.CreateClient(AlphaServiceClients.Asset);
        var unitRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{request.UnitId}");
        unitRequest.ForwardAuthorization(authorizationHeader);
        var unitResponse = await asset.SendAsync(unitRequest, cancellationToken);
// LEARN: branches only when the boolean condition is true.
        if (!unitResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Unit not found or not visible to the current user.");
        }

        var unit = await unitResponse.Content.ReadFromJsonAsync<UnitDto>(cancellationToken)
            ?? throw new InvalidOperationException("Unit not found.");

        return await GenerateMonthlyAsync(unit.TenantId, request.UnitId, period, cancellationToken);
    }

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<MonthlyReportDto> RunQueuedMonthlyAsync(Guid tenantId, Guid unitId, string period, CancellationToken cancellationToken) =>
        GenerateMonthlyAsync(tenantId, unitId, period, cancellationToken);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task<MonthlyReportDto> GenerateMonthlyAsync(Guid tenantId, Guid unitId, string period, CancellationToken cancellationToken)
    {
        var profile = await httpClientFactory.CreateClient(AlphaServiceClients.TagCatalog)
// LEARN: performs an outbound HTTP call, usually to another service boundary.
            .GetFromJsonAsync<ReportProfileDto>(
                $"/internal/v1/report-config/units/{unitId}?tenantId={tenantId}",
                cancellationToken)
            ?? throw new InvalidOperationException($"Report profile for unit {unitId} is not configured.");

        var telemetry = httpClientFactory.CreateClient(AlphaServiceClients.Telemetry);
        var aggregateResponse = await telemetry.PostAsJsonAsync(
            $"/internal/v1/telemetry/units/{unitId}/report-aggregate",
            new ReportAggregateRequest(period, profile.MetricBindings),
            cancellationToken);
        aggregateResponse.EnsureSuccessStatusCode();
        var aggregate = await aggregateResponse.Content.ReadFromJsonAsync<ReportAggregateDto>(cancellationToken)
            ?? throw new InvalidOperationException($"Telemetry aggregate for unit {unitId} was empty.");

        var alarm = httpClientFactory.CreateClient(AlphaServiceClients.Alarm);
        var alarmCount = await alarm.GetFromJsonAsync<int>($"/internal/v1/alarms/count?unitId={unitId}&period={period}", cancellationToken);
        var availability = alarmCount > 0 ? profile.AvailabilityWithAlarmsPercent : profile.AvailabilityNoAlarmsPercent;

        var report = new MonthlyReportDto(
            Guid.Empty,
            tenantId,
            unitId,
            period,
            aggregate.ElectricalKwh,
            aggregate.ThermalKwh,
            aggregate.RuntimeHours,
            availability,
            aggregate.EstimatedWoodChipsKg,
            Math.Round(aggregate.EstimatedWoodChipsKg * profile.BiocharYieldM3PerKg, 2),
            alarmCount,
            DateTimeOffset.UtcNow);

        return await repository.SaveAsync(report, cancellationToken);
    }
}
