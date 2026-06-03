using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Contracts;
using Wolverine;

namespace Alpha.Scada.Alarm.Application;

public sealed class AlarmService(AlarmRepository repository, UnitKeyResolver unitKeyResolver, IMessageBus bus)
{
    public async Task EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken)
    {
        var route = await ResolveRouteAsync(request.UnitId, cancellationToken);
        var changes = await repository.EvaluateAsync(request, cancellationToken);
        await PublishAlarmChangesAsync(changes, route, cancellationToken);
    }

    public async Task RaiseCommunicationLostAsync(UnitDto unit, UnitRouteKeys? knownRoute, CancellationToken cancellationToken)
    {
        var route = knownRoute is null
            ? await ResolveRouteAsync(unit.Id, cancellationToken)
            : ToAlarmRoute(knownRoute);
        var alarm = await repository.RaiseCommunicationLostAsync(unit, cancellationToken);
        if (alarm is not null)
        {
            await PublishRaisedAsync(alarm, route, cancellationToken);
        }
    }

    public Task RaiseCommunicationLostAsync(UnitDto unit, CancellationToken cancellationToken) =>
        RaiseCommunicationLostAsync(unit, null, cancellationToken);

    public Task<IReadOnlyCollection<AlarmDto>> GetActiveAsync(CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetActiveAsync(user, cancellationToken);

    public async Task<bool> AcknowledgeAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken)
    {
        var existing = await repository.GetActiveAlarmAsync(alarmId, user, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        var route = await ResolveRouteAsync(existing.UnitId, cancellationToken);
        var alarm = await repository.AcknowledgeAsync(alarmId, user, cancellationToken);
        if (alarm is null)
        {
            return false;
        }

        await bus.PublishAsync(new AlarmAcknowledged(
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

    private async Task<AlarmRouteKeys> ResolveRouteAsync(Guid unitId, CancellationToken cancellationToken) =>
        ToAlarmRoute(await unitKeyResolver.ResolveAsync(unitId, cancellationToken));

    private static AlarmRouteKeys ToAlarmRoute(UnitRouteKeys route) =>
        new(route.TenantId, route.UnitId, route.TenantKey, route.SiteKey, route.UnitKey);

    private async Task PublishAlarmChangesAsync(AlarmChanges changes, AlarmRouteKeys route, CancellationToken cancellationToken)
    {
        foreach (var alarm in changes.Raised)
        {
            await PublishRaisedAsync(alarm, route, cancellationToken);
        }

        foreach (var alarm in changes.Cleared)
        {
            await bus.PublishAsync(new AlarmCleared(
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

    private Task PublishRaisedAsync(AlarmDto alarm, AlarmRouteKeys route, CancellationToken cancellationToken) =>
        bus.PublishAsync(new AlarmRaised(
            alarm.Id,
            alarm.TenantId,
            alarm.UnitId,
            alarm.TagId,
            route.TenantKey,
            route.SiteKey,
            route.UnitKey,
            alarm.Severity,
            alarm.Message,
            alarm.RaisedAtUtc)).AsTask();
}
