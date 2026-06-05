/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Edge/Application/ChpUnitSimulatorWorker.cs
- Module role: Alpha.Scada.Edge is the edge/simulator service. In this codebase it stands in for field-side publishers by producing raw telemetry onto NATS subjects.
- Local role: This file is a hosted background process. ASP.NET Core starts it with the service and stops it with the host cancellation token.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: BackgroundService is the .NET hosted-worker base class; ExecuteAsync runs until the host cancellation token is signaled. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Security.Cryptography;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NATS.Client.Core;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NATS.Client.JetStream;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Edge.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ChpUnitSimulatorWorker(
// LEARN: continues an argument/object/collection initializer onto the next line.
    IConfiguration configuration,
// LEARN: inherits from BackgroundService, the ASP.NET Core base type for long-running hosted workers.
    ILogger<ChpUnitSimulatorWorker> logger) : BackgroundService
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private readonly Random _random = new();

// LEARN: overrides the hosted-worker entry point; the host calls this when the service starts.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
// LEARN: branches only when the boolean condition is true.
        if (!configuration.GetValue("Simulator:Enabled", true))
        {
// LEARN: writes structured log output; placeholders become searchable log fields.
            logger.LogInformation("Combined heat and power unit simulator is disabled.");
// LEARN: returns a value or exits the current method.
            return;
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(BuildNatsOptions());
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var subject = Topics.Telemetry("demo-operator", "demo-energy-site", "chp-demo-001");

// LEARN: writes structured log output; placeholders become searchable log fields.
        logger.LogInformation("Starting edge telemetry simulator.");
// LEARN: starts a loop that continues while its condition remains true.
        while (!stoppingToken.IsCancellationRequested)
        {
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var now = DateTimeOffset.UtcNow;
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var wave = Math.Sin(now.ToUnixTimeSeconds() / 30.0);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var envelope = new EdgeTelemetryEnvelope("1.0", "chp-demo-001", now, CreateSamples(now, wave));
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, jsonOptions);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var messageId = DeterministicMessageId(subject, payload);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var headers = new NatsHeaders
                {
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                    [RawTelemetryHeaders.NatsMessageId] = messageId
                };

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await jetStream.PublishAsync(subject, payload, headers: headers, cancellationToken: stoppingToken);
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
// LEARN: executes one C# statement; semicolons terminate most statements.
                break;
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (Exception ex)
            {
// LEARN: writes structured log output; placeholders become searchable log fields.
                logger.LogWarning(ex, "Simulator telemetry publish failed. Retrying.");
            }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private NatsOpts BuildNatsOptions()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var options = NatsOptions.FromConfiguration(configuration);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var user = configuration["Simulator:NatsUser"] ?? configuration["Nats:User"];
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var password = configuration["Simulator:NatsPassword"] ?? configuration["Nats:Password"];
// LEARN: returns a value or exits the current method.
        return new NatsOpts
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            Url = options.Url,
// LEARN: continues an argument/object/collection initializer onto the next line.
            Name = $"alpha-scada-simulator-{Environment.MachineName}",
// LEARN: continues an argument/object/collection initializer onto the next line.
            RetryOnInitialConnect = true,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            AuthOpts = string.IsNullOrWhiteSpace(user)
// LEARN: creates a new object or record instance.
                ? new NatsAuthOpts()
// LEARN: creates a new object or record instance.
                : new NatsAuthOpts
                {
// LEARN: continues an argument/object/collection initializer onto the next line.
                    Username = user,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                    Password = password ?? string.Empty
                }
        };
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private IReadOnlyCollection<EdgeTelemetrySample> CreateSamples(DateTimeOffset timestamp, double wave)
    {
// LEARN: returns a value or exits the current method.
        return
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        [
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("fuel.wood_chip_feed_kg_h", 55, 4, wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("gasifier.reactor_temp_c", 780, 35, wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("gas_cleaning.filter_dp_mbar", 42, 8, wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("engine.electrical_output_kw", 58, 3, wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("engine.oil_reservoir_l", 24, 0.15, -wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("heat.thermal_output_kw", 134, 7, wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("heat.supply_temp_c", 88, 3, wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("heat.return_temp_c", 55, 3, -wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("biochar.output_m3_day", 0.62, 0.08, wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("exhaust.temperature_c", 132, 6, wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("air.compressed_pressure_bar", 8.1, 0.25, wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("ventilation.air_exchange_m3_h", 820, 25, wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("safety.negative_pressure_pa", -120, 18, wave, timestamp),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Sample("safety.co_ppm", 8, 3, wave, timestamp),
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            new("safety.fire_suppression_ready", 1, "good", timestamp)
        ];
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private EdgeTelemetrySample Sample(string key, double center, double spread, double wave, DateTimeOffset timestamp)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var noise = (_random.NextDouble() - 0.5) * spread;
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var value = center + (spread * 0.6 * wave) + noise;
// LEARN: returns a value or exits the current method.
        return new EdgeTelemetrySample(key, Math.Round(value, 2), "good", timestamp);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static string DeterministicMessageId(string subject, ReadOnlySpan<byte> payload)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var subjectBytes = System.Text.Encoding.UTF8.GetBytes(subject);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var buffer = new byte[subjectBytes.Length + 1 + payload.Length];
// LEARN: executes one C# statement; semicolons terminate most statements.
        subjectBytes.CopyTo(buffer);
// LEARN: executes one C# statement; semicolons terminate most statements.
        buffer[subjectBytes.Length] = 0x1f;
// LEARN: executes one C# statement; semicolons terminate most statements.
        payload.CopyTo(buffer.AsSpan(subjectBytes.Length + 1));
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var hash = SHA256.HashData(buffer);
// LEARN: returns a value or exits the current method.
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }
}
