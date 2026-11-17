using Microsoft.Extensions.Logging;

namespace Lakona.Rpc
{
    internal static class RpcPayloadLogging
    {
        internal static void Write<T>(ILogger logger, string direction, uint requestId,
            int serviceId, int methodId, T payload, string? connectionId = null)
        {
            try
            {
                if (!logger.IsEnabled(LogLevel.Trace)) return;
                logger.LogTrace("RPC {Direction} payload {RequestId} service {ServiceId} method {MethodId} connection {ConnectionId}: {Payload}",
                    direction, requestId, serviceId, methodId, connectionId, payload);
            }
            catch
            {
                // Optional diagnostics must not change invocation or response delivery.
            }
        }
    }
}
