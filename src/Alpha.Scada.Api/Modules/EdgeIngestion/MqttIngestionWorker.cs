using System.Buffers;
using System.Text;
using System.Text.Json;
using Alpha.Scada.Api.Contracts;
using Alpha.Scada.Api.Data;
using Alpha.Scada.Api.Modules.Realtime;
using Microsoft.AspNetCore.SignalR;
using MQTTnet;
using MQTTnet.Protocol;

namespace Alpha.Scada.Api.Modules.EdgeIngestion;

public sealed class MqttIngestionWorker(
    IConfiguration configuration,
    PlatformRepository repository,
    IHubContext<TelemetryHub> hub,
    ILogger<MqttIngestionWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Mqtt:Enabled", true))
        {
            logger.LogInformation("MQTT ingestion is disabled by configuration.");
            return;
        }

        var host = configuration.GetValue("Mqtt:Host", "localhost")!;
        var port = configuration.GetValue("Mqtt:Port", 1883);
        var factory = new MqttClientFactory();
        var client = factory.CreateMqttClient();
        var options = factory.CreateClientOptionsBuilder()
            .WithClientId($"alpha-scada-api-{Environment.MachineName}")
            .WithConnectionUri($"mqtt://{host}:{port}")
            .WithCleanSession()
            .Build();

        client.ApplicationMessageReceivedAsync += async args =>
        {
            await HandleMessageAsync(args.ApplicationMessage.Topic, args.ApplicationMessage.Payload.ToArray(), stoppingToken);
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!client.IsConnected)
                {
                    await client.ConnectAsync(options, stoppingToken);
                    await client.SubscribeAsync(
                        factory.CreateSubscribeOptionsBuilder()
                            .WithTopicFilter("alpha/+/+/+/telemetry", MqttQualityOfServiceLevel.AtLeastOnce, false, false, MqttRetainHandling.SendAtSubscribe)
                            .WithTopicFilter("alpha/+/+/+/status", MqttQualityOfServiceLevel.AtLeastOnce, false, false, MqttRetainHandling.SendAtSubscribe)
                            .Build(),
                        stoppingToken);
                    logger.LogInformation("MQTT ingestion connected to {Host}:{Port}.", host, port);
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MQTT ingestion connection failed. Retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleMessageAsync(string topicName, byte[] payload, CancellationToken cancellationToken)
    {
        var topic = TopicParser.Parse(topicName);
        if (topic is null)
        {
            logger.LogWarning("Ignoring unsupported MQTT topic {Topic}.", topicName);
            return;
        }

        if (topic.Kind != "telemetry")
        {
            return;
        }

        var json = Encoding.UTF8.GetString(payload);
        var envelope = JsonSerializer.Deserialize<EdgeTelemetryEnvelope>(json, JsonOptions);
        if (envelope is null || envelope.SchemaVersion != "1.0" || envelope.UnitKey != topic.UnitKey)
        {
            logger.LogWarning("Ignoring invalid telemetry payload on {Topic}.", topicName);
            return;
        }

        await repository.IngestTelemetryAsync(topic.TenantKey, topic.SiteKey, topic.UnitKey, envelope, cancellationToken);
        await repository.EvaluateAlarmsAsync(cancellationToken);

        await hub.Clients.Group($"unit:{topic.UnitKey}").SendAsync("telemetryUpdated", envelope, cancellationToken);
        await hub.Clients.All.SendAsync("unitStatusChanged", new
        {
            topic.TenantKey,
            topic.SiteKey,
            topic.UnitKey,
            status = "online"
        }, cancellationToken);
    }
}
