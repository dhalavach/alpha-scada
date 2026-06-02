using System.Net.Http.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.Reporting.Infrastructure;
using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Reporting.Application;

public sealed class ReportingService(
    IHttpClientFactory httpClientFactory,
    ReportingRepository repository)
{
    public async Task<MonthlyReportDto> RunMonthlyAsync(ReportRunRequest request, CurrentUserDto user, string authorizationHeader, CancellationToken cancellationToken)
    {
        var period = string.IsNullOrWhiteSpace(request.Period) ? DateTimeOffset.UtcNow.ToString("yyyy-MM") : request.Period;
        var asset = httpClientFactory.CreateClient("asset");
        var unitRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/units/{request.UnitId}");
        unitRequest.ForwardAuthorization(authorizationHeader);
        var unitResponse = await asset.SendAsync(unitRequest, cancellationToken);
        if (!unitResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Unit not found or not visible to the current user.");
        }

        var unit = await unitResponse.Content.ReadFromJsonAsync<UnitDto>(cancellationToken)
            ?? throw new InvalidOperationException("Unit not found.");

        return await GenerateMonthlyAsync(unit.TenantId, request.UnitId, period, cancellationToken);
    }

    public Task<MonthlyReportDto> RunQueuedMonthlyAsync(Guid tenantId, Guid unitId, string period, CancellationToken cancellationToken) =>
        GenerateMonthlyAsync(tenantId, unitId, period, cancellationToken);

    private async Task<MonthlyReportDto> GenerateMonthlyAsync(Guid tenantId, Guid unitId, string period, CancellationToken cancellationToken)
    {
        var profile = await httpClientFactory.CreateClient("tagCatalog")
            .GetFromJsonAsync<ReportProfileDto>(
                $"/internal/v1/report-config/units/{unitId}?tenantId={tenantId}",
                cancellationToken)
            ?? throw new InvalidOperationException($"Report profile for unit {unitId} is not configured.");

        var telemetry = httpClientFactory.CreateClient("telemetry");
        var aggregateResponse = await telemetry.PostAsJsonAsync(
            $"/internal/v1/telemetry/units/{unitId}/report-aggregate",
            new ReportAggregateRequest(period, profile.MetricBindings),
            cancellationToken);
        aggregateResponse.EnsureSuccessStatusCode();
        var aggregate = await aggregateResponse.Content.ReadFromJsonAsync<ReportAggregateDto>(cancellationToken)
            ?? throw new InvalidOperationException($"Telemetry aggregate for unit {unitId} was empty.");

        var alarm = httpClientFactory.CreateClient("alarm");
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
