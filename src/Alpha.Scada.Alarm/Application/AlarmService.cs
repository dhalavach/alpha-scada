using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Contracts;
using Npgsql;

namespace Alpha.Scada.Alarm.Application;

public sealed class AlarmService(
    AlarmRepository repository,
    UnitKeyResolver unitKeyResolver,
    NpgsqlDataSource dataSource)
{
    public async Task<AlarmEventBatch> EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken)
    {
        var route = await ResolveRouteAsync(request.UnitId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changes = await repository.EvaluateAsync(connection, transaction, request, cancellationToken);
        var events = ToAlarmEvents(changes, route);
        await transaction.CommitAsync(cancellationToken);
        return events;
    }

    public async Task<AlarmRaised?> RaiseCommunicationLostAsync(UnitDto unit, UnitRouteKeys? knownRoute, CancellationToken cancellationToken)
    {
        var route = knownRoute is null
            ? await ResolveRouteAsync(unit.Id, cancellationToken)
            : ToAlarmRoute(knownRoute);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var alarm = await repository.RaiseCommunicationLostAsync(connection, transaction, unit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return alarm is null ? null : ToRaised(alarm, route);
    }

    public Task<AlarmRaised?> RaiseCommunicationLostAsync(UnitDto unit, CancellationToken cancellationToken) =>
        RaiseCommunicationLostAsync(unit, null, cancellationToken);

    public Task<IReadOnlyCollection<AlarmDto>> GetActiveAsync(CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetActiveAsync(user, cancellationToken);

    public async Task<AlarmAcknowledged?> AcknowledgeAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken)
    {
        var existing = await repository.GetActiveAlarmAsync(alarmId, user, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var route = await ResolveRouteAsync(existing.UnitId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var alarm = await repository.AcknowledgeAsync(connection, transaction, alarmId, user, cancellationToken);
        if (alarm is null)
        {
            return null;
        }

        var acknowledged = new AlarmAcknowledged(
            alarm.Id,
            alarm.TenantId,
            alarm.UnitId,
            alarm.TagId,
            route.TenantKey,
            route.SiteKey,
            route.UnitKey,
            user.UserId,
            alarm.AcknowledgedAtUtc ?? DateTimeOffset.UtcNow);
        await transaction.CommitAsync(cancellationToken);
        return acknowledged;
    }

    public Task<int> CountForUnitPeriodAsync(Guid unitId, string period, CancellationToken cancellationToken) =>
        repository.CountForUnitPeriodAsync(unitId, period, cancellationToken);

    private async Task<AlarmRouteKeys> ResolveRouteAsync(Guid unitId, CancellationToken cancellationToken) =>
        ToAlarmRoute(await unitKeyResolver.ResolveAsync(unitId, cancellationToken));

    private static AlarmRouteKeys ToAlarmRoute(UnitRouteKeys route) =>
        new(route.TenantId, route.UnitId, route.TenantKey, route.SiteKey, route.UnitKey);

    private static AlarmEventBatch ToAlarmEvents(
        AlarmChanges changes,
        AlarmRouteKeys route) =>
        new(
            changes.Raised.Select(alarm => ToRaised(alarm, route)).ToArray(),
            changes.Cleared.Select(alarm => new AlarmCleared(
                alarm.Id,
                alarm.TenantId,
                alarm.UnitId,
                alarm.TagId,
                route.TenantKey,
                route.SiteKey,
                route.UnitKey,
                alarm.ClearedAtUtc ?? DateTimeOffset.UtcNow)).ToArray());

    private static AlarmRaised ToRaised(
        AlarmDto alarm,
        AlarmRouteKeys route) =>
        new(
            alarm.Id,
            alarm.TenantId,
            alarm.UnitId,
            alarm.TagId,
            route.TenantKey,
            route.SiteKey,
            route.UnitKey,
            alarm.Severity,
            alarm.Message,
            alarm.RaisedAtUtc);
}

public sealed record AlarmEventBatch(
    IReadOnlyCollection<AlarmRaised> Raised,
    IReadOnlyCollection<AlarmCleared> Cleared);
