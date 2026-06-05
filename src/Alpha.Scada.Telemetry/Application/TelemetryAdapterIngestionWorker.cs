/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/TelemetryAdapterIngestionWorker.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file is a hosted background process. ASP.NET Core starts it with the service and stops it with the host cancellation token.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: BackgroundService is the .NET hosted-worker base class; ExecuteAsync runs until the host cancellation token is signaled. Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Security.Cryptography;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Application.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NATS.Client.Core;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NATS.Client.JetStream;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NATS.Client.JetStream.Models;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Telemetry.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TelemetryAdapterIngestionWorker(
// LEARN: continues an argument/object/collection initializer onto the next line.
    IConfiguration configuration,
// LEARN: continues an argument/object/collection initializer onto the next line.
    TelemetryAdapterResolver adapters,
// LEARN: continues an argument/object/collection initializer onto the next line.
    CanonicalTelemetryHandler handler,
// LEARN: continues an argument/object/collection initializer onto the next line.
    IMessageBus bus,
// LEARN: inherits from BackgroundService, the ASP.NET Core base type for long-running hosted workers.
    ILogger<TelemetryAdapterIngestionWorker> logger) : BackgroundService
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    private const string DurableName = "telemetry-edge-json";
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

// LEARN: overrides the hosted-worker entry point; the host calls this when the service starts.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(BuildNatsOptions());
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var consumer = await CreateConsumerWhenReadyAsync(jetStream, stoppingToken);

// LEARN: writes structured log output; placeholders become searchable log fields.
        logger.LogInformation("Telemetry adapter listening to {Subject} on {Stream}.", Topics.TelemetryWildcard, Topics.EdgeStream);
// LEARN: asynchronously loops over a stream of values without blocking a thread.
        await foreach (var message in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                           .WithCancellation(stoppingToken))
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await ProcessAsync(connection, message, stoppingToken);
        }
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task<INatsJSConsumer> CreateConsumerWhenReadyAsync(INatsJSContext jetStream, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var consumerConfig = new ConsumerConfig(DurableName)
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            Description = "Normalizes raw Alpha JSON edge telemetry into canonical telemetry.",
// LEARN: continues an argument/object/collection initializer onto the next line.
            FilterSubject = Topics.TelemetryWildcard,
// LEARN: continues an argument/object/collection initializer onto the next line.
            AckWait = TimeSpan.FromSeconds(30),
// LEARN: continues an argument/object/collection initializer onto the next line.
            MaxDeliver = 5,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            MaxAckPending = 1000
        };

