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
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This notification target does not support generated typed dispatch.");
        }

        ValueTask DispatchNotificationAsync(
            int serviceId,
            int methodId,
            ReadOnlyMemory<byte> payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This notification target does not support generated command dispatch.");
        }

        ValueTask DispatchNotificationAsync(
            string methodName,
            object?[] arguments,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default);
    }
}
