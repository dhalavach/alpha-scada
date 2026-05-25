using Alpha.Scada.Api.Data;
using Alpha.Scada.Api.Modules.Auth;
using Alpha.Scada.Api.Modules.EdgeIngestion;

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
        var user = new CurrentUser(Guid.NewGuid(), Guid.NewGuid(), "user@example.com", "User", role);

        Assert.Equal(expected, AuthEndpointFilter.CanAcknowledge(user));
    }
}
