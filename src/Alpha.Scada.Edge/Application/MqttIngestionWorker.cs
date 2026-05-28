using System.Buffers;
using System.Text.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.Edge.Domain;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace Alpha.Scada.Edge.Application;

public sealed class MqttIngestionWorker(
    IConfiguration configuration,
    EdgeTelemetryPipeline pipeline,
    ILogger<MqttIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Mqtt:Enabled", true))
        {
            logger.LogInformation("MQTT ingestion is disabled.");
            return;
        }

        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += async args =>
        {
            try
            {
                var topic = TopicParser.Parse(args.ApplicationMessage.Topic);
                if (topic is null || topic.Kind != "telemetry")
                {
                    return;
                }

                var payloadSegment = args.ApplicationMessage.PayloadSegment;
                var payload = payloadSegment.Array is null
                    ? []
                    : payloadSegment.Array.AsSpan(payloadSegment.Offset, payloadSegment.Count).ToArray();
                var envelope = JsonSerializer.Deserialize<EdgeTelemetryEnvelope>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (envelope is not null)
                {
                    await pipeline.IngestAsync(topic.TenantKey, topic.SiteKey, topic.UnitKey, envelope, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process MQTT message on {Topic}.", args.ApplicationMessage.Topic);
            }
        };

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId($"alpha-scada-edge-{Environment.MachineName}")
            .WithTcpServer(configuration["Mqtt:Host"] ?? "localhost", configuration.GetValue("Mqtt:Port", 1883));

        var mqttUser = configuration["Mqtt:User"];
        var mqttPassword = configuration["Mqtt:Password"];
        if (!string.IsNullOrWhiteSpace(mqttUser))
        {
            optionsBuilder.WithCredentials(mqttUser, mqttPassword);
        }

        var options = optionsBuilder.Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!client.IsConnected)
                {
                    await client.ConnectAsync(options, stoppingToken);
                    await client.SubscribeAsync(
                        new MqttClientSubscribeOptionsBuilder()
                            .WithTopicFilter("alpha/+/+/+/telemetry", MqttQualityOfServiceLevel.AtLeastOnce, false, false, MqttRetainHandling.SendAtSubscribe)
                            .WithTopicFilter("alpha/+/+/+/status", MqttQualityOfServiceLevel.AtLeastOnce, false, false, MqttRetainHandling.SendAtSubscribe)
                            .Build(),
                        stoppingToken);
                    logger.LogInformation("MQTT ingestion connected to {Host}:{Port}.", configuration["Mqtt:Host"] ?? "localhost", configuration.GetValue("Mqtt:Port", 1883));
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MQTT connection failed. Retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
