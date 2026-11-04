using System.ComponentModel;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IRpcNotificationDispatchTarget
    {
        ValueTask DispatchNotificationAsync<TPayload>(
            int serviceId,
            int methodId,
            TPayload payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default);

        /// <summary>Sends a payload already encoded with the bound endpoint serializer.</summary>
        ValueTask DispatchEncodedNotificationAsync(
            int serviceId,
            int methodId,
            ReadOnlyMemory<byte> payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default);
    }
}
