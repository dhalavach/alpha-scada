using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Alpha.Scada.Tests;

public sealed class ServiceDefaultsSecurityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer")]
    [InlineData("Bearer definitely-not-a-jwt")]
    public void User_context_rejects_missing_or_invalid_bearer_tokens(string authorization)
    {
        var tokens = new JwtTokenService(ConfigurationWithSecret());
        var headers = new HeaderDictionary
        {
            ["Authorization"] = authorization
        };

        Assert.Null(HttpUserContext.FromBearerToken(headers, tokens));
    }

    [Fact]
    public void User_context_accepts_bearer_token_case_insensitively()
    {
        var tokens = new JwtTokenService(ConfigurationWithSecret());
        var user = new UserDto(Guid.NewGuid(), Guid.NewGuid(), "operator@example.test", "Operator", Roles.Operator);
        var issued = tokens.Issue(user, TimeSpan.FromMinutes(5));
        var headers = new HeaderDictionary
        {
            ["Authorization"] = $"bearer   {issued.AccessToken}  "
        };

        var current = HttpUserContext.FromBearerToken(headers, tokens);

        Assert.NotNull(current);
        Assert.Equal(user.Id, current.UserId);
        Assert.Equal(user.TenantId, current.TenantId);
        Assert.Equal(user.Role, current.Role);
    }

    [Fact]
    public void Forward_authorization_replaces_existing_header_and_ignores_blank_values()
    {
        using var request = new HttpRequestMessage();
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer old");

        request.ForwardAuthorization("Bearer new");

        Assert.Equal("Bearer new", request.Headers.Authorization?.ToString());

        request.ForwardAuthorization("");

        Assert.False(request.Headers.Contains("Authorization"));
    }

    [Fact]
    public void Forward_authorization_from_http_request_copies_current_bearer_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer copied";
        using var target = new HttpRequestMessage();

        target.ForwardAuthorizationFrom(context.Request);

        Assert.Equal("Bearer copied", target.Headers.Authorization?.ToString());
    }

    private static IConfiguration ConfigurationWithSecret() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32"
            })
            .Build();
}
