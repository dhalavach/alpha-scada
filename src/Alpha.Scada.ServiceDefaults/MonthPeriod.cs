namespace Alpha.Scada.ServiceDefaults;

public readonly record struct MonthPeriod(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public static MonthPeriod Parse(string period)
    {
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
