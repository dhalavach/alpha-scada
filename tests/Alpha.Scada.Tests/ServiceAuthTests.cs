using System.Net;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Alpha.Scada.Tests;

public sealed class ServiceAuthTests
{
    [Fact]
    public async Task Service_only_endpoint_requires_service_role()
    {
        var configuration = ConfigurationWithSecret();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddAlphaJwtAuthentication(builder.Configuration);

        var app = builder.Build();
        app.UseAlphaAuthorization();
        app.MapGet("/service-only", () => Results.Ok("ok"))
            .RequireAuthorization(AlphaAuthentication.ServiceOnlyPolicy);
        await app.StartAsync();
        await using (app)
        {
            using var client = app.GetTestClient();
            var tokens = new JwtTokenService(configuration);
            var userToken = tokens.Issue(new UserDto(Guid.NewGuid(), Guid.NewGuid(), "viewer@example.test", "Viewer", Roles.Viewer), TimeSpan.FromMinutes(5)).AccessToken;
            var serviceToken = new ServiceTokenProvider(tokens).GetToken();

            var anonymous = await client.GetAsync("/service-only");
            var user = await client.SendAsync(TokenRequest(userToken));
            var service = await client.SendAsync(TokenRequest(serviceToken));

            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, user.StatusCode);
            Assert.Equal(HttpStatusCode.OK, service.StatusCode);
        }
    }

    [Fact]
    public async Task Service_authorization_handler_does_not_overwrite_existing_authorization()
    {
        var inner = new CaptureHandler();
        var handler = new ServiceAuthorizationHandler(new ServiceTokenProvider(new JwtTokenService(ConfigurationWithSecret())))
        {
            InnerHandler = inner
        };
        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://service.test/internal");
        request.ForwardAuthorization("Bearer user-token");

        await client.SendAsync(request);

        Assert.Equal("Bearer user-token", inner.Authorization);
    }

    [Fact]
    public async Task Service_authorization_handler_attaches_service_token_when_authorization_is_missing()
    {
        var configuration = ConfigurationWithSecret();
        var inner = new CaptureHandler();
        var handler = new ServiceAuthorizationHandler(new ServiceTokenProvider(new JwtTokenService(configuration)))
        {
            InnerHandler = inner
        };
        using var client = new HttpClient(handler);

        await client.GetAsync("http://service.test/internal");

        Assert.StartsWith("Bearer ", inner.Authorization, StringComparison.Ordinal);
        Assert.Equal(Roles.Service, new JwtTokenService(configuration).Validate(inner.Authorization?["Bearer ".Length..] ?? "")?.Role);
    }

    [Fact]
    public void Service_token_provider_reuses_cached_token_within_lifetime()
    {
        var provider = new ServiceTokenProvider(new JwtTokenService(ConfigurationWithSecret()));

        var first = provider.GetToken();
        var second = provider.GetToken();

        Assert.Equal(first, second);
    }

    private static HttpRequestMessage TokenRequest(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/service-only");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static IConfiguration ConfigurationWithSecret() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32"
            })
            .Build();

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
