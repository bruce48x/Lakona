using System.ComponentModel;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

/// <summary>
///     Generated-support channel for notifications bound to one RPC connection.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RpcNotificationChannel
{
    private readonly RpcSession _session;

    internal RpcNotificationChannel(RpcSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public ValueTask SendAsync<TPayload>(
        int serviceId,
        int methodId,
        TPayload payload,
        RpcPushMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        return _session.SendNotificationAsync(
            serviceId,
            methodId,
            payload,
            metadata,
            cancellationToken);
    }

    public ValueTask SendRawAsync(
        int serviceId,
        int methodId,
        ReadOnlyMemory<byte> payload,
        RpcPushMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        return _session.SendRawNotificationAsync(
            serviceId,
            methodId,
            payload,
            metadata,
            cancellationToken);
    }
}
