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

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmService(
    AlarmRepository repository,
    UnitKeyResolver unitKeyResolver,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
    NpgsqlDataSource dataSource,
    IAlarmOutboxSignal outboxSignal)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmEventBatch> EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken)
    {
        var route = await ResolveRouteAsync(request.UnitId, cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changes = await repository.EvaluateAsync(connection, transaction, request, cancellationToken);
        var events = ToAlarmEvents(changes, route);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await repository.EnqueueOutboxAsync(connection, transaction, events.All, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: branches only when the boolean condition is true.
        if (events.HasEvents)
        {
            outboxSignal.Kick();
        }

        return events;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmRaised?> RaiseCommunicationLostAsync(UnitDto unit, UnitRouteKeys? knownRoute, CancellationToken cancellationToken)
    {
        var route = knownRoute is null
            ? await ResolveRouteAsync(unit.Id, cancellationToken)
            : ToAlarmRoute(knownRoute);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var alarm = await repository.RaiseCommunicationLostAsync(connection, transaction, unit, cancellationToken);
        var raised = alarm is null ? null : ToRaised(alarm, route);
// LEARN: branches only when the boolean condition is true.
        if (raised is not null)
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await repository.EnqueueOutboxAsync(connection, transaction, [raised], cancellationToken);
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: branches only when the boolean condition is true.
        if (raised is not null)
        {
            outboxSignal.Kick();
        }

        return raised;
    }

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<AlarmRaised?> RaiseCommunicationLostAsync(UnitDto unit, CancellationToken cancellationToken) =>
        RaiseCommunicationLostAsync(unit, null, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<IReadOnlyCollection<AlarmDto>> GetActiveAsync(CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetActiveAsync(user, cancellationToken);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmAcknowledged?> AcknowledgeAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken)
    {
        var existing = await repository.GetActiveAlarmAsync(alarmId, user, cancellationToken);
// LEARN: branches only when the boolean condition is true.
        if (existing is null)
        {
            return null;
        }

        var route = await ResolveRouteAsync(existing.UnitId, cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var alarm = await repository.AcknowledgeAsync(connection, transaction, alarmId, user, cancellationToken);
// LEARN: branches only when the boolean condition is true.
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
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await repository.EnqueueOutboxAsync(connection, transaction, [acknowledged], cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
        outboxSignal.Kick();
        return acknowledged;
    }

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<int> CountForUnitPeriodAsync(Guid unitId, string period, CancellationToken cancellationToken) =>
        repository.CountForUnitPeriodAsync(unitId, period, cancellationToken);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task<AlarmRouteKeys> ResolveRouteAsync(Guid unitId, CancellationToken cancellationToken) =>
        ToAlarmRoute(await unitKeyResolver.ResolveAsync(unitId, cancellationToken));

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
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

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record AlarmEventBatch(
    IReadOnlyCollection<AlarmRaised> Raised,
    IReadOnlyCollection<AlarmCleared> Cleared)
{
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public bool HasEvents => Raised.Count > 0 || Cleared.Count > 0;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public IEnumerable<object> All => Raised.Cast<object>().Concat(Cleared);
}
