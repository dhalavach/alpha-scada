/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/RealtimeTenantIsolationTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. SignalR provides server-to-browser realtime delivery; Gateway owns that public realtime boundary. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Security.Claims;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Gateway.Application;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Gateway.Realtime;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Reporting.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Http.Features;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.SignalR;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class RealtimeTenantIsolationTests
{
// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Telemetry_hub_auto_joins_authenticated_tenant_group()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tenantId = Guid.NewGuid();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var groups = new RecordingGroupManager();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var hub = new TelemetryHub
        {
// LEARN: creates a new object or record instance.
            Context = new TestHubCallerContext("connection-1", tenantId),
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
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
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tenantId = Guid.NewGuid();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var unitId = Guid.NewGuid();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var hub = new RecordingHubContext<TelemetryHub>();

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await new TelemetryBroadcastHandler(hub).Handle(new TelemetryBatchStored(
// LEARN: continues an argument/object/collection initializer onto the next line.
            tenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            unitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            "tenant-a",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "site-a",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "unit-a",
// LEARN: continues an argument/object/collection initializer onto the next line.
            DateTimeOffset.UtcNow,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
            []), CancellationToken.None);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await new AlarmBroadcastHandler(hub).Handle(new AlarmRaised(
// LEARN: continues an argument/object/collection initializer onto the next line.
            Guid.NewGuid(),
// LEARN: continues an argument/object/collection initializer onto the next line.
            tenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            unitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            null,
// LEARN: continues an argument/object/collection initializer onto the next line.
            "tenant-a",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "site-a",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "unit-a",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "warning",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "Test alarm",
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
            DateTimeOffset.UtcNow), CancellationToken.None);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await new UnitStatusBroadcastHandler(hub).Handle(new UnitStatusChanged(
// LEARN: continues an argument/object/collection initializer onto the next line.
            tenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            Guid.NewGuid(),
// LEARN: continues an argument/object/collection initializer onto the next line.
            unitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            "tenant-a",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "site-a",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "unit-a",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "Unit A",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "online",
// LEARN: continues an argument/object/collection initializer onto the next line.
            DateTimeOffset.UtcNow,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
            DateTimeOffset.UtcNow), CancellationToken.None);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await new ReportCompletedHandler(hub).Handle(new ReportCompleted(
// LEARN: continues an argument/object/collection initializer onto the next line.
            Guid.NewGuid(),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Guid.NewGuid(),
// LEARN: continues an argument/object/collection initializer onto the next line.
            tenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            unitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            "2026-06",
// LEARN: continues an argument/object/collection initializer onto the next line.
            DateTimeOffset.UtcNow,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
            null), CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Empty(hub.Clients.AllMessages);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(4, hub.Clients.GroupMessages.Count);
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        Assert.All(hub.Clients.GroupMessages, message => Assert.Equal(TelemetryHub.TenantGroup(tenantId), message.GroupName));
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "telemetryUpdated");
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "alarmsChanged");
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "unitStatusChanged");
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "reportCompleted");
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class RecordingHubContext<THub> : IHubContext<THub>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        where THub : Hub
    {
// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public RecordingHubClients Clients { get; } = new();

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        IHubClients IHubContext<THub>.Clients => Clients;

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public IGroupManager Groups { get; } = new RecordingGroupManager();
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class RecordingHubClients : IHubClients
    {
// LEARN: declares a member with explicit visibility so the type boundary is clear.
        private readonly RecordingClientProxy _all;

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
        public RecordingHubClients()
        {
// LEARN: creates a new object or record instance.
            _all = new RecordingClientProxy(null, AllMessages);
        }

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public List<HubMessage> AllMessages { get; } = [];

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
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
// LEARN: creates a new object or record instance.
            messages.Add(new HubMessage(groupName, method));
// LEARN: returns a value or exits the current method.
            return Task.CompletedTask;
        }
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class RecordingGroupManager : IGroupManager
    {
// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public List<(string ConnectionId, string GroupName)> Added { get; } = [];

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            Added.Add((connectionId, groupName));
// LEARN: returns a value or exits the current method.
            return Task.CompletedTask;
        }

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
            Task.CompletedTask;
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class TestHubCallerContext(string connectionId, Guid tenantId) : HubCallerContext
    {
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
        private readonly CancellationTokenSource _connectionAborted = new();

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public override string ConnectionId { get; } = connectionId;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public override string? UserIdentifier => null;

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public override ClaimsPrincipal User { get; } = new(new ClaimsIdentity([
// LEARN: creates a new object or record instance.
            new Claim("tenant_id", tenantId.ToString("D"))
// LEARN: executes one C# statement; semicolons terminate most statements.
        ], "Test"));

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public override IFeatureCollection Features { get; } = new FeatureCollection();

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public override CancellationToken ConnectionAborted => _connectionAborted.Token;

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
        public override void Abort() => _connectionAborted.Cancel();
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private sealed record HubMessage(string? GroupName, string Method);
}
