using Alpha.Scada.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;

namespace Alpha.Scada.Telemetry.Application;

public sealed class TelemetryService(TelemetryRepository repository)
{
    public Task IngestAsync(TelemetryIngestRequest request, CancellationToken cancellationToken) =>
        repository.IngestAsync(request, cancellationToken);

    public Task<IReadOnlyCollection<TagValueDto>> GetCurrentAsync(Guid unitId, CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetCurrentAsync(unitId, user, cancellationToken);

    public Task<IReadOnlyCollection<TelemetryHistoryPointDto>> GetHistoryAsync(Guid tagId, TimeSpan window, CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetHistoryAsync(tagId, window, user, cancellationToken);

    public Task<ReportAggregateDto> GetReportAggregateAsync(Guid unitId, string period, CancellationToken cancellationToken) =>
        repository.GetReportAggregateAsync(unitId, period, cancellationToken);
}
