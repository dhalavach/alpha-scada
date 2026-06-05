/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/NatsSecurityTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Builders;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Configurations;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Containers;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NATS.Client.Core;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Xunit.Sdk;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class NatsSecurityTests
{
// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Nats_rejects_bad_credentials_and_accepts_edge_ingress()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-nats-security-test-{Guid.NewGuid():N}");
// LEARN: executes one C# statement; semicolons terminate most statements.
        Directory.CreateDirectory(tempDir);
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await File.WriteAllTextAsync(Path.Combine(tempDir, "nats-server.conf"), """
                port: 4222
                jetstream {
                  store_dir: "/data/jetstream"
                }
                mqtt {
                  port: 1883
                }
                authorization {
                  users = [
                    { user: "edge", password: "edge-pass" },
                    { user: "services", password: "services-pass" }
                  ]
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
                .WithCommand("-c", "/etc/nats/nats-server.conf")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
// LEARN: executes one C# statement; semicolons terminate most statements.
                .Build();

// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await nats.StartAsync();
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (DockerUnavailableException ex)
            {
// LEARN: throws an exception to signal that this path cannot continue safely.
                throw SkipException.ForSkip($"Docker is not available for NATS integration test: {ex.Message}");
            }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (nats)
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var natsUrl = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.False(await CanConnectAsync(natsUrl, "wrong", "wrong"));

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var received = WaitForSubjectAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                    natsUrl,
// LEARN: executes one C# statement; semicolons terminate most statements.
                    "alpha.demo.site.unit.telemetry");

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await Task.Delay(250);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await PublishAsync(natsUrl, "edge", "edge-pass", "alpha.demo.site.unit.telemetry");

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal("alpha.demo.site.unit.telemetry", await received.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }
// LEARN: runs cleanup code whether or not the try block failed.
        finally
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            Directory.Delete(tempDir, recursive: true);
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<string> WaitForSubjectAsync(string natsUrl, string subject)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(new NatsOpts
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            Url = natsUrl,
// LEARN: continues an argument/object/collection initializer onto the next line.
            RetryOnInitialConnect = true,
// LEARN: creates a new object or record instance.
            AuthOpts = new NatsAuthOpts
            {
// LEARN: continues an argument/object/collection initializer onto the next line.
                Username = "services",
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                Password = "services-pass"
            }
// LEARN: executes one C# statement; semicolons terminate most statements.
        });

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
        using var cts = new CancellationTokenSource();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var receive = Task.Run(async () =>
        {
// LEARN: asynchronously loops over a stream of values without blocking a thread.
            await foreach (var message in connection.SubscribeAsync<byte[]>(subject, cancellationToken: cts.Token)
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
            return await receive.WaitAsync(TimeSpan.FromSeconds(5));
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (TimeoutException)
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await cts.CancelAsync();
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new TimeoutException($"No NATS message arrived on {subject}.");
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<bool> CanConnectAsync(string natsUrl, string user, string password)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(new NatsOpts
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            Url = natsUrl,
// LEARN: continues an argument/object/collection initializer onto the next line.
            RetryOnInitialConnect = false,
// LEARN: creates a new object or record instance.
            AuthOpts = new NatsAuthOpts
            {
// LEARN: continues an argument/object/collection initializer onto the next line.
                Username = user,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                Password = password
            }
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await connection.PingAsync(cancellationToken: cts.Token);
// LEARN: returns a value or exits the current method.
            return true;
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch
        {
// LEARN: returns a value or exits the current method.
            return false;
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task PublishAsync(string natsUrl, string user, string password, string subject)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(new NatsOpts
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            Url = natsUrl,
// LEARN: continues an argument/object/collection initializer onto the next line.
            RetryOnInitialConnect = true,
// LEARN: creates a new object or record instance.
            AuthOpts = new NatsAuthOpts
            {
// LEARN: continues an argument/object/collection initializer onto the next line.
                Username = user,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                Password = password
            }
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.PublishAsync(subject, Array.Empty<byte>());
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.PingAsync();
    }
}
