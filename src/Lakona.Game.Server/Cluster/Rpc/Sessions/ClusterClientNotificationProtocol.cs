using System.Collections.Generic;
using Lakona.Rpc.Core;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc;

internal static class ClusterClientNotificationProtocol
{
    public const int ServiceId = ClusterProtocol.ServiceId;
    public const int DispatchMethodId = 30;
    public const int BatchDispatchMethodId = 31;

    public static readonly RpcMethod<ClientNotificationDispatchRequest, ClientNotificationDispatchReply> DispatchMethod =
        new(ServiceId, DispatchMethodId);

    public static readonly RpcMethod<ClientNotificationBatchDispatchRequest, ClientNotificationBatchDispatchReply>
        BatchDispatchMethod = new(ServiceId, BatchDispatchMethodId);
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ClientNotificationDispatchRequest
{
    [MemoryPackOrder(0)]
    public ClientNotificationCommand? Command { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ClientNotificationDispatchReply
{
    [MemoryPackOrder(0)]
    public int Status { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ClientNotificationBatchDispatchRequest
{
    [MemoryPackOrder(0)]
    public IReadOnlyList<ClientNotificationCommand> Commands { get; set; } = [];
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ClientNotificationBatchDispatchReply
{
    [MemoryPackOrder(0)]
    public int[] Statuses { get; set; } = [];
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ClientNotificationCommand
{
    [MemoryPackOrder(0)]
    public string OwnerKey { get; set; } = "";

    [MemoryPackOrder(1)]
    public string SessionId { get; set; } = "";

    [MemoryPackOrder(2)]
    public string CallbackContractType { get; set; } = "";

    [MemoryPackOrder(3)]
    public string MethodName { get; set; } = "";

    [MemoryPackOrder(4)]
    public int ServiceId { get; set; }

    [MemoryPackOrder(5)]
    public int MethodId { get; set; }

    [MemoryPackOrder(6)]
    public byte[] Payload { get; set; } = [];

    [MemoryPackOrder(7)]
    public ClientNotificationMetadata? Metadata { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ClientNotificationMetadata
{
    [MemoryPackOrder(0)]
    public string Type { get; set; } = "";

    [MemoryPackOrder(1)]
    public byte[] Payload { get; set; } = [];

    public RpcPushMetadata ToRpcPushMetadata() => new()
    {
        Type = Type,
        Payload = Payload
    };
}
