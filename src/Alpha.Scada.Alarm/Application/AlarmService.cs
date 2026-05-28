using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Contracts;
using Wolverine;

namespace Alpha.Scada.Alarm.Application;

public sealed class AlarmService(AlarmRepository repository, UnitKeyResolver unitKeyResolver, IMessageBus messageBus)
{
    public async Task EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken)
    {
        var changes = await repository.EvaluateAsync(request, cancellationToken);
        foreach (var alarm in changes.Raised)
        {
            await PublishRaisedAsync(alarm, cancellationToken);
        }

        foreach (var alarm in changes.Cleared)
        {
            await PublishClearedAsync(alarm, cancellationToken);
        }
    }

    public async Task RaiseCommunicationLostAsync(UnitDto unit, CancellationToken cancellationToken)
    {
        var alarm = await repository.RaiseCommunicationLostAsync(unit, cancellationToken);
        if (alarm is not null)
        {
            await PublishRaisedAsync(alarm, cancellationToken);
        }
    }

    public Task<IReadOnlyCollection<AlarmDto>> GetActiveAsync(CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetActiveAsync(user, cancellationToken);

    public async Task<bool> AcknowledgeAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken)
    {
        var alarm = await repository.AcknowledgeAsync(alarmId, user, cancellationToken);
        if (alarm is null)
        {
            return false;
        }

        var route = await unitKeyResolver.ResolveAsync(alarm.UnitId, cancellationToken);
        await messageBus.PublishAsync(new AlarmAcknowledged(
            alarm.Id,
            alarm.TenantId,
            alarm.UnitId,
            alarm.TagId,
            route.TenantKey,
            route.SiteKey,
            route.UnitKey,
            user.UserId,
            alarm.AcknowledgedAtUtc ?? DateTimeOffset.UtcNow));
        return true;
    }

    public Task<int> CountForUnitPeriodAsync(Guid unitId, string period, CancellationToken cancellationToken) =>
        repository.CountForUnitPeriodAsync(unitId, period, cancellationToken);

    private async Task PublishRaisedAsync(AlarmDto alarm, CancellationToken cancellationToken)
    {
        var route = await unitKeyResolver.ResolveAsync(alarm.UnitId, cancellationToken);
        await messageBus.PublishAsync(new AlarmRaised(
            alarm.Id,
            alarm.TenantId,
            alarm.UnitId,
            alarm.TagId,
            route.TenantKey,
            route.SiteKey,
            route.UnitKey,
            alarm.Severity,
            alarm.Message,
            alarm.RaisedAtUtc));
    }

    private async Task PublishClearedAsync(AlarmDto alarm, CancellationToken cancellationToken)
    {
        var route = await unitKeyResolver.ResolveAsync(alarm.UnitId, cancellationToken);
        await messageBus.PublishAsync(new AlarmCleared(
            alarm.Id,
            alarm.TenantId,
            alarm.UnitId,
            alarm.TagId,
            route.TenantKey,
            route.SiteKey,
            route.UnitKey,
            alarm.ClearedAtUtc ?? DateTimeOffset.UtcNow));
    }
}
