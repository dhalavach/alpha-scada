/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Domain/TelemetryModels.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: domain files should stay independent of ASP.NET, NATS, Wolverine, and SQL so business rules can be reasoned about directly.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Telemetry.Domain;

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record TelemetrySample(Guid TenantId, Guid UnitId, Guid TagId, DateTimeOffset TimestampUtc, double Value, string Quality);
