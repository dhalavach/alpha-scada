/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/MonthPeriod.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.ServiceDefaults;

public readonly record struct MonthPeriod(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public static MonthPeriod Parse(string period)
    {
// LEARN: branches only when the boolean condition is true.
        if (period.Length != 7 || period[4] != '-'
            || !int.TryParse(period[..4], out var year)
            || !int.TryParse(period[5..], out var month)
            || month is < 1 or > 12)
        {
            throw new ArgumentException("Period must use yyyy-MM format.", nameof(period));
        }

        var start = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        return new MonthPeriod(start, start.AddMonths(1));
    }
}
