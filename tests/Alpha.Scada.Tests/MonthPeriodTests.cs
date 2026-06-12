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

    [Theory]
    [InlineData("2026-01", true)]
    [InlineData("2026-12", true)]
    [InlineData("2026-00", false)]
    [InlineData("2026-13", false)]
    [InlineData("2026-1", false)]
    [InlineData("", false)]
    public void Month_period_try_parse_reports_validity_without_throwing(string period, bool expected)
    {
        var parsed = MonthPeriod.TryParse(period, out var result);

        Assert.Equal(expected, parsed);
        if (expected)
        {
            Assert.Equal(period, result.StartUtc.ToString("yyyy-MM"));
        }
    }

    [Fact]
    public void Month_period_try_parse_rejects_null()
    {
        Assert.False(MonthPeriod.TryParse(null, out _));
    }
}
