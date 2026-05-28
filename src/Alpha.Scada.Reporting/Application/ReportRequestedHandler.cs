using Alpha.Scada.Reporting.Contracts;

namespace Alpha.Scada.Reporting.Application;

public sealed class ReportRequestedHandler(ReportingService service)
{
    public async Task<ReportCompleted> Handle(ReportRequested message, CancellationToken cancellationToken)
    {
        var report = await service.RunQueuedMonthlyAsync(
            message.TenantId,
            message.UnitId,
            message.Period,
            cancellationToken);

        return new ReportCompleted(
            message.RequestId,
            report.Id,
            report.TenantId,
            report.UnitId,
            report.Period,
            DateTimeOffset.UtcNow,
            message.CorrelationId);
    }
}
