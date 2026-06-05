/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Application/AlarmService.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Contracts;
using Npgsql;

namespace Alpha.Scada.Alarm.Application;

public sealed class AlarmService(
    AlarmRepository repository,
    UnitKeyResolver unitKeyResolver,
    NpgsqlDataSource dataSource,
    IAlarmOutboxSignal outboxSignal)
{
    public async Task<AlarmEventBatch> EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken)
    {
        var route = await ResolveRouteAsync(request.UnitId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changes = await repository.EvaluateAsync(connection, transaction, request, cancellationToken);
        var events = ToAlarmEvents(changes, route);
        await repository.EnqueueOutboxAsync(connection, transaction, events.All, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (events.HasEvents)
        {
            outboxSignal.Kick();
        }

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
        var raised = alarm is null ? null : ToRaised(alarm, route);
        if (raised is not null)
        {
            await repository.EnqueueOutboxAsync(connection, transaction, [raised], cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        if (raised is not null)
        {
            outboxSignal.Kick();
        }

        return raised;
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
        await repository.EnqueueOutboxAsync(connection, transaction, [acknowledged], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        outboxSignal.Kick();
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
    IReadOnlyCollection<AlarmCleared> Cleared)
{
    public bool HasEvents => Raised.Count > 0 || Cleared.Count > 0;

    public IEnumerable<object> All => Raised.Cast<object>().Concat(Cleared);
}
