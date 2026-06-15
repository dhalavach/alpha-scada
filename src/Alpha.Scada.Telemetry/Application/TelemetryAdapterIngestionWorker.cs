using System.Diagnostics;
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
    TelemetryIngestionOptions ingestionOptions,
    TelemetryIngestionMetrics metrics,
    ILogger<TelemetryAdapterIngestionWorker> logger) : BackgroundService
{
    private const string DurableName = "telemetry-edge-json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var connection = new NatsConnection(BuildNatsOptions());
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
        var consumer = await CreateConsumerWhenReadyAsync(jetStream, stoppingToken);
        var degreeOfParallelism = ingestionOptions.EffectiveMaxDegreeOfParallelism;
        var maxDeliveriesWatchdog = WatchMaxDeliveriesAsync(connection, stoppingToken);

        logger.LogInformation(
            "Telemetry adapter listening to {Subject} on {Stream} with max degree of parallelism {MaxDegreeOfParallelism}.",
            Topics.TelemetryWildcard,
            Topics.EdgeStream,
            degreeOfParallelism);
        await TelemetryParallelPump.RunSafelyAsync(
            consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken),
            degreeOfParallelism,
            async (message, ct) => await ProcessOneMeasuredAsync(jetStream, message, ct),
            ex => logger.LogError(ex, "Telemetry adapter message processing escaped its guarded path."),
            stoppingToken);
        await maxDeliveriesWatchdog;
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

    private async ValueTask ProcessOneMeasuredAsync(
        INatsJSContext jetStream,
        INatsJSMsg<byte[]> message,
        CancellationToken cancellationToken)
    {
        var measurement = metrics.Begin();
        try
        {
            measurement.Complete(await ProcessAsync(jetStream, message, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            measurement.Complete(TelemetryIngestionOutcome.EscapedError);
            throw;
        }
    }

    private async Task<TelemetryIngestionOutcome> ProcessAsync(
        INatsJSContext jetStream,
        INatsJSMsg<byte[]> message,
        CancellationToken cancellationToken)
    {
        var headers = ReadHeaders(message);
        var messageId = ResolveMessageId(message.Subject, message.Data, headers);
        using var activity = metrics.StartIngestion(message.Subject, messageId);
        try
        {
            message.EnsureSuccess();
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

            var outcome = await AckAsync(
                message,
                stored is null ? TelemetryIngestionOutcome.Dropped : TelemetryIngestionOutcome.Success,
                cancellationToken);
            activity?.SetTag("alpha.telemetry.ingestion.outcome", outcome.ToString());
            return outcome;
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return await DeadLetterAsync(jetStream, message, messageId, ex, cancellationToken);
        }
        catch (InvalidTelemetryEnvelopeException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return await DeadLetterAsync(jetStream, message, messageId, ex, cancellationToken);
        }
        catch (TelemetryResolutionException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return await DeadLetterAsync(jetStream, message, messageId, ex, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogWarning(ex, "Telemetry adapter failed to process {Subject}. Requesting redelivery.", message.Subject);
            return await NakAsync(message, cancellationToken);
        }
    }

    private async Task<TelemetryIngestionOutcome> AckAsync(
        INatsJSMsg<byte[]> message,
        TelemetryIngestionOutcome successOutcome,
        CancellationToken cancellationToken)
    {
        try
        {
            await message.AckAsync(cancellationToken: cancellationToken);
            return successOutcome;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telemetry adapter could not ack {Subject}; JetStream will redeliver after AckWait.", message.Subject);
            return TelemetryIngestionOutcome.TerminalError;
        }
    }

    private async Task<TelemetryIngestionOutcome> NakAsync(
        INatsJSMsg<byte[]> message,
        CancellationToken cancellationToken)
    {
        try
        {
            await message.NakAsync(
                new AckOpts
                {
                    NakDelay = TimeSpan.FromSeconds(1)
                },
                cancellationToken);
            return TelemetryIngestionOutcome.Retry;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telemetry adapter could not request redelivery for {Subject}; JetStream will redeliver after AckWait.", message.Subject);
            return TelemetryIngestionOutcome.TerminalError;
        }
    }

    private async Task<TelemetryIngestionOutcome> DeadLetterAsync(
        INatsJSContext jetStream,
        INatsJSMsg<byte[]> message,
        string messageId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(exception, "Telemetry adapter dead-lettered invalid payload from {Subject}.", message.Subject);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            DeadLetteredTelemetryFactory.Create(message.Subject, messageId, exception, message.Data, DateTimeOffset.UtcNow),
            JsonOptions);
        var headers = new NatsHeaders
        {
            [RawTelemetryHeaders.NatsMessageId] = messageId
        };
        try
        {
            var ack = await jetStream.PublishAsync(
                Topics.Dlq("telemetry", message.Subject),
                payload,
                headers: headers,
                cancellationToken: cancellationToken);
            ack.EnsureSuccess();
            await message.AckTerminateAsync(
                new AckOpts
                {
                    TerminateReason = exception.Message
                },
                cancellationToken);
            return TelemetryIngestionOutcome.DeadLetter;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telemetry adapter could not complete dead-letter handling for {Subject}; JetStream will redeliver after AckWait.", message.Subject);
            return TelemetryIngestionOutcome.TerminalError;
        }
    }

    private async Task WatchMaxDeliveriesAsync(NatsConnection connection, CancellationToken cancellationToken)
    {
        const string advisorySubject = "$JS.EVENT.ADVISORY.CONSUMER.MAX_DELIVERIES.ALPHA_EDGE.telemetry-edge-json";
        try
        {
            await foreach (var advisory in connection.SubscribeAsync<string>(advisorySubject, cancellationToken: cancellationToken)
                               .WithCancellation(cancellationToken))
            {
                metrics.RecordMaxDeliveriesExhausted();
                logger.LogError(
                    "Telemetry adapter message exhausted max JetStream deliveries. Advisory: {Advisory}",
                    advisory.Data);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telemetry adapter max-deliveries advisory watcher stopped unexpectedly.");
        }
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

        return MessageIds.Deterministic(subject, payload.Span);
    }

}
