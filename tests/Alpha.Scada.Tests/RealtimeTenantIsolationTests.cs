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

public sealed class RealtimeTenantIsolationTests
{
    [Fact]
    public async Task Telemetry_hub_auto_joins_authenticated_tenant_group()
    {
        var tenantId = Guid.NewGuid();
        var groups = new RecordingGroupManager();
        var hub = new TelemetryHub
        {
            Context = new TestHubCallerContext("connection-1", tenantId),
            Groups = groups
        };

        await hub.OnConnectedAsync();

        Assert.Contains((hub.Context.ConnectionId, TelemetryHub.TenantGroup(tenantId)), groups.Added);
    }

    [Fact]
    public async Task Broadcast_handlers_target_tenant_group_instead_of_all_clients()
    {
        var tenantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var hub = new RecordingHubContext<TelemetryHub>();

        await new TelemetryBroadcastHandler(hub).Handle(new TelemetryBatchStored(
            tenantId,
            unitId,
            "tenant-a",
            "site-a",
            "unit-a",
            DateTimeOffset.UtcNow,
            []), CancellationToken.None);

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

        await new ReportCompletedHandler(hub).Handle(new ReportCompleted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            tenantId,
            unitId,
            "2026-06",
            DateTimeOffset.UtcNow,
            null), CancellationToken.None);

        Assert.Empty(hub.Clients.AllMessages);
        Assert.Equal(4, hub.Clients.GroupMessages.Count);
        Assert.All(hub.Clients.GroupMessages, message => Assert.Equal(TelemetryHub.TenantGroup(tenantId), message.GroupName));
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "telemetryUpdated");
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "alarmsChanged");
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "unitStatusChanged");
        Assert.Contains(hub.Clients.GroupMessages, message => message.Method == "reportCompleted");
    }

    private sealed class RecordingHubContext<THub> : IHubContext<THub>
        where THub : Hub
    {
        public RecordingHubClients Clients { get; } = new();

        IHubClients IHubContext<THub>.Clients => Clients;

        public IGroupManager Groups { get; } = new RecordingGroupManager();
    }

    private sealed class RecordingHubClients : IHubClients
    {
        private readonly RecordingClientProxy _all;

        public RecordingHubClients()
        {
            _all = new RecordingClientProxy(null, AllMessages);
        }

        public List<HubMessage> AllMessages { get; } = [];

        public List<HubMessage> GroupMessages { get; } = [];

        public IClientProxy All => _all;

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _all;

        public IClientProxy Client(string connectionId) => _all;

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _all;

        public IClientProxy Group(string groupName) => new RecordingClientProxy(groupName, GroupMessages);

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new RecordingClientProxy(string.Join(",", groupNames), GroupMessages);

        public IClientProxy User(string userId) => _all;

        public IClientProxy Users(IReadOnlyList<string> userIds) => _all;
    }

    private sealed class RecordingClientProxy(string? groupName, List<HubMessage> messages) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            messages.Add(new HubMessage(groupName, method));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Added { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestHubCallerContext(string connectionId, Guid tenantId) : HubCallerContext
    {
        private readonly CancellationTokenSource _connectionAborted = new();

        public override string ConnectionId { get; } = connectionId;

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal User { get; } = new(new ClaimsIdentity([
            new Claim("tenant_id", tenantId.ToString("D"))
        ], "Test"));

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override CancellationToken ConnectionAborted => _connectionAborted.Token;

        public override void Abort() => _connectionAborted.Cancel();
    }

    private sealed record HubMessage(string? GroupName, string Method);
}
