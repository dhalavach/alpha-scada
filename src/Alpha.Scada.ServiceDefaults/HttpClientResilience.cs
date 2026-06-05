/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/HttpClientResilience.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.DependencyInjection;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Http.Resilience;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class HttpClientResilience
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static IHttpClientBuilder AddAlphaResilience(this IHttpClientBuilder builder)
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        builder.AddStandardResilienceHandler(options =>
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
// LEARN: executes one C# statement; semicolons terminate most statements.
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
// LEARN: executes one C# statement; semicolons terminate most statements.
            options.Retry.MaxRetryAttempts = 2;
// LEARN: executes one C# statement; semicolons terminate most statements.
            options.Retry.Delay = TimeSpan.FromMilliseconds(200);
// LEARN: executes one C# statement; semicolons terminate most statements.
            options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
// LEARN: executes one C# statement; semicolons terminate most statements.
        });

// LEARN: returns a value or exits the current method.
        return builder;
    }
}
