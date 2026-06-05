/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/MonthPeriod.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
public readonly record struct MonthPeriod(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static MonthPeriod Parse(string period)
    {
// LEARN: branches only when the boolean condition is true.
        if (period.Length != 7 || period[4] != '-'
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            || !int.TryParse(period[..4], out var year)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            || !int.TryParse(period[5..], out var month)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            || month is < 1 or > 12)
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new ArgumentException("Period must use yyyy-MM format.", nameof(period));
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var start = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
// LEARN: returns a value or exits the current method.
        return new MonthPeriod(start, start.AddMonths(1));
    }
}
