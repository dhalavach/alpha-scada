/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/Messaging/NatsOptions.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults.Messaging;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class NatsOptions
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string SectionName = "Nats";

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
    public string Url { get; init; } = string.Empty;

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
    public string? User { get; init; }

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
    public string? Password { get; init; }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static NatsOptions FromConfiguration(IConfiguration configuration)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var options = configuration.GetSection(SectionName).Get<NatsOptions>() ?? new NatsOptions();
// LEARN: branches only when the boolean condition is true.
        if (string.IsNullOrWhiteSpace(options.Url))
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new InvalidOperationException($"{SectionName}:Url is required.");
        }

// LEARN: returns a value or exits the current method.
        return options;
    }
}
