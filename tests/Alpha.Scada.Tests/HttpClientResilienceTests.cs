/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/HttpClientResilienceTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.DependencyInjection;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class HttpClientResilienceTests
{
// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public void Alpha_resilience_policy_registers_for_named_http_client()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var services = new ServiceCollection();

// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        services
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            .AddHttpClient("downstream", client => client.BaseAddress = new Uri("http://localhost"))
// LEARN: executes one C# statement; semicolons terminate most statements.
            .AddAlphaResilience();

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
        using var provider = services.BuildServiceProvider();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var factory = provider.GetRequiredService<IHttpClientFactory>();

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
        using var client = factory.CreateClient("downstream");

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(new Uri("http://localhost/"), client.BaseAddress);
    }
}
