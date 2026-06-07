using System.Net.Sockets;

internal static class StartupDiagnostics
{
    public static void ValidatePort(int port)
    {
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), $"Port {port} is out of valid range (1-65535).");
        }

        try
        {
            using var test = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            test.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"Port {port} is not available: {ex.Message}", ex);
        }
    }

    public static void ValidateDirectoryEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Directory endpoint must not be empty.", nameof(endpoint));
        }

        if (!endpoint.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Directory endpoint '{endpoint}' must start with tcp://", nameof(endpoint));
        }
    }
}
