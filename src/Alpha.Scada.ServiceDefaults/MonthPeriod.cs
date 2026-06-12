namespace Alpha.Scada.ServiceDefaults;

public readonly record struct MonthPeriod(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public static MonthPeriod Parse(string period)
    {
        if (!TryParse(period, out var parsed))
        {
            throw new ArgumentException("Period must use yyyy-MM format.", nameof(period));
        }

        return parsed;
    }

    public static bool TryParse(string? period, out MonthPeriod parsed)
    {
        parsed = default;
        if (period is null
            || period.Length != 7
            || period[4] != '-'
            || !int.TryParse(period.AsSpan(0, 4), out var year)
            || !int.TryParse(period.AsSpan(5, 2), out var month)
            || month is < 1 or > 12)
        {
            return false;
        }

        var start = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        parsed = new MonthPeriod(start, start.AddMonths(1));
        return true;
    }
}
