/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Domain/AlarmRule.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is domain code: it should express business concepts or rules with minimal infrastructure noise.
- Architecture connection: domain files should stay independent of ASP.NET, NATS, Wolverine, and SQL so business rules can be reasoned about directly.
- .NET/C# concepts to notice: File-scoped namespaces and small sealed classes/records are modern C# style choices that reduce boilerplate and make ownership explicit.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Alarm.Domain;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class AlarmRule
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static (bool IsAlarm, string Severity, string Message) Evaluate(ResolvedTelemetrySample sample)
    {
// LEARN: branches only when the boolean condition is true.
        if (sample.Quality != "good")
        {
// LEARN: returns a value or exits the current method.
            return (true, sample.Subsystem == "Safety" ? "critical" : "warning", $"{sample.TagName} quality is {sample.Quality}");
        }

// LEARN: branches only when the boolean condition is true.
        if (sample.AlarmLow is not null && sample.Value < sample.AlarmLow)
        {
// LEARN: returns a value or exits the current method.
            return (true, sample.Subsystem == "Safety" ? "critical" : "warning", $"{sample.TagName} below low threshold");
        }

// LEARN: branches only when the boolean condition is true.
        if (sample.AlarmHigh is not null && sample.Value > sample.AlarmHigh)
        {
// LEARN: returns a value or exits the current method.
            return (true, sample.Subsystem == "Safety" ? "critical" : "warning", $"{sample.TagName} above high threshold");
        }

// LEARN: returns a value or exits the current method.
        return (false, "", "");
    }
}
