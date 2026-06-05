/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/ContractTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Identity.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Http;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text.Json;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ContractTests
{
// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public void Password_hash_roundtrips_without_storing_plaintext()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var hash = PasswordHasher.Hash("ChangeMe!123");

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.NotEqual("ChangeMe!123", hash);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.True(PasswordHasher.Verify("ChangeMe!123", hash));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.False(PasswordHasher.Verify("wrong", hash));
    }

// LEARN: marks this method as a parameterized xUnit test that receives InlineData rows.
    [Theory]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData(Roles.Admin, true)]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData(Roles.Operator, true)]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData(Roles.Viewer, false)]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData(Roles.SupportEngineer, true)]
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public void Alarm_acknowledgement_permissions_follow_fixed_roles(string role, bool expected)
    {
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(expected, RoleRules.CanAcknowledge(role));
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public void Jwt_service_requires_a_configured_secret()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var configuration = new ConfigurationBuilder().Build();

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var error = Assert.Throws<InvalidOperationException>(() => new JwtTokenService(configuration));

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Contains("Jwt:Secret", error.Message);
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public void Jwt_service_issues_and_validates_user_claims()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var configuration = new ConfigurationBuilder()
// LEARN: creates a new object or record instance.
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32"
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            })
// LEARN: executes one C# statement; semicolons terminate most statements.
            .Build();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tokens = new JwtTokenService(configuration);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var user = new UserDto(Guid.NewGuid(), Guid.NewGuid(), "admin@example.test", "Admin", Roles.Admin);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var response = tokens.Issue(user, TimeSpan.FromMinutes(5));
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var currentUser = tokens.Validate(response.AccessToken);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.NotNull(currentUser);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(user.Id, currentUser.UserId);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(user.TenantId, currentUser.TenantId);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(user.Email, currentUser.Email);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(user.DisplayName, currentUser.DisplayName);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(user.Role, currentUser.Role);
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public void Alarm_raised_contract_roundtrips_with_web_json_defaults()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var message = new AlarmRaised(
// LEARN: continues an argument/object/collection initializer onto the next line.
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
// LEARN: continues an argument/object/collection initializer onto the next line.
            "demo-operator",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "demo-energy-site",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "chp-demo-001",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "High",
// LEARN: continues an argument/object/collection initializer onto the next line.
            "Electrical output exceeded high threshold.",
// LEARN: executes one C# statement; semicolons terminate most statements.
            DateTimeOffset.Parse("2026-05-28T10:00:00Z"));

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var json = JsonSerializer.Serialize(message, options);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var roundTrip = JsonSerializer.Deserialize<AlarmRaised>(json, options);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal("1.0", AlarmRaised.SchemaVersion);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Contains("\"alarmId\"", json);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.DoesNotContain("AlarmId", json);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(message, roundTrip);
    }
}
