/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/TelemetryAdapterIngestionWorker.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file is a hosted background process. ASP.NET Core starts it with the service and stops it with the host cancellation token.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: BackgroundService is the .NET hosted-worker base class; ExecuteAsync runs until the host cancellation token is signaled. Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

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

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TelemetryAdapterIngestionWorker(
    IConfiguration configuration,
    TelemetryAdapterResolver adapters,
    CanonicalTelemetryHandler handler,
    IMessageBus bus,
// LEARN: inherits from BackgroundService, the ASP.NET Core base type for long-running hosted workers.
    ILogger<TelemetryAdapterIngestionWorker> logger) : BackgroundService
{
    private const string DurableName = "telemetry-edge-json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

// LEARN: overrides the hosted-worker entry point; the host calls this when the service starts.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(BuildNatsOptions());
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
        var consumer = await CreateConsumerWhenReadyAsync(jetStream, stoppingToken);

// LEARN: writes structured log output; placeholders become searchable log fields.
        logger.LogInformation("Telemetry adapter listening to {Subject} on {Stream}.", Topics.TelemetryWildcard, Topics.EdgeStream);
// LEARN: asynchronously loops over a stream of values without blocking a thread.
        await foreach (var message in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken)
                           .WithCancellation(stoppingToken))
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await ProcessAsync(connection, message, stoppingToken);
        }
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
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

// LEARN: starts a loop that continues while its condition remains true.
        while (true)
        {
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
                return await jetStream.CreateOrUpdateConsumerAsync(Topics.EdgeStream, consumerConfig, cancellationToken);
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (Exception ex)
            {
// LEARN: writes structured log output; placeholders become searchable log fields.
                logger.LogWarning(ex, "Telemetry adapter could not create NATS consumer yet. Retrying.");
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task ProcessAsync(
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
        NatsConnection connection,
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
        INatsJSMsg<byte[]> message,
        CancellationToken cancellationToken)
    {
        message.EnsureSuccess();
        var headers = ReadHeaders(message);
        var messageId = ResolveMessageId(message.Subject, message.Data, headers);
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
            var telemetry = adapters.Normalize(message.Data, new TelemetrySource(message.Subject, headers));
            var stored = await handler.Handle(telemetry, cancellationToken);
// LEARN: branches only when the boolean condition is true.
            if (stored is not null)
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await bus.PublishAsync(
                    stored,
                    new DeliveryOptions
                    {
                        DeduplicationId = messageId
                    }.WithHeader(RawTelemetryHeaders.NatsMessageId, messageId));
            }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await message.AckAsync(cancellationToken: cancellationToken);
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (JsonException ex)
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await DeadLetterAsync(connection, message, messageId, ex, cancellationToken);
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (InvalidTelemetryEnvelopeException ex)
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await DeadLetterAsync(connection, message, messageId, ex, cancellationToken);
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (Exception ex)
        {
// LEARN: writes structured log output; placeholders become searchable log fields.
            logger.LogWarning(ex, "Telemetry adapter failed to process {Subject}. Requesting redelivery.", message.Subject);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await message.NakAsync(
                new AckOpts
                {
                    NakDelay = TimeSpan.FromSeconds(1)
                },
                cancellationToken);
        }
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task DeadLetterAsync(
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
        NatsConnection connection,
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
        INatsJSMsg<byte[]> message,
        string messageId,
        Exception exception,
        CancellationToken cancellationToken)
    {
// LEARN: writes structured log output; placeholders become searchable log fields.
        logger.LogWarning(exception, "Telemetry adapter dead-lettered invalid payload from {Subject}.", message.Subject);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new DeadLetteredTelemetry(message.Subject, messageId, exception.GetType().Name, exception.Message, DateTimeOffset.UtcNow),
            JsonOptions);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.PublishAsync(Topics.Dlq("telemetry", message.Subject), payload, cancellationToken: cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
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
// LEARN: branches only when the boolean condition is true.
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
// LEARN: branches only when the boolean condition is true.
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
