using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Alpha.Scada.Contracts;
using Alpha.Scada.Gateway;
using Alpha.Scada.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Alpha.Scada.Tests;

public sealed class GatewayEndpointTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task History_rejects_windows_outside_public_bounds(int minutes)
    {
        var fixture = await BuildAppAsync(Roles.Operator);
        await using var app = fixture.App;
        using var response = await app.GetTestClient().SendAsync(
            Authorized(HttpMethod.Get, $"/api/tags/{Guid.NewGuid()}/history?minutes={minutes}", fixture.Token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Report_run_rejects_invalid_period_before_calling_asset()
    {
        var assetCalls = 0;
        var fixture = await BuildAppAsync(Roles.Operator, (clientName, _, _) =>
        {
            if (clientName == AlphaServiceClients.Asset)
            {
                Interlocked.Increment(ref assetCalls);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        await using var app = fixture.App;
        using var request = Authorized(HttpMethod.Post, "/api/reports/monthly/run", fixture.Token);
        request.Content = JsonContent.Create(new ReportRunRequest(UnitId, "2026-13"));

        using var response = await app.GetTestClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, assetCalls);
    }

    [Fact]
    public async Task Viewer_cannot_run_reports_but_operator_reaches_asset_lookup()
    {
        var assetCalls = 0;
        Task<HttpResponseMessage> Responder(string clientName, HttpRequestMessage _, CancellationToken __)
        {
            if (clientName == AlphaServiceClients.Asset)
            {
                Interlocked.Increment(ref assetCalls);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var viewer = await BuildAppAsync(Roles.Viewer, Responder);
        await using (viewer.App)
        {
            using var request = Authorized(HttpMethod.Post, "/api/reports/monthly/run", viewer.Token);
            request.Content = JsonContent.Create(new ReportRunRequest(UnitId, "2026-06"));
            using var response = await viewer.App.GetTestClient().SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, assetCalls);
        }

        var operatorFixture = await BuildAppAsync(Roles.Operator, Responder);
        await using (operatorFixture.App)
        {
            using var request = Authorized(HttpMethod.Post, "/api/reports/monthly/run", operatorFixture.Token);
            request.Content = JsonContent.Create(new ReportRunRequest(UnitId, "2026-06"));
            using var response = await operatorFixture.App.GetTestClient().SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(1, assetCalls);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway)]
    public async Task Login_distinguishes_invalid_credentials_from_identity_failure(
        HttpStatusCode identityStatus,
        HttpStatusCode expectedGatewayStatus)
    {
        var fixture = await BuildAppAsync(Roles.Operator, (clientName, _, _) =>
            Task.FromResult(new HttpResponseMessage(
                clientName == AlphaServiceClients.Identity ? identityStatus : HttpStatusCode.NotFound)));
        await using var app = fixture.App;

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("user@example.test", "password"));

        Assert.Equal(expectedGatewayStatus, response.StatusCode);
    }

    [Fact]
    public async Task Current_tag_fanout_runs_concurrently_and_returns_null_for_missing_values()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalogStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var telemetryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tagId = Guid.NewGuid();

        async Task<HttpResponseMessage> Responder(
            string clientName,
            HttpRequestMessage _,
            CancellationToken cancellationToken)
        {
            if (clientName == AlphaServiceClients.TagCatalog)
            {
                catalogStarted.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return JsonResponse<TagDto[]>([
                    new TagDto(tagId, TenantId, UnitId, "engine.output", "Output", "Engine", "kW", null, null)
                ]);
            }

            if (clientName == AlphaServiceClients.Telemetry)
            {
                telemetryStarted.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return JsonResponse(Array.Empty<TagValueDto>());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var fixture = await BuildAppAsync(Roles.Operator, Responder);
        await using var app = fixture.App;
        var responseTask = app.GetTestClient().SendAsync(
            Authorized(HttpMethod.Get, $"/api/units/{UnitId}/tags/current", fixture.Token));

        await Task.WhenAll(
            catalogStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            telemetryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        release.TrySetResult();

        using var response = await responseTask;
        var tags = await response.Content.ReadFromJsonAsync<TagCurrentDto[]>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(tags);
        Assert.Null(Assert.Single(tags).Value);
        Assert.Null(tags[0].TimestampUtc);
    }

    private static async Task<(WebApplication App, string Token)> BuildAppAsync(
        string role,
        Func<string, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder = null)
    {
        var configuration = Configuration();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddProblemDetails();
        builder.Services.AddAlphaJwtAuthentication(builder.Configuration);
        builder.Services.AddSingleton<IHttpClientFactory>(
            new StubHttpClientFactory(responder ?? ((_, _, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))));
        builder.Services.AddSingleton(DispatchProxy.Create<IMessageBus, ThrowingMessageBusProxy>());

        var app = builder.Build();
        app.UseAlphaExceptionHandling();
        app.UseAlphaAuthorization();
        app.MapGatewayEndpoints();
        await app.StartAsync();

        var token = new JwtTokenService(configuration).Issue(
            new UserDto(Guid.NewGuid(), TenantId, "user@example.test", "Test User", role),
            TimeSpan.FromMinutes(5)).AccessToken;
        return (app, token);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpResponseMessage JsonResponse<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32"
            })
            .Build();

    private sealed class StubHttpClientFactory(
        Func<string, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new DelegateHandler((request, cancellationToken) => responder(name, request, cancellationToken)), disposeHandler: true)
            {
                BaseAddress = new Uri("http://downstream.test")
            };
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }

    private class ThrowingMessageBusProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected message bus call to {targetMethod?.Name}.");
    }
}
