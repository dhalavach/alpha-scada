/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/NatsTestSupport.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Builders;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Configurations;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Containers;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NATS.Client.Core;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NATS.Client.JetStream;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a static helper class whose members are called on the type itself.
internal static class NatsTestSupport
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static async Task<IContainer> StartAsync(string tempDir)
    {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await File.WriteAllTextAsync(Path.Combine(tempDir, "nats-server.conf"), """
            port: 4222
            http_port: 8222
            jetstream {
              store_dir: "/data/jetstream"
            }
            mqtt {
              port: 1883
            }
            """);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var nats = new ContainerBuilder()
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithImage(TestImages.Nats)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithBindMount(tempDir, "/etc/nats", AccessMode.ReadOnly)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithPortBinding(4222, true)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithPortBinding(1883, true)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithPortBinding(8222, true)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithCommand("-c", "/etc/nats/nats-server.conf")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
// LEARN: executes one C# statement; semicolons terminate most statements.
            .Build();

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await nats.StartAsync();
// LEARN: returns a value or exits the current method.
        return nats;
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static string Url(IContainer nats) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static async Task PublishAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
        string natsUrl,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string subject,
// LEARN: continues an argument/object/collection initializer onto the next line.
        byte[] payload,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string messageId,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken = default)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(new NatsOpts
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            Url = natsUrl,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            RetryOnInitialConnect = true
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var headers = new NatsHeaders
        {
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            [RawTelemetryHeaders.NatsMessageId] = messageId
        };
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await jetStream.PublishAsync(subject, payload, headers: headers, cancellationToken: cancellationToken);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static async Task<string> WaitForSubjectAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
        string natsUrl,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string subject,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        TimeSpan timeout)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(new NatsOpts
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            Url = natsUrl,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            RetryOnInitialConnect = true
// LEARN: executes one C# statement; semicolons terminate most statements.
        });

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
        using var cts = new CancellationTokenSource();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var receive = Task.Run(async () =>
        {
// LEARN: asynchronously loops over a stream of values without blocking a thread.
            await foreach (var message in connection.SubscribeAsync<string>(subject, cancellationToken: cts.Token)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                               .WithCancellation(cts.Token))
            {
// LEARN: returns a value or exits the current method.
                return message.Subject;
            }

// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new TimeoutException($"No NATS message arrived on {subject}.");
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        }, CancellationToken.None);

// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: returns a value or exits the current method.
            return await receive.WaitAsync(timeout);
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (TimeoutException)
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await cts.CancelAsync();
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new TimeoutException($"No NATS message arrived on {subject} within {timeout}.");
        }
    }
}
