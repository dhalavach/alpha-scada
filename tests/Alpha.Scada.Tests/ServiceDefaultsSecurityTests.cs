/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/ServiceDefaultsSecurityTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Http;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ServiceDefaultsSecurityTests
{
// LEARN: marks this method as a parameterized xUnit test that receives InlineData rows.
    [Theory]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("")]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("Basic abc")]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("Bearer")]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("Bearer definitely-not-a-jwt")]
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public void Jwt_service_rejects_invalid_tokens(string token)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tokens = new JwtTokenService(ConfigurationWithSecret());

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Null(tokens.Validate(token));
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public void Jwt_service_validates_issued_user_claims()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tokens = new JwtTokenService(ConfigurationWithSecret());
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var user = new UserDto(Guid.NewGuid(), Guid.NewGuid(), "operator@example.test", "Operator", Roles.Operator);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var issued = tokens.Issue(user, TimeSpan.FromMinutes(5));

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var current = tokens.Validate(issued.AccessToken);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.NotNull(current);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(user.Id, current.UserId);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(user.TenantId, current.TenantId);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(user.Role, current.Role);
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public void Forward_authorization_replaces_existing_header_and_ignores_blank_values()
    {
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
        using var request = new HttpRequestMessage();
// LEARN: executes one C# statement; semicolons terminate most statements.
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer old");

// LEARN: executes one C# statement; semicolons terminate most statements.
        request.ForwardAuthorization("Bearer new");

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal("Bearer new", request.Headers.Authorization?.ToString());

// LEARN: executes one C# statement; semicolons terminate most statements.
        request.ForwardAuthorization("");

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.False(request.Headers.Contains("Authorization"));
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public void Forward_authorization_from_http_request_copies_current_bearer_header()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var context = new DefaultHttpContext();
// LEARN: executes one C# statement; semicolons terminate most statements.
        context.Request.Headers.Authorization = "Bearer copied";
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
        using var target = new HttpRequestMessage();

// LEARN: executes one C# statement; semicolons terminate most statements.
        target.ForwardAuthorizationFrom(context.Request);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal("Bearer copied", target.Headers.Authorization?.ToString());
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static IConfiguration ConfigurationWithSecret() =>
// LEARN: creates a new object or record instance.
        new ConfigurationBuilder()
// LEARN: creates a new object or record instance.
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32"
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            })
// LEARN: executes one C# statement; semicolons terminate most statements.
            .Build();
}
