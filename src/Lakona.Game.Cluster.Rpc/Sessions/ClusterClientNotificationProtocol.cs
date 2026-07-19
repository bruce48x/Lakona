using System.Collections.Generic;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc;

public static class ClusterClientNotificationProtocol
{
    public const int ServiceId = ClusterProtocol.ServiceId;
    public const int DispatchMethodId = 30;

    public static readonly RpcMethod<ClientNotificationDispatchRequest, ClientNotificationDispatchReply> DispatchMethod =
        new(ServiceId, DispatchMethodId);
}

public sealed class ClientNotificationDispatchRequest
{
    public ClientNotificationCommand? Command { get; set; }
}

public sealed class ClientNotificationDispatchReply
{
    public int Status { get; set; }
}

public sealed class ClientNotificationCommand
{
    public string OwnerKey { get; set; } = "";

    public string SessionId { get; set; } = "";

    public string CallbackContractType { get; set; } = "";

    public string MethodName { get; set; } = "";

    public int ServiceId { get; set; }

    public int MethodId { get; set; }

    public byte[] Payload { get; set; } = [];

    public IReadOnlyList<ClientNotificationArgument> Arguments { get; set; } = [];

    public RpcPushMetadata? Metadata { get; set; }
}

public sealed class ClientNotificationArgument
{
    public string TypeName { get; set; } = "";

    public byte[] Payload { get; set; } = [];
}
