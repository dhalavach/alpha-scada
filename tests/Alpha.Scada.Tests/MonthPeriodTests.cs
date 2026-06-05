/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/MonthPeriodTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Tests;

public sealed class MonthPeriodTests
{
    [Theory]
    [InlineData("2026-06", "2026-06-01T00:00:00+00:00", "2026-07-01T00:00:00+00:00")]
    [InlineData("2026-12", "2026-12-01T00:00:00+00:00", "2027-01-01T00:00:00+00:00")]
    public void Month_period_parses_to_index_friendly_utc_range(string period, string expectedStart, string expectedEnd)
    {
        var range = MonthPeriod.Parse(period);

        Assert.Equal(DateTimeOffset.Parse(expectedStart), range.StartUtc);
        Assert.Equal(DateTimeOffset.Parse(expectedEnd), range.EndUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026")]
    [InlineData("2026-00")]
    [InlineData("2026-13")]
    [InlineData("2026-6")]
    [InlineData("June 2026")]
    public void Month_period_rejects_invalid_periods(string period)
    {
        Assert.Throws<ArgumentException>(() => MonthPeriod.Parse(period));
    }
}
