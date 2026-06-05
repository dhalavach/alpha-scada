using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Application.Messaging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Wolverine;

namespace Alpha.Scada.Telemetry.Application;

public sealed class TelemetryAdapterIngestionWorker(
    IConfiguration configuration,
    TelemetryAdapterResolver adapters,
    CanonicalTelemetryHandler handler,
    IMessageBus bus,
    ILogger<TelemetryAdapterIngestionWorker> logger) : BackgroundService
{
    private const string DurableName = "telemetry-edge-json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var connection = new NatsConnection(BuildNatsOptions());
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
        var consumer = await CreateConsumerWhenReadyAsync(jetStream, stoppingToken);

        logger.LogInformation("Telemetry adapter listening to {Subject} on {Stream}.", Topics.TelemetryWildcard, Topics.EdgeStream);
        await foreach (var message in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken)
                           .WithCancellation(stoppingToken))
        {
            await ProcessAsync(connection, message, stoppingToken);
        }
    }

    private async Task<INatsJSConsumer> CreateConsumerWhenReadyAsync(INatsJSContext jetStream, CancellationToken cancellationToken)
    {
        var consumerConfig = new ConsumerConfig(DurableName)
        {
            Description = "Normalizes raw Alpha JSON edge telemetry into canonical telemetry.",
            FilterSubject = Topics.TelemetryWildcard,
            AckWait = TimeSpan.FromSeconds(30),
            MaxDeliver = 5,
            MaxAckPending = 1000
        };

        while (true)
        {
            try
            {
                return await jetStream.CreateOrUpdateConsumerAsync(Topics.EdgeStream, consumerConfig, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telemetry adapter could not create NATS consumer yet. Retrying.");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    private async Task ProcessAsync(
        NatsConnection connection,
        INatsJSMsg<byte[]> message,
        CancellationToken cancellationToken)
    {
        message.EnsureSuccess();
        var headers = ReadHeaders(message);
        var messageId = ResolveMessageId(message.Subject, message.Data, headers);
        try
        {
            var telemetry = adapters.Normalize(message.Data, new TelemetrySource(message.Subject, headers));
            var stored = await handler.Handle(telemetry, cancellationToken);
            if (stored is not null)
            {
                await bus.PublishAsync(
                    stored,
                    new DeliveryOptions
                    {
                        DeduplicationId = messageId
                    }.WithHeader(RawTelemetryHeaders.NatsMessageId, messageId));
            }

            await message.AckAsync(cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            await DeadLetterAsync(connection, message, messageId, ex, cancellationToken);
        }
        catch (InvalidTelemetryEnvelopeException ex)
        {
            await DeadLetterAsync(connection, message, messageId, ex, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telemetry adapter failed to process {Subject}. Requesting redelivery.", message.Subject);
            await message.NakAsync(
                new AckOpts
                {
                    NakDelay = TimeSpan.FromSeconds(1)
                },
                cancellationToken);
        }
    }

    private async Task DeadLetterAsync(
        NatsConnection connection,
        INatsJSMsg<byte[]> message,
        string messageId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(exception, "Telemetry adapter dead-lettered invalid payload from {Subject}.", message.Subject);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new DeadLetteredTelemetry(message.Subject, messageId, exception.GetType().Name, exception.Message, DateTimeOffset.UtcNow),
            JsonOptions);
        await connection.PublishAsync(Topics.Dlq("telemetry", message.Subject), payload, cancellationToken: cancellationToken);
        await message.AckTerminateAsync(
            new AckOpts
            {
                TerminateReason = exception.Message
            },
            cancellationToken);
    }

    private NatsOpts BuildNatsOptions()
    {
        var options = NatsOptions.FromConfiguration(configuration);
        return new NatsOpts
        {
            Url = options.Url,
            Name = $"alpha-scada-telemetry-adapter-{Environment.MachineName}",
            RetryOnInitialConnect = true,
            AuthOpts = string.IsNullOrWhiteSpace(options.User)
                ? new NatsAuthOpts()
                : new NatsAuthOpts
                {
                    Username = options.User,
                    Password = options.Password ?? string.Empty
                }
        };
    }

    private static IReadOnlyDictionary<string, string?> ReadHeaders(INatsJSMsg<byte[]> message)
    {
        if (message is not NatsJSMsg<byte[]> concrete || concrete.Headers is null)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        return concrete.Headers.ToDictionary(
            header => header.Key,
            header => (string?)header.Value.ToString(),
            StringComparer.Ordinal);
    }

    private static string ResolveMessageId(
        string subject,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string?> headers)
    {
        if (headers.TryGetValue(RawTelemetryHeaders.NatsMessageId, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var subjectBytes = Encoding.UTF8.GetBytes(subject);
        var buffer = new byte[subjectBytes.Length + 1 + payload.Length];
        subjectBytes.CopyTo(buffer);
        buffer[subjectBytes.Length] = 0x1f;
        payload.Span.CopyTo(buffer.AsSpan(subjectBytes.Length + 1));
        var hash = SHA256.HashData(buffer);
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }

    private sealed record DeadLetteredTelemetry(
        string Subject,
        string MessageId,
        string ErrorType,
        string ErrorMessage,
        DateTimeOffset DeadLetteredAtUtc);
}
