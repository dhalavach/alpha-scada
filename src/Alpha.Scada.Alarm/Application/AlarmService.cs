using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using Npgsql;

namespace Alpha.Scada.Alarm.Application;

public sealed class AlarmService(
    AlarmRepository repository,
    UnitKeyResolver unitKeyResolver,
    NpgsqlDataSource dataSource,
    WolverineTransactionalOutbox outbox)
{
    public async Task EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken)
    {
        var route = await ResolveRouteAsync(request.UnitId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changes = await repository.EvaluateAsync(connection, transaction, request, cancellationToken);
        var outboxBatches = await StoreAlarmChangesAsync(connection, transaction, changes, route, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await PublishAndClearAsync(outboxBatches, cancellationToken);
    }

    public async Task RaiseCommunicationLostAsync(UnitDto unit, UnitRouteKeys? knownRoute, CancellationToken cancellationToken)
    {
        var route = knownRoute is null
            ? await ResolveRouteAsync(unit.Id, cancellationToken)
            : ToAlarmRoute(knownRoute);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var alarm = await repository.RaiseCommunicationLostAsync(connection, transaction, unit, cancellationToken);
        WolverineOutboxBatch? outboxBatch = null;
        if (alarm is not null)
        {
            outboxBatch = await StoreRaisedAsync(connection, transaction, alarm, route, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        if (outboxBatch is not null)
        {
            await outbox.PublishAndClearAsync(outboxBatch, cancellationToken);
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
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var alarm = await repository.AcknowledgeAsync(connection, transaction, alarmId, user, cancellationToken);
        if (alarm is null)
        {
            return false;
        }

        var outboxBatch = await outbox.StoreAsync(connection, transaction, new AlarmAcknowledged(
            alarm.Id,
            alarm.TenantId,
            alarm.UnitId,
            alarm.TagId,
            route.TenantKey,
            route.SiteKey,
            route.UnitKey,
            user.UserId,
            alarm.AcknowledgedAtUtc ?? DateTimeOffset.UtcNow), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.PublishAndClearAsync(outboxBatch, cancellationToken);
        return true;
    }

    public Task<int> CountForUnitPeriodAsync(Guid unitId, string period, CancellationToken cancellationToken) =>
        repository.CountForUnitPeriodAsync(unitId, period, cancellationToken);

    private async Task<AlarmRouteKeys> ResolveRouteAsync(Guid unitId, CancellationToken cancellationToken) =>
        ToAlarmRoute(await unitKeyResolver.ResolveAsync(unitId, cancellationToken));

    private static AlarmRouteKeys ToAlarmRoute(UnitRouteKeys route) =>
        new(route.TenantId, route.UnitId, route.TenantKey, route.SiteKey, route.UnitKey);

    private async Task<IReadOnlyCollection<WolverineOutboxBatch>> StoreAlarmChangesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AlarmChanges changes,
        AlarmRouteKeys route,
        CancellationToken cancellationToken)
    {
        var outboxBatches = new List<WolverineOutboxBatch>();
        foreach (var alarm in changes.Raised)
        {
            outboxBatches.Add(await StoreRaisedAsync(connection, transaction, alarm, route, cancellationToken));
        }

        foreach (var alarm in changes.Cleared)
        {
            outboxBatches.Add(await outbox.StoreAsync(connection, transaction, new AlarmCleared(
                alarm.Id,
                alarm.TenantId,
                alarm.UnitId,
                alarm.TagId,
                route.TenantKey,
                route.SiteKey,
                route.UnitKey,
                alarm.ClearedAtUtc ?? DateTimeOffset.UtcNow), cancellationToken));
        }

        return outboxBatches;
    }

    private Task<WolverineOutboxBatch> StoreRaisedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AlarmDto alarm,
        AlarmRouteKeys route,
        CancellationToken cancellationToken) =>
        outbox.StoreAsync(connection, transaction, new AlarmRaised(
            alarm.Id,
            alarm.TenantId,
            alarm.UnitId,
            alarm.TagId,
            route.TenantKey,
            route.SiteKey,
            route.UnitKey,
            alarm.Severity,
            alarm.Message,
            alarm.RaisedAtUtc), cancellationToken);

    private async Task PublishAndClearAsync(
        IReadOnlyCollection<WolverineOutboxBatch> outboxBatches,
        CancellationToken cancellationToken)
    {
        foreach (var outboxBatch in outboxBatches)
        {
            await outbox.PublishAndClearAsync(outboxBatch, cancellationToken);
        }
    }
}