// LEARN: starts a loop that continues while its condition remains true.
        while (true)
        {
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
// LEARN: returns a value or exits the current method.
                return await jetStream.CreateOrUpdateConsumerAsync(Topics.EdgeStream, consumerConfig, cancellationToken);
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
// LEARN: throws an exception to signal that this path cannot continue safely.
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
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        message.EnsureSuccess();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var headers = ReadHeaders(message);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var messageId = ResolveMessageId(message.Subject, message.Data, headers);
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var telemetry = adapters.Normalize(message.Data, new TelemetrySource(message.Subject, headers));
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var stored = await handler.Handle(telemetry, cancellationToken);
// LEARN: branches only when the boolean condition is true.
            if (stored is not null)
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await bus.PublishAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                    stored,
// LEARN: creates a new object or record instance.
                    new DeliveryOptions
                    {
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                        DeduplicationId = messageId
// LEARN: executes one C# statement; semicolons terminate most statements.
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
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw;
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (Exception ex)
        {
// LEARN: writes structured log output; placeholders become searchable log fields.
            logger.LogWarning(ex, "Telemetry adapter failed to process {Subject}. Requesting redelivery.", message.Subject);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await message.NakAsync(
// LEARN: creates a new object or record instance.
                new AckOpts
                {
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                    NakDelay = TimeSpan.FromSeconds(1)
                },
// LEARN: executes one C# statement; semicolons terminate most statements.
                cancellationToken);
        }
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task DeadLetterAsync(
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
        NatsConnection connection,
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
        INatsJSMsg<byte[]> message,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string messageId,
// LEARN: continues an argument/object/collection initializer onto the next line.
        Exception exception,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: writes structured log output; placeholders become searchable log fields.
        logger.LogWarning(exception, "Telemetry adapter dead-lettered invalid payload from {Subject}.", message.Subject);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var payload = JsonSerializer.SerializeToUtf8Bytes(
// LEARN: creates a new object or record instance.
            new DeadLetteredTelemetry(message.Subject, messageId, exception.GetType().Name, exception.Message, DateTimeOffset.UtcNow),
// LEARN: executes one C# statement; semicolons terminate most statements.
            JsonOptions);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.PublishAsync(Topics.Dlq("telemetry", message.Subject), payload, cancellationToken: cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await message.AckTerminateAsync(
// LEARN: creates a new object or record instance.
            new AckOpts
            {
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                TerminateReason = exception.Message
            },
// LEARN: executes one C# statement; semicolons terminate most statements.
            cancellationToken);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private NatsOpts BuildNatsOptions()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var options = NatsOptions.FromConfiguration(configuration);
// LEARN: returns a value or exits the current method.
        return new NatsOpts
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            Url = options.Url,
// LEARN: continues an argument/object/collection initializer onto the next line.
            Name = $"alpha-scada-telemetry-adapter-{Environment.MachineName}",
// LEARN: continues an argument/object/collection initializer onto the next line.
            RetryOnInitialConnect = true,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            AuthOpts = string.IsNullOrWhiteSpace(options.User)
// LEARN: creates a new object or record instance.
                ? new NatsAuthOpts()
// LEARN: creates a new object or record instance.
                : new NatsAuthOpts
                {
// LEARN: continues an argument/object/collection initializer onto the next line.
                    Username = options.User,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                    Password = options.Password ?? string.Empty
                }
        };
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static IReadOnlyDictionary<string, string?> ReadHeaders(INatsJSMsg<byte[]> message)
    {
// LEARN: branches only when the boolean condition is true.
        if (message is not NatsJSMsg<byte[]> concrete || concrete.Headers is null)
        {
// LEARN: returns a value or exits the current method.
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

// LEARN: returns a value or exits the current method.
        return concrete.Headers.ToDictionary(
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            header => header.Key,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            header => (string?)header.Value.ToString(),
// LEARN: executes one C# statement; semicolons terminate most statements.
            StringComparer.Ordinal);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static string ResolveMessageId(
// LEARN: continues an argument/object/collection initializer onto the next line.
        string subject,
// LEARN: continues an argument/object/collection initializer onto the next line.
        ReadOnlyMemory<byte> payload,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        IReadOnlyDictionary<string, string?> headers)
    {
// LEARN: branches only when the boolean condition is true.
        if (headers.TryGetValue(RawTelemetryHeaders.NatsMessageId, out var value)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            && !string.IsNullOrWhiteSpace(value))
        {
// LEARN: returns a value or exits the current method.
            return value;
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var subjectBytes = Encoding.UTF8.GetBytes(subject);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var buffer = new byte[subjectBytes.Length + 1 + payload.Length];
// LEARN: executes one C# statement; semicolons terminate most statements.
        subjectBytes.CopyTo(buffer);
// LEARN: executes one C# statement; semicolons terminate most statements.
        buffer[subjectBytes.Length] = 0x1f;
// LEARN: executes one C# statement; semicolons terminate most statements.
        payload.Span.CopyTo(buffer.AsSpan(subjectBytes.Length + 1));
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var hash = SHA256.HashData(buffer);
// LEARN: returns a value or exits the current method.
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private sealed record DeadLetteredTelemetry(
// LEARN: continues an argument/object/collection initializer onto the next line.
        string Subject,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string MessageId,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string ErrorType,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string ErrorMessage,
// LEARN: executes one C# statement; semicolons terminate most statements.
        DateTimeOffset DeadLetteredAtUtc);
}
