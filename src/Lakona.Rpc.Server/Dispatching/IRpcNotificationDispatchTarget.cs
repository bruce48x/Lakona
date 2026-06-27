using System.ComponentModel;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IRpcNotificationDispatchTarget
    {
        ValueTask DispatchNotificationAsync(
            string methodName,
            object?[] arguments,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default);
    }
}
