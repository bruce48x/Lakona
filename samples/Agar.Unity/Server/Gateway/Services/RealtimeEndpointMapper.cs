using Agar.Sample.State.Contracts;
using Shared.Interfaces;

namespace Gateway.Services;

internal static class RealtimeEndpointMapper
{
    public static RealtimeConnectionInfo ToRealtimeConnectionInfo(
        GatewayEndpointDescriptor gateway,
        string roomId,
        string matchId,
        string sessionToken)
    {
        return new RealtimeConnectionInfo
        {
            Transport = ParseTransport(gateway.Transport),
            Host = gateway.Host,
            Port = gateway.Port,
            Path = gateway.Path,
            RoomId = roomId,
            MatchId = matchId,
            SessionToken = sessionToken
        };
    }

    public static RealtimeTransportKind ParseTransport(string? transport)
    {
        if (string.Equals(transport, "kcp", StringComparison.OrdinalIgnoreCase))
        {
            return RealtimeTransportKind.Kcp;
        }

        if (string.Equals(transport, "ws", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(transport, "websocket", StringComparison.OrdinalIgnoreCase))
        {
            return RealtimeTransportKind.WebSocket;
        }

        return RealtimeTransportKind.Unknown;
    }
}
