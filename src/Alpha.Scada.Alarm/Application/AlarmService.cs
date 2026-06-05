/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Application/AlarmService.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Alarm.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmService(
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlarmRepository repository,
// LEARN: continues an argument/object/collection initializer onto the next line.
    UnitKeyResolver unitKeyResolver,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
    NpgsqlDataSource dataSource,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    IAlarmOutboxSignal outboxSignal)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmEventBatch> EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var route = await ResolveRouteAsync(request.UnitId, cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var changes = await repository.EvaluateAsync(connection, transaction, request, cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var events = ToAlarmEvents(changes, route);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await repository.EnqueueOutboxAsync(connection, transaction, events.All, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: branches only when the boolean condition is true.
        if (events.HasEvents)
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            outboxSignal.Kick();
        }

// LEARN: returns a value or exits the current method.
        return events;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmRaised?> RaiseCommunicationLostAsync(UnitDto unit, UnitRouteKeys? knownRoute, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var route = knownRoute is null
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            ? await ResolveRouteAsync(unit.Id, cancellationToken)
// LEARN: executes one C# statement; semicolons terminate most statements.
            : ToAlarmRoute(knownRoute);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var alarm = await repository.RaiseCommunicationLostAsync(connection, transaction, unit, cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
            outboxSignal.Kick();
        }

// LEARN: returns a value or exits the current method.
        return raised;
    }

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<AlarmRaised?> RaiseCommunicationLostAsync(UnitDto unit, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        RaiseCommunicationLostAsync(unit, null, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<IReadOnlyCollection<AlarmDto>> GetActiveAsync(CurrentUserDto user, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        repository.GetActiveAsync(user, cancellationToken);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmAcknowledged?> AcknowledgeAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var existing = await repository.GetActiveAlarmAsync(alarmId, user, cancellationToken);
// LEARN: branches only when the boolean condition is true.
        if (existing is null)
        {
// LEARN: returns a value or exits the current method.
            return null;
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var route = await ResolveRouteAsync(existing.UnitId, cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var alarm = await repository.AcknowledgeAsync(connection, transaction, alarmId, user, cancellationToken);
// LEARN: branches only when the boolean condition is true.
        if (alarm is null)
        {
// LEARN: returns a value or exits the current method.
            return null;
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var acknowledged = new AlarmAcknowledged(
// LEARN: continues an argument/object/collection initializer onto the next line.
            alarm.Id,
// LEARN: continues an argument/object/collection initializer onto the next line.
            alarm.TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            alarm.UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            alarm.TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            route.TenantKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            route.SiteKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            route.UnitKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            user.UserId,
// LEARN: executes one C# statement; semicolons terminate most statements.
            alarm.AcknowledgedAtUtc ?? DateTimeOffset.UtcNow);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await repository.EnqueueOutboxAsync(connection, transaction, [acknowledged], cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: executes one C# statement; semicolons terminate most statements.
        outboxSignal.Kick();
// LEARN: returns a value or exits the current method.
        return acknowledged;
    }

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<int> CountForUnitPeriodAsync(Guid unitId, string period, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        repository.CountForUnitPeriodAsync(unitId, period, cancellationToken);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task<AlarmRouteKeys> ResolveRouteAsync(Guid unitId, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        ToAlarmRoute(await unitKeyResolver.ResolveAsync(unitId, cancellationToken));

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static AlarmRouteKeys ToAlarmRoute(UnitRouteKeys route) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        new(route.TenantId, route.UnitId, route.TenantKey, route.SiteKey, route.UnitKey);

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static AlarmEventBatch ToAlarmEvents(
// LEARN: continues an argument/object/collection initializer onto the next line.
        AlarmChanges changes,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        AlarmRouteKeys route) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        new(
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            changes.Raised.Select(alarm => ToRaised(alarm, route)).ToArray(),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            changes.Cleared.Select(alarm => new AlarmCleared(
// LEARN: continues an argument/object/collection initializer onto the next line.
                alarm.Id,
// LEARN: continues an argument/object/collection initializer onto the next line.
                alarm.TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                alarm.UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                alarm.TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                route.TenantKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
                route.SiteKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
                route.UnitKey,
// LEARN: executes one C# statement; semicolons terminate most statements.
                alarm.ClearedAtUtc ?? DateTimeOffset.UtcNow)).ToArray());

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static AlarmRaised ToRaised(
// LEARN: continues an argument/object/collection initializer onto the next line.
        AlarmDto alarm,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        AlarmRouteKeys route) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        new(
// LEARN: continues an argument/object/collection initializer onto the next line.
            alarm.Id,
// LEARN: continues an argument/object/collection initializer onto the next line.
            alarm.TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            alarm.UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            alarm.TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            route.TenantKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            route.SiteKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            route.UnitKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            alarm.Severity,
// LEARN: continues an argument/object/collection initializer onto the next line.
            alarm.Message,
// LEARN: executes one C# statement; semicolons terminate most statements.
            alarm.RaisedAtUtc);
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record AlarmEventBatch(
// LEARN: continues an argument/object/collection initializer onto the next line.
    IReadOnlyCollection<AlarmRaised> Raised,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    IReadOnlyCollection<AlarmCleared> Cleared)
{
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public bool HasEvents => Raised.Count > 0 || Cleared.Count > 0;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public IEnumerable<object> All => Raised.Cast<object>().Concat(Cleared);
}
