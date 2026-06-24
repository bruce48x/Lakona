using Lakona.Rpc.Core;
using MemoryPack;

namespace Lakona.Game.Server.Sessions;

internal static class ClusterClientNotificationProtocol
{
    public const int ServiceId = 0x554C4301;
    public const int DispatchMethodId = 30;

    public static readonly RpcMethod<ClientNotificationDispatchRequest, ClientNotificationDispatchReply> DispatchMethod =
        new(ServiceId, DispatchMethodId);
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ClientNotificationDispatchRequest
{
    [MemoryPackOrder(0)]
    public ClientNotificationCommand? Command { get; init; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ClientNotificationDispatchReply
{
    [MemoryPackOrder(0)]
    public int Status { get; init; }
}
