using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Guardrails.Rules;

internal static class HeartbeatRule
{
    internal static IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameRuntimeOptions runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        if (runtime.Heartbeat.Interval <= TimeSpan.Zero)
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA10090",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Heartbeat:Interval must be greater than zero.",
                "Set Lakona:Heartbeat:Interval to a TimeSpan such as 00:00:15.");
        }

        if (runtime.Heartbeat.Timeout <= TimeSpan.Zero)
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA10091",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Heartbeat:Timeout must be greater than zero.",
                "Set Lakona:Heartbeat:Timeout to a TimeSpan such as 00:00:45.");
        }

        if (runtime.Heartbeat.Interval > TimeSpan.Zero
            && runtime.Heartbeat.Timeout > TimeSpan.Zero
            && runtime.Heartbeat.Timeout < runtime.Heartbeat.Interval)
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA10092",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Heartbeat:Timeout must not be shorter than Lakona:Heartbeat:Interval.",
                "Increase Lakona:Heartbeat:Timeout or reduce Lakona:Heartbeat:Interval.");
        }
    }
}
