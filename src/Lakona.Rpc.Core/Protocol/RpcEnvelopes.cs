namespace Lakona.Rpc.Core
{
    /// <summary>
    /// Identifies the kind of RPC envelope stored in a transport frame.
    /// </summary>
    public enum RpcFrameType : byte
    {
        /// <summary>
        /// A client-to-server RPC request.
        /// </summary>
        Request = 1,

        /// <summary>
        /// A server-to-client RPC response.
        /// </summary>
        Response = 2,

        /// <summary>
        /// A server-to-client push notification.
        /// </summary>
        Push = 3,

        /// <summary>
        /// A keepalive ping frame.
        /// </summary>
        KeepAlivePing = 4,

        /// <summary>
        /// A keepalive pong frame.
        /// </summary>
        KeepAlivePong = 5
    }

    /// <summary>
    /// Describes the framework-level outcome of an RPC response.
    /// </summary>
    public enum RpcStatus : byte
    {
        /// <summary>
        /// The request completed successfully and the payload contains the serialized return value.
        /// </summary>
        Ok = 0,

        /// <summary>
        /// The target service or method was not found.
        /// </summary>
        NotFound = 1,

        /// <summary>
        /// The server handler failed or returned an invalid framework response.
        /// </summary>
        HandlerError = 2,

        /// <summary>
        /// The server could not accept the request because it is overloaded.
        /// </summary>
        Overloaded = 3,

        /// <summary>
        /// The request reached the RPC layer but was invalid for the target RPC contract.
        /// </summary>
        BadRequest = 4,

        /// <summary>
        /// The peer violated the RPC wire protocol or connection state machine.
        /// </summary>
        ProtocolError = 5
    }

    /// <summary>
    /// Mutable request envelope used before encoding a client-to-server RPC request.
    /// </summary>
    public sealed class RpcRequestEnvelope
    {
        /// <summary>
        /// Client-assigned request identifier used to correlate the response.
        /// </summary>
        public uint RequestId { get; set; }

        /// <summary>
        /// Generated numeric identifier for the target service.
        /// </summary>
        public int ServiceId { get; set; }

        /// <summary>
        /// Generated numeric identifier for the target method.
        /// </summary>
        public int MethodId { get; set; }

        /// <summary>
        /// Serialized method argument payload.
        /// </summary>
        public ReadOnlyMemory<byte> Payload { get; set; } = ReadOnlyMemory<byte>.Empty;
    }

    /// <summary>
    /// Decoded request envelope with an owned payload frame slice.
    /// </summary>
    public sealed class RpcRequestFrame : IDisposable
    {
        /// <summary>
        /// Initializes a decoded request frame.
        /// </summary>
        /// <param name="requestId">Client-assigned request identifier.</param>
        /// <param name="serviceId">Generated numeric identifier for the target service.</param>
        /// <param name="methodId">Generated numeric identifier for the target method.</param>
        /// <param name="payload">Serialized method argument payload.</param>
        public RpcRequestFrame(uint requestId, int serviceId, int methodId, TransportFrame payload)
        {
            RequestId = requestId;
            ServiceId = serviceId;
            MethodId = methodId;
            Payload = payload;
        }

        /// <summary>
        /// Client-assigned request identifier used to correlate the response.
        /// </summary>
        public uint RequestId { get; }

        /// <summary>
        /// Generated numeric identifier for the target service.
        /// </summary>
        public int ServiceId { get; }

        /// <summary>
        /// Generated numeric identifier for the target method.
        /// </summary>
        public int MethodId { get; }

        /// <summary>
        /// Serialized method argument payload.
        /// </summary>
        public TransportFrame Payload { get; }

        /// <summary>
        /// Releases the underlying payload frame.
        /// </summary>
        public void Dispose() => Payload.Dispose();
    }

    /// <summary>
    /// Mutable response envelope used before encoding a server-to-client RPC response.
    /// </summary>
    public sealed class RpcResponseEnvelope
    {
        /// <summary>
        /// Identifier of the request being answered.
        /// </summary>
        public uint RequestId { get; set; }

        /// <summary>
        /// Response status.
        /// </summary>
        public RpcStatus Status { get; set; }

        /// <summary>
        /// Serialized return payload.
        /// </summary>
        public ReadOnlyMemory<byte> Payload { get; set; } = ReadOnlyMemory<byte>.Empty;

        /// <summary>
        /// Optional server error message associated with non-success responses.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Decoded response envelope with an owned payload frame slice.
    /// </summary>
    public sealed class RpcResponseFrame : IDisposable
    {
        /// <summary>
        /// Initializes a decoded response frame.
        /// </summary>
        /// <param name="requestId">Identifier of the request being answered.</param>
        /// <param name="status">Response status.</param>
        /// <param name="payload">Serialized return payload.</param>
        /// <param name="errorMessage">Optional server error message.</param>
        public RpcResponseFrame(uint requestId, RpcStatus status, TransportFrame payload, string? errorMessage)
        {
            RequestId = requestId;
            Status = status;
            Payload = payload;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Identifier of the request being answered.
        /// </summary>
        public uint RequestId { get; }

        /// <summary>
        /// Response status.
        /// </summary>
        public RpcStatus Status { get; }

        /// <summary>
        /// Serialized return payload.
        /// </summary>
        public TransportFrame Payload { get; }

        /// <summary>
        /// Optional server error message associated with non-success responses.
        /// </summary>
        public string? ErrorMessage { get; }

        /// <summary>
        /// Releases the underlying payload frame.
        /// </summary>
        public void Dispose() => Payload.Dispose();
    }

    /// <summary>
    /// Mutable push envelope used before encoding a server-to-client notification.
    /// </summary>
    public sealed class RpcPushEnvelope
    {
        /// <summary>
        /// Generated numeric identifier for the target client service.
        /// </summary>
        public int ServiceId { get; set; }

        /// <summary>
        /// Generated numeric identifier for the target client method.
        /// </summary>
        public int MethodId { get; set; }

        /// <summary>
        /// Serialized push payload.
        /// </summary>
        public ReadOnlyMemory<byte> Payload { get; set; } = ReadOnlyMemory<byte>.Empty;
    }

    /// <summary>
    /// Decoded push envelope with an owned payload frame slice.
    /// </summary>
    public sealed class RpcPushFrame : IDisposable
    {
        /// <summary>
        /// Initializes a decoded push frame.
        /// </summary>
        /// <param name="serviceId">Generated numeric identifier for the target client service.</param>
        /// <param name="methodId">Generated numeric identifier for the target client method.</param>
        /// <param name="payload">Serialized push payload.</param>
        public RpcPushFrame(int serviceId, int methodId, TransportFrame payload)
        {
            ServiceId = serviceId;
            MethodId = methodId;
            Payload = payload;
        }

        /// <summary>
        /// Generated numeric identifier for the target client service.
        /// </summary>
        public int ServiceId { get; }

        /// <summary>
        /// Generated numeric identifier for the target client method.
        /// </summary>
        public int MethodId { get; }

        /// <summary>
        /// Serialized push payload.
        /// </summary>
        public TransportFrame Payload { get; }

        /// <summary>
        /// Releases the underlying payload frame.
        /// </summary>
        public void Dispose() => Payload.Dispose();
    }

    /// <summary>
    /// Keepalive ping envelope sent to measure liveness and optional round-trip time.
    /// </summary>
    public sealed class RpcKeepAlivePingEnvelope
    {
        /// <summary>
        /// UTC timestamp ticks captured by the sender.
        /// </summary>
        public long TimestampTicksUtc { get; set; }
    }

    /// <summary>
    /// Keepalive pong envelope sent in response to a keepalive ping.
    /// </summary>
    public sealed class RpcKeepAlivePongEnvelope
    {
        /// <summary>
        /// UTC timestamp ticks copied from the matching ping.
        /// </summary>
        public long TimestampTicksUtc { get; set; }
    }

    /// <summary>
    /// Singleton marker value used for RPC methods with no return payload.
    /// </summary>
    public sealed class RpcVoid
    {
        /// <summary>
        /// Shared void marker instance.
        /// </summary>
        public static readonly RpcVoid Instance = new();
    }
}
