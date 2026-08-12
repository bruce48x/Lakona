using System.Net;

namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class ManagementAdminRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        var management = runtime.Management;
        if (management.AdminEnabled.Value
            && management.AdminRequireLoopback.Value
            && !IsLoopbackHost(management.HttpHost.Value))
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA130",
                LakonaGameDiagnosticSeverity.Error,
                "Lakona:Management:Http:Host binds admin routes to a non-loopback host while Lakona:Management:Admin:RequireLoopback is true.",
                "Set Lakona:Management:Http:Host to 127.0.0.1, localhost, or ::1, or disable Lakona:Management:Admin:RequireLoopback only in a trusted network.");
        }
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address)
            && IPAddress.IsLoopback(address);
    }
}
