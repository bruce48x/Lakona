using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Sessions;

internal static class ClusterClientNotificationProtocol
{
    public const int ServiceId = 0x554C4301;
    public const int DispatchMethodId = 30;

    public static readonly RpcMethod<ClientNotificationDispatchRequest, ClientNotificationDispatchReply> DispatchMethod =
        new(ServiceId, DispatchMethodId);
}

public sealed class ClientNotificationDispatchRequest
{
    public ClientNotificationCommand? Command { get; init; }
}

public sealed class ClientNotificationDispatchReply
{
    public int Status { get; init; }
}
