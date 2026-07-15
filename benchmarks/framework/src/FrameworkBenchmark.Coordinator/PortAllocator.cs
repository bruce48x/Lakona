using System.Net;
using System.Net.Sockets;

namespace FrameworkBenchmark.Coordinator;

public static class PortAllocator
{
    public static IReadOnlyDictionary<string, string> Allocate(IEnumerable<string> names)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var allocated = new HashSet<int>();
        foreach (var name in names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            int port;
            do
            {
                port = GetFreePort();
            }
            while (!allocated.Add(port));

            result.Add(name, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return result;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
