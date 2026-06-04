using System.Text.Json;
using Alpha.Scada.Contracts.Messaging;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Application.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Npgsql;

namespace Alpha.Scada.Telemetry.Application;

public sealed class TelemetryEdgeIngestionWorker(
    IConfiguration configuration,
    CatalogCache catalog,
    TelemetryRepository repository,
    NpgsqlDataSource dataSource,
    WolverineTransactionalOutbox outbox,
    ILogger<TelemetryEdgeIngestionWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var connection = new NatsConnection(BuildOptions());
        var jetStream = new NatsJSContextFactory().CreateContext(connection);

        await EnsureStreamAsync(jetStream, stoppingToken);
        var consumer = await jetStream.CreateOrUpdateConsumerAsync(
            Topics.EdgeStream,
            new ConsumerConfig("telemetry-edge-json")
            {
                FilterSubject = Topics.TelemetryWildcard,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                AckWait = TimeSpan.FromSeconds(30),
                MaxDeliver = 5
            },
            stoppingToken);

        logger.LogInformation("Telemetry raw NATS JetStream consumer is listening on {Subject}.", Topics.TelemetryWildcard);
        await foreach (var message in consumer.ConsumeAsync<string>(cancellationToken: stoppingToken))
        {
            await ProcessMessageAsync(message, stoppingToken);
        }
    }

    private NatsOpts BuildOptions()
    {
        var user = configuration["Nats:User"];
        var password = configuration["Nats:Password"];
        return new NatsOpts
        {
            Url = configuration["Nats:Url"] ?? "nats://localhost:4222",
            Name = "alpha-scada-telemetry-edge-ingestion",
            RetryOnInitialConnect = true,
            AuthOpts = string.IsNullOrWhiteSpace(user)
                ? new NatsAuthOpts()
                : new NatsAuthOpts
                {
                    Username = user,
                    Password = password ?? string.Empty
                }
        };
    }

    private static async Task EnsureStreamAsync(INatsJSContext jetStream, CancellationToken cancellationToken)
    {
        await jetStream.CreateOrUpdateStreamAsync(
            new StreamConfig(
                Topics.EdgeStream,
                [Topics.TelemetryWildcard, Topics.SparkplugWildcard])
            {
                Retention = StreamConfigRetention.Limits,
                Storage = StreamConfigStorage.File,
                MaxAge = TimeSpan.FromDays(7),
                DuplicateWindow = TimeSpan.FromMinutes(10)
            },
            cancellationToken);
    }

    private async Task ProcessMessageAsync(INatsJSMsg<string> message, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessTelemetryAsync(message.Subject, message.Data ?? string.Empty, cancellationToken);
            await message.AckAsync(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Terminating malformed telemetry payload from {Subject}.", message.Subject);
            await message.AckTerminateAsync(new AckOpts { TerminateReason = "Malformed telemetry JSON." }, cancellationToken);
        }
        catch (InvalidTelemetryEnvelopeException ex)
        {
            logger.LogWarning(ex, "Terminating unsupported telemetry payload from {Subject}.", message.Subject);
            await message.AckTerminateAsync(new AckOpts { TerminateReason = ex.Message }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telemetry payload from {Subject} failed. Nacking for retry.", message.Subject);
            await message.NakAsync(new AckOpts { NakDelay = TimeSpan.FromSeconds(5) }, cancellationToken);
        }
    }

    private async Task ProcessTelemetryAsync(string subject, string payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new JsonException("Telemetry payload was empty.");
        }

        var envelope = JsonSerializer.Deserialize<TelemetryEnvelopeV1>(payload, JsonOptions)
            ?? throw new JsonException("Telemetry payload was empty.");
        EnsureSupportedSchema(envelope.PayloadSchemaVersion);

        var topic = TelemetryTopicParser.Parse(subject);
        if (topic is null)
        {
            throw new InvalidTelemetryEnvelopeException($"Telemetry arrived on invalid subject '{subject}'.");
        }

        var batch = await catalog.ResolveAsync(topic, envelope, cancellationToken);
        if (batch is null)
        {
            logger.LogWarning("Telemetry envelope contained no resolvable samples for {Subject}.", subject);
            return;
        }

        var stored = new TelemetryBatchStored(
            batch.TenantId,
            batch.UnitId,
            batch.TenantKey,
            batch.SiteKey,
            batch.UnitKey,
            DateTimeOffset.UtcNow,
            batch.Samples.Select(sample => new StoredSample(
                sample.TagId,
                sample.TagKey,
                sample.Value,
                sample.Quality,
                sample.SourceTimestampUtc)).ToArray());

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await repository.IngestAsync(connection, transaction, new(batch.TenantId, batch.UnitId, batch.Samples), cancellationToken);
        var outboxBatch = await outbox.StoreAsync(connection, transaction, stored, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.PublishAndClearAsync(outboxBatch, cancellationToken);
    }

    private void EnsureSupportedSchema(string schemaVersion)
    {
        var parts = schemaVersion.Split('.', 2);
        if (parts.Length == 0 || parts[0] != "1")
        {
            throw new InvalidTelemetryEnvelopeException($"Unsupported telemetry schema version '{schemaVersion}'.");
        }

        if (schemaVersion != TelemetryEnvelopeV1.SchemaVersion)
        {
            logger.LogWarning("Processing newer compatible telemetry schema version {SchemaVersion}.", schemaVersion);
        }
    }
}

public sealed class InvalidTelemetryEnvelopeException(string message) : Exception(message);
