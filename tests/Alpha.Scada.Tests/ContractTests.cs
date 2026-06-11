using Alpha.Scada.Contracts;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Contracts.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Alpha.Scada.Tests;

public sealed class ContractTests
{
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

        Assert.Equal("1.0", AlarmRaised.SchemaVersion);
        Assert.Contains("\"alarmId\"", json);
        Assert.DoesNotContain("AlarmId", json);
        Assert.Equal(message, roundTrip);
    }

    [Fact]
    public void Telemetry_v1_contract_preserves_the_edge_wire_shape()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-11T12:00:00Z");
        var envelope = new TelemetryEnvelopeV1(
            TelemetryEnvelopeV1.SchemaVersion,
            "chp-demo-001",
            timestamp,
            [new TelemetrySampleV1("engine.output_kw", 42.5, "good", timestamp)]);

        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"schemaVersion\":\"1.0\"", json);
        Assert.Contains("\"unitKey\":\"chp-demo-001\"", json);
        Assert.Contains("\"tagKey\":\"engine.output_kw\"", json);
    }

    [Fact]
    public void Deterministic_message_ids_are_stable_and_subject_scoped()
    {
        ReadOnlySpan<byte> payload = """{"value":42}"""u8;

        var first = MessageIds.Deterministic("alpha.demo.site.unit.telemetry", payload);
        var repeat = MessageIds.Deterministic("alpha.demo.site.unit.telemetry", payload);
        var otherSubject = MessageIds.Deterministic("alpha.demo.site.other.telemetry", payload);

        Assert.Equal(first, repeat);
        Assert.NotEqual(first, otherSubject);
        Assert.True(Guid.TryParse(first, out _));
    }
}
