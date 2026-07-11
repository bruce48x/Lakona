namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class HeartbeatRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        if (runtime.Heartbeat.Interval.Value <= TimeSpan.Zero)
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA090",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Heartbeat:Interval must be greater than zero.",
                "Set Lakona:Heartbeat:Interval to a TimeSpan such as 00:00:15.");
        }

        if (runtime.Heartbeat.Timeout.Value <= TimeSpan.Zero)
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA091",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Heartbeat:Timeout must be greater than zero.",
                "Set Lakona:Heartbeat:Timeout to a TimeSpan such as 00:00:45.");
        }

        if (runtime.Heartbeat.Interval.Value > TimeSpan.Zero
            && runtime.Heartbeat.Timeout.Value > TimeSpan.Zero
            && runtime.Heartbeat.Timeout.Value < runtime.Heartbeat.Interval.Value)
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA092",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Heartbeat:Timeout must not be shorter than Lakona:Heartbeat:Interval.",
                "Increase Lakona:Heartbeat:Timeout or reduce Lakona:Heartbeat:Interval.");
        }
    }
}
