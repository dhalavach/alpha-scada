using System.Text;
using System.Text.Json;
using Alpha.Scada.Alarm.Domain;
using Alpha.Scada.Contracts;
using Alpha.Scada.Contracts.Messaging;
using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Application;
using Alpha.Scada.Telemetry.Application.Messaging;

namespace Alpha.Scada.Tests;

public sealed class DomainRuleTests
{
    [Theory]
    [InlineData("Safety", "bad", 50, null, null, true, "critical")]
    [InlineData("Engine", "uncertain", 50, null, null, true, "warning")]
    [InlineData("Engine", "good", -1.0, 0.0, 100.0, true, "warning")]
    [InlineData("Engine", "good", 101.0, 0.0, 100.0, true, "warning")]
    [InlineData("Engine", "good", 50.0, 0.0, 100.0, false, "")]
    public void Alarm_rule_evaluates_quality_and_thresholds(
        string subsystem,
        string quality,
        double value,
        double? low,
        double? high,
        bool expectedAlarm,
        string expectedSeverity)
    {
        var result = AlarmRule.Evaluate(new ResolvedTelemetrySample(
            Guid.NewGuid(),
            "engine.electrical_output_kw",
            "Electrical Output",
            subsystem,
            "kW",
            low,
            high,
            value,
            quality,
            DateTimeOffset.UtcNow));

        Assert.Equal(expectedAlarm, result.IsAlarm);
        Assert.Equal(expectedSeverity, result.Severity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("alpha.demo.site.unit")]
    [InlineData("other.demo.site.unit.telemetry")]
    [InlineData("alpha.demo.site.unit.status")]
    [InlineData("alpha.demo.site.unit.telemetry.extra")]
    [InlineData("alpha/demo/site/unit/telemetry")]
    [InlineData("alpha.demo.site.unit/with-slash.telemetry")]
    [InlineData("alpha.de.mo.site.unit.telemetry")]
    [InlineData("alpha.demo.si.te.unit.telemetry")]
    [InlineData("alpha.demo.site.un.it.telemetry")]
    [InlineData("alpha.demo.site.extra.segment.telemetry")]
    public void Telemetry_topic_parser_rejects_invalid_topics(string topic)
    {
        Assert.Null(TelemetryTopicParser.Parse(topic));
    }

    [Fact]
    public void Telemetry_topic_parser_extracts_route_keys()
    {
        var parsed = TelemetryTopicParser.Parse("alpha.demo.site.unit-001.telemetry");

        Assert.NotNull(parsed);
        Assert.Equal(new TelemetryTopic("demo", "site", "unit-001"), parsed);
    }

    [Fact]
    public void Telemetry_subject_helper_uses_native_nats_dots()
    {
        Assert.Equal(
            "alpha.demo-operator.demo-energy-site.chp-demo-001.telemetry",
            Topics.Telemetry("demo-operator", "demo-energy-site", "chp-demo-001"));
    }

    [Fact]
    public void Nats_json_telemetry_adapter_normalizes_header_free_payload()
    {
        var adapter = new NatsJsonTelemetryAdapter();
        var timestamp = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new TelemetryEnvelopeV1(
                TelemetryEnvelopeV1.SchemaVersion,
                "chp-demo-001",
                timestamp,
                [new("engine.electrical_output_kw", 61.2, "good", timestamp)]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var telemetry = adapter.Normalize(
            payload,
            new TelemetrySource(
                Topics.Telemetry("demo-operator", "demo-energy-site", "chp-demo-001"),
                new Dictionary<string, string?>()));

        Assert.Equal("demo-operator", telemetry.TenantKey);
        Assert.Equal("demo-energy-site", telemetry.SiteKey);
        Assert.Equal("chp-demo-001", telemetry.UnitKey);
        var reading = Assert.Single(telemetry.Readings);
        Assert.Equal("engine.electrical_output_kw", reading.TagKey);
        Assert.Equal(61.2, reading.Value);
        Assert.Equal("good", reading.Quality);
    }

    [Fact]
    public void Nats_json_telemetry_adapter_rejects_unsupported_schema()
    {
        var adapter = new NatsJsonTelemetryAdapter();
        var timestamp = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new TelemetryEnvelopeV1("2.0", "chp-demo-001", timestamp, []),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Throws<InvalidTelemetryEnvelopeException>(() =>
            adapter.Normalize(
                payload,
                new TelemetrySource(
                    Topics.Telemetry("demo-operator", "demo-energy-site", "chp-demo-001"),
                    new Dictionary<string, string?>())));
    }

    [Fact]
    public void Nats_json_telemetry_adapter_surfaces_malformed_json()
    {
        var adapter = new NatsJsonTelemetryAdapter();

        Assert.Throws<JsonException>(() =>
            adapter.Normalize(
                Encoding.UTF8.GetBytes("{"),
                new TelemetrySource(
                    Topics.Telemetry("demo-operator", "demo-energy-site", "chp-demo-001"),
                    new Dictionary<string, string?>())));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256.nope.salt.key")]
    [InlineData("pbkdf2-sha256.100000.not-base64.key")]
    public void Password_verify_fails_closed_for_malformed_hashes(string hash)
    {
        Assert.False(PasswordHasher.Verify("password", hash));
    }

    [Fact]
    public void Telemetry_envelope_schema_constant_matches_wire_version()
    {
        var envelope = new TelemetryEnvelopeV1(
            TelemetryEnvelopeV1.SchemaVersion,
            "unit-001",
            DateTimeOffset.UtcNow,
            []);

        Assert.Equal("1.0", envelope.PayloadSchemaVersion);
    }
}
