using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.Reporting.Infrastructure;
using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Reporting.Application;

public sealed class ReportingService(
    IHttpClientFactory httpClientFactory,
    ReportingRepository repository)
{
    public Task<MonthlyReportDto> RunQueuedMonthlyAsync(Guid tenantId, Guid unitId, string period, CancellationToken cancellationToken) =>
        GenerateMonthlyAsync(tenantId, unitId, period, cancellationToken);

    private async Task<MonthlyReportDto> GenerateMonthlyAsync(Guid tenantId, Guid unitId, string period, CancellationToken cancellationToken)
    {
        var profile = await httpClientFactory.CreateClient(AlphaServiceClients.TagCatalog)
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
