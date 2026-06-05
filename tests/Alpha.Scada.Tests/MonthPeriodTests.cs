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

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class MonthPeriodTests
{
// LEARN: marks this method as a parameterized xUnit test that receives InlineData rows.
    [Theory]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("2026-06", "2026-06-01T00:00:00+00:00", "2026-07-01T00:00:00+00:00")]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("2026-12", "2026-12-01T00:00:00+00:00", "2027-01-01T00:00:00+00:00")]
    public void Month_period_parses_to_index_friendly_utc_range(string period, string expectedStart, string expectedEnd)
    {
        var range = MonthPeriod.Parse(period);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(DateTimeOffset.Parse(expectedStart), range.StartUtc);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(DateTimeOffset.Parse(expectedEnd), range.EndUtc);
    }

// LEARN: marks this method as a parameterized xUnit test that receives InlineData rows.
    [Theory]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("")]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("2026")]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("2026-00")]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("2026-13")]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("2026-6")]
// LEARN: supplies one set of arguments to the parameterized test below.
    [InlineData("June 2026")]
    public void Month_period_rejects_invalid_periods(string period)
    {
        Assert.Throws<ArgumentException>(() => MonthPeriod.Parse(period));
    }
}
