using System.Net;

namespace Lakona.Game.Server.HotfixAdmin;

public sealed class HotfixAdminOptions
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 20090;

    public string HotfixRoot { get; set; } = "hotfix";

    public string BuildTag { get; set; } = "";

    public string Mode { get; set; } = "production";

    public void Validate()
    {
        if (!IsLoopbackHost(Host))
        {
            throw new InvalidOperationException("Hotfix admin endpoint must bind to a loopback host.");
        }
    }

    internal static bool IsLoopbackHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
