/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/Messaging/NatsOptions.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Microsoft.Extensions.Configuration;

namespace Alpha.Scada.ServiceDefaults.Messaging;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class NatsOptions
{
    public const string SectionName = "Nats";

    public string Url { get; init; } = string.Empty;

    public string? User { get; init; }

    public string? Password { get; init; }

    public static NatsOptions FromConfiguration(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<NatsOptions>() ?? new NatsOptions();
// LEARN: branches only when the boolean condition is true.
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            throw new InvalidOperationException($"{SectionName}:Url is required.");
        }

        return options;
    }
}
