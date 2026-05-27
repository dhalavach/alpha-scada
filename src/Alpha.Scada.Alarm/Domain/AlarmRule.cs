using Alpha.Scada.Contracts;

namespace Alpha.Scada.Alarm.Domain;

public static class AlarmRule
{
    public static (bool IsAlarm, string Severity, string Message) Evaluate(ResolvedTelemetrySample sample)
    {
        if (sample.Quality != "good")
        {
            return (true, sample.Subsystem == "Safety" ? "critical" : "warning", $"{sample.TagName} quality is {sample.Quality}");
        }

        if (sample.AlarmLow is not null && sample.Value < sample.AlarmLow)
        {
            return (true, sample.Subsystem == "Safety" ? "critical" : "warning", $"{sample.TagName} below low threshold");
        }

        if (sample.AlarmHigh is not null && sample.Value > sample.AlarmHigh)
        {
            return (true, sample.Subsystem == "Safety" ? "critical" : "warning", $"{sample.TagName} above high threshold");
        }

        return (false, "", "");
    }
}
