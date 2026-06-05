/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.TagCatalog/Domain/TagDefinition.cs
- Module role: Alpha.Scada.TagCatalog is the tag-catalog service. It owns tag definitions, engineering units, thresholds, subsystem grouping, and report ontology/configuration rather than scattering those constants through code.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: domain files should stay independent of ASP.NET, NATS, Wolverine, and SQL so business rules can be reasoned about directly.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.TagCatalog.Domain;

public sealed record TagDefinition(
    Guid Id,
    Guid TenantId,
    Guid UnitId,
    string Key,
    string Name,
    string Subsystem,
    string EngineeringUnit,
    double? AlarmLow,
    double? AlarmHigh);
