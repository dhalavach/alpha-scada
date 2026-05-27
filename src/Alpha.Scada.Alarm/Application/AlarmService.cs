using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Contracts;

namespace Alpha.Scada.Alarm.Application;

public sealed class AlarmService(AlarmRepository repository)
{
    public Task EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken) =>
        repository.EvaluateAsync(request, cancellationToken);

    public Task RaiseCommunicationLostAsync(UnitDto unit, CancellationToken cancellationToken) =>
        repository.RaiseCommunicationLostAsync(unit, cancellationToken);

    public Task<IReadOnlyCollection<AlarmDto>> GetActiveAsync(CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetActiveAsync(user, cancellationToken);

    public Task<bool> AcknowledgeAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.AcknowledgeAsync(alarmId, user, cancellationToken);

    public Task<int> CountForUnitPeriodAsync(Guid unitId, string period, CancellationToken cancellationToken) =>
        repository.CountForUnitPeriodAsync(unitId, period, cancellationToken);
}
