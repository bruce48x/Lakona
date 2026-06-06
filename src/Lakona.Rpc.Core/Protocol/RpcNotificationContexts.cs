using System;

namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Details for an exception thrown by a client-side notification handler.
    /// </summary>
    public sealed class RpcNotificationHandlerExceptionContext
    {
        public RpcNotificationHandlerExceptionContext(int serviceId, int methodId, Type payloadType, Exception exception)
        {
            ServiceId = serviceId;
            MethodId = methodId;
            PayloadType = payloadType ?? throw new ArgumentNullException(nameof(payloadType));
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }

        public int ServiceId { get; }

        public int MethodId { get; }

        public Type PayloadType { get; }

        public Exception Exception { get; }
    }

    /// <summary>
    ///     Details for a server-to-client notification frame that had no registered client-side handler.
    /// </summary>
    public sealed class RpcUnhandledNotificationContext
    {
        public RpcUnhandledNotificationContext(int serviceId, int methodId, int payloadLength)
        {
            ServiceId = serviceId;
            MethodId = methodId;
            PayloadLength = payloadLength;
        }

        public int ServiceId { get; }

        public int MethodId { get; }

        public int PayloadLength { get; }
    }
}
