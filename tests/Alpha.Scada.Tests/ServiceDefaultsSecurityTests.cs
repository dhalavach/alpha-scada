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
    public void Jwt_service_rejects_invalid_tokens(string token)
    {
        var tokens = new JwtTokenService(ConfigurationWithSecret());

        Assert.Null(tokens.Validate(token));
    }

    [Fact]
    public void Jwt_service_validates_issued_user_claims()
    {
        var tokens = new JwtTokenService(ConfigurationWithSecret());
        var user = new UserDto(Guid.NewGuid(), Guid.NewGuid(), "operator@example.test", "Operator", Roles.Operator);
        var issued = tokens.IssueUserToken(user, TimeSpan.FromMinutes(5));

        var current = tokens.Validate(issued.AccessToken);

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

    private static IConfiguration ConfigurationWithSecret() => TestJwt.Configuration();
}
