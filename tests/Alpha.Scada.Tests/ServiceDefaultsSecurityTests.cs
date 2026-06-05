/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/ServiceDefaultsSecurityTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

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
        var issued = tokens.Issue(user, TimeSpan.FromMinutes(5));

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

    private static IConfiguration ConfigurationWithSecret() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32"
            })
            .Build();
}
