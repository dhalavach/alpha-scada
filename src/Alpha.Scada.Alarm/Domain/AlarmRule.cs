/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Domain/AlarmRule.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is domain code: it should express business concepts or rules with minimal infrastructure noise.
- Architecture connection: domain files should stay independent of ASP.NET, NATS, Wolverine, and SQL so business rules can be reasoned about directly.
- .NET/C# concepts to notice: File-scoped namespaces and small sealed classes/records are modern C# style choices that reduce boilerplate and make ownership explicit.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

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
