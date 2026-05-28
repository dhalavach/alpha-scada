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
        var telemetry = httpClientFactory.CreateClient("telemetry");
        var aggregate = await telemetry.GetFromJsonAsync<ReportAggregateDto>(
            $"/internal/v1/telemetry/units/{unitId}/report-aggregate?period={period}",
            cancellationToken) ?? new ReportAggregateDto(0, 0, 0, 0);

        var alarm = httpClientFactory.CreateClient("alarm");
        var alarmCount = await alarm.GetFromJsonAsync<int>($"/internal/v1/alarms/count?unitId={unitId}&period={period}", cancellationToken);
        var availability = alarmCount > 0 ? 98.5 : 99.5;

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
            Math.Round(aggregate.EstimatedWoodChipsKg * 0.00045, 2),
            alarmCount,
            DateTimeOffset.UtcNow);

        return await repository.SaveAsync(report, cancellationToken);
    }
}
