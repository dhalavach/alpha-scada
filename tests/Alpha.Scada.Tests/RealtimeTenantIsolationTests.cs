/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/RealtimeTenantIsolationTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. SignalR provides server-to-browser realtime delivery; Gateway owns that public realtime boundary. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Security.Claims;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Gateway.Application;
using Alpha.Scada.Gateway.Realtime;
using Alpha.Scada.Reporting.Contracts;
using Alpha.Scada.Telemetry.Contracts;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class RealtimeTenantIsolationTests
{
// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Telemetry_hub_auto_joins_authenticated_tenant_group()
    {
        var tenantId = Guid.NewGuid();
        var groups = new RecordingGroupManager();
        var hub = new TelemetryHub
        {
            Context = new TestHubCallerContext("connection-1", tenantId),
            Groups = groups
        };

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await hub.OnConnectedAsync();

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Contains((hub.Context.ConnectionId, TelemetryHub.TenantGroup(tenantId)), groups.Added);
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Broadcast_handlers_target_tenant_group_instead_of_all_clients()
    {
        var tenantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var hub = new RecordingHubContext<TelemetryHub>();

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await new TelemetryBroadcastHandler(hub).Handle(new TelemetryBatchStored(
            tenantId,
            unitId,
            "tenant-a",
            "site-a",
            "unit-a",
            DateTimeOffset.UtcNow,
            []), CancellationToken.None);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await new AlarmBroadcastHandler(hub).Handle(new AlarmRaised(
            Guid.NewGuid(),
            tenantId,
            unitId,
            null,
            "tenant-a",
            "site-a",
            "unit-a",
            "warning",
            "Test alarm",
            DateTimeOffset.UtcNow), CancellationToken.None);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await new UnitStatusBroadcastHandler(hub).Handle(new UnitStatusChanged(
            tenantId,
            Guid.NewGuid(),
            unitId,
            "tenant-a",
            "site-a",
            "unit-a",
            "Unit A",
            "online",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow), CancellationToken.None);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await new ReportCompletedHandler(hub).Handle(new ReportCompleted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            tenantId,
            unitId,
            "2026-06",
            DateTimeOffset.UtcNow,
            null), CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Empty(hub.Clients.AllMessages);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(4, hub.Clients.GroupMessages.Count);
        Assert.All(hub.Clients.GroupMessages, message => Assert.Equal(TelemetryHub.TenantGroup(tenantId), message.GroupName));
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "telemetryUpdated");
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "alarmsChanged");
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "unitStatusChanged");
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "reportCompleted");
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class RecordingHubContext<THub> : IHubContext<THub>
        where THub : Hub
    {
        public RecordingHubClients Clients { get; } = new();

        IHubClients IHubContext<THub>.Clients => Clients;

        public IGroupManager Groups { get; } = new RecordingGroupManager();
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class RecordingHubClients : IHubClients
    {
        private readonly RecordingClientProxy _all;

        public RecordingHubClients()
        {
            _all = new RecordingClientProxy(null, AllMessages);
        }

        public List<HubMessage> AllMessages { get; } = [];

        public List<HubMessage> GroupMessages { get; } = [];

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public IClientProxy All => _all;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _all;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public IClientProxy Client(string connectionId) => _all;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _all;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public IClientProxy Group(string groupName) => new RecordingClientProxy(groupName, GroupMessages);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new RecordingClientProxy(string.Join(",", groupNames), GroupMessages);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public IClientProxy User(string userId) => _all;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public IClientProxy Users(IReadOnlyList<string> userIds) => _all;
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class RecordingClientProxy(string? groupName, List<HubMessage> messages) : IClientProxy
    {
// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            messages.Add(new HubMessage(groupName, method));
            return Task.CompletedTask;
        }
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Added { get; } = [];

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class TestHubCallerContext(string connectionId, Guid tenantId) : HubCallerContext
    {
        private readonly CancellationTokenSource _connectionAborted = new();

        public override string ConnectionId { get; } = connectionId;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public override string? UserIdentifier => null;

        public override ClaimsPrincipal User { get; } = new(new ClaimsIdentity([
            new Claim("tenant_id", tenantId.ToString("D"))
        ], "Test"));

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public override CancellationToken ConnectionAborted => _connectionAborted.Token;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public override void Abort() => _connectionAborted.Cancel();
    }

    private sealed record HubMessage(string? GroupName, string Method);
}
