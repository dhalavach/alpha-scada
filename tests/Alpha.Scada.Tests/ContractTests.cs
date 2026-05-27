using Alpha.Scada.Contracts;
using Alpha.Scada.Edge.Domain;
using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Alpha.Scada.Tests;

public sealed class ContractTests
{
    [Fact]
    public void Topic_parser_accepts_telemetry_contract_topic()
    {
        var topic = TopicParser.Parse("alpha/demo-operator/demo-energy-site/chp-demo-001/telemetry");

        Assert.NotNull(topic);
        Assert.Equal("demo-operator", topic.TenantKey);
        Assert.Equal("demo-energy-site", topic.SiteKey);
        Assert.Equal("chp-demo-001", topic.UnitKey);
        Assert.Equal("telemetry", topic.Kind);
    }

    [Theory]
    [InlineData("other/demo-operator/demo-energy-site/chp-demo-001/telemetry")]
    [InlineData("alpha/demo-operator/demo-energy-site/chp-demo-001/control")]
    [InlineData("alpha/demo-operator/demo-energy-site/telemetry")]
    public void Topic_parser_rejects_unsupported_topics(string topicName)
    {
        Assert.Null(TopicParser.Parse(topicName));
    }

    [Fact]
    public void Password_hash_roundtrips_without_storing_plaintext()
    {
        var hash = PasswordHasher.Hash("ChangeMe!123");

        Assert.NotEqual("ChangeMe!123", hash);
        Assert.True(PasswordHasher.Verify("ChangeMe!123", hash));
        Assert.False(PasswordHasher.Verify("wrong", hash));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Operator, true)]
    [InlineData(Roles.Viewer, false)]
    [InlineData(Roles.SupportEngineer, true)]
    public void Alarm_acknowledgement_permissions_follow_fixed_roles(string role, bool expected)
    {
        Assert.Equal(expected, RoleRules.CanAcknowledge(role));
    }

    [Fact]
    public void User_context_rejects_handcrafted_identity_headers_without_bearer_token()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32"
            })
            .Build();
        var tokens = new JwtTokenService(configuration);
        var headers = new HeaderDictionary
        {
            ["X-User-Id"] = Guid.NewGuid().ToString(),
            ["X-Tenant-Id"] = Guid.NewGuid().ToString(),
            ["X-User-Email"] = "attacker@example.test",
            ["X-User-Name"] = "Attacker",
            ["X-User-Role"] = Roles.Admin
        };

        Assert.Null(HttpUserContext.FromBearerToken(headers, tokens));
    }

    [Fact]
    public void Jwt_service_requires_a_configured_secret()
    {
        var configuration = new ConfigurationBuilder().Build();

        var error = Assert.Throws<InvalidOperationException>(() => new JwtTokenService(configuration));

        Assert.Contains("Jwt:Secret", error.Message);
    }

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

        Assert.NotNull(currentUser);
        Assert.Equal(user.Id, currentUser.UserId);
        Assert.Equal(user.TenantId, currentUser.TenantId);
        Assert.Equal(user.Email, currentUser.Email);
        Assert.Equal(user.DisplayName, currentUser.DisplayName);
        Assert.Equal(user.Role, currentUser.Role);
    }
}
