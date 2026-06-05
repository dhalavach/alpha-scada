/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/ContractTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Contracts;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ContractTests
{
// LEARN: marks this method as a single xUnit test case.
    [Fact]
    public void Password_hash_roundtrips_without_storing_plaintext()
    {
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
    public void Alarm_acknowledgement_permissions_follow_fixed_roles(string role, bool expected)
    {
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(expected, RoleRules.CanAcknowledge(role));
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
    public void Jwt_service_requires_a_configured_secret()
    {
        var configuration = new ConfigurationBuilder().Build();

        var error = Assert.Throws<InvalidOperationException>(() => new JwtTokenService(configuration));

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Contains("Jwt:Secret", error.Message);
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
    public void Jwt_service_issues_and_validates_user_claims()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32"
            })
            .Build();
        var tokens = new JwtTokenService(configuration);
        var user = new UserDto(Guid.NewGuid(), Guid.NewGuid(), "admin@example.test", "Admin", Roles.Admin);

        var response = tokens.Issue(user, TimeSpan.FromMinutes(5));
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
    public void Alarm_raised_contract_roundtrips_with_web_json_defaults()
    {
        var message = new AlarmRaised(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "demo-operator",
            "demo-energy-site",
            "chp-demo-001",
            "High",
            "Electrical output exceeded high threshold.",
            DateTimeOffset.Parse("2026-05-28T10:00:00Z"));

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(message, options);
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
