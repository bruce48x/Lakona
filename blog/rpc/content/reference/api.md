---
title: API Reference
date: 2026-05-11T00:00:00+08:00
---

This page is generated from C# XML documentation comments. Update the source comments first, then rerun `scripts/generate-api-reference.ps1`.

## Lakona.Rpc.Core

### Field `Lakona.Rpc.Core.RpcEnvelopeCodec.MaxPayloadSize`

Maximum payload length accepted by envelope decoders.

### Field `Lakona.Rpc.Core.RpcFrameType.KeepAlivePing`

A keepalive ping frame.

### Field `Lakona.Rpc.Core.RpcFrameType.KeepAlivePong`

A keepalive pong frame.

### Field `Lakona.Rpc.Core.RpcFrameType.Push`

A server-to-client push notification.

### Field `Lakona.Rpc.Core.RpcFrameType.Request`

A client-to-server RPC request.

### Field `Lakona.Rpc.Core.RpcFrameType.Response`

A server-to-client RPC response.

### Field `Lakona.Rpc.Core.RpcStatus.BadRequest`

The request reached the RPC layer but was invalid for the target RPC contract.

### Field `Lakona.Rpc.Core.RpcStatus.HandlerError`

The server handler failed or returned an invalid framework response.

### Field `Lakona.Rpc.Core.RpcStatus.NotFound`

The target service or method was not found.

### Field `Lakona.Rpc.Core.RpcStatus.Ok`

The request completed successfully and the payload contains the serialized return value.

### Field `Lakona.Rpc.Core.RpcStatus.Overloaded`

The server could not accept the request because it is overloaded.

### Field `Lakona.Rpc.Core.RpcStatus.ProtocolError`

The peer violated the RPC wire protocol or connection state machine.

### Field `Lakona.Rpc.Core.RpcVoid.Instance`

Shared void marker instance.

### Method `Lakona.Rpc.Core.IRpcClient.CallAsync(Lakona.Rpc.Core.RpcMethod<T0,T1>,T0,System.Threading.CancellationToken)`

Sends one RPC request and waits for the matching response.

Parameters:
- `method`: Generated method descriptor containing the service id and method id.
- `arg`: Request DTO instance, or `null` for void-style requests.
- `ct`: Cancellation token for the outbound send and response wait.

Type parameters:
- `TArg`: Request DTO type.
- `TResult`: Response DTO type.

Returns: The deserialized response DTO.

Exceptions:
- `Lakona.Rpc.Core.RpcException`: Thrown by the default runtime when the remote response status is not `Lakona.Rpc.Core.RpcStatus.Ok`.

### Method `Lakona.Rpc.Core.IRpcClient.RegisterNotificationHandler(Lakona.Rpc.Core.RpcNotificationMethod<T0>,System.Func<T0,System.Threading.Tasks.ValueTask>)`

Registers the handler for a server-to-client notification method.

Remarks: The default runtime invokes handlers from its internal notification-processing loop. It does not marshal notifications to the Unity main thread.

Parameters:
- `method`: Generated notification descriptor containing the service id and notification method id.
- `handler`: Handler invoked with the deserialized notification DTO.

Type parameters:
- `TArg`: Notification DTO type.

### Method `Lakona.Rpc.Core.IRpcConnectionAcceptor.AcceptAsync(System.Threading.CancellationToken)`

Waits for the next accepted connection.

Parameters:
- `ct`: Cancellation token for the accept operation.

Returns: The accepted transport plus display and remote endpoint metadata.

### Method `Lakona.Rpc.Core.IRpcSerializer.Deserialize(System.ReadOnlyMemory<System.Byte>)`

Deserializes a DTO value from payload bytes.

Parameters:
- `data`: Payload bytes.

Type parameters:
- `T`: DTO type.

Returns: The deserialized DTO value.

### Method `Lakona.Rpc.Core.IRpcSerializer.Deserialize(System.ReadOnlySpan<System.Byte>)`

Deserializes a DTO value from payload bytes.

Parameters:
- `data`: Payload bytes.

Type parameters:
- `T`: DTO type.

Returns: The deserialized DTO value.

### Method `Lakona.Rpc.Core.IRpcSerializer.SerializeFrame(T0)`

Serializes a DTO value into an owned transport frame.

Parameters:
- `value`: DTO instance to serialize.

Type parameters:
- `T`: DTO type.

Returns: An owned frame containing the serialized payload. The caller disposes it.

### Method `Lakona.Rpc.Core.ITransport.ConnectAsync(System.Threading.CancellationToken)`

Prepares this transport for frame I/O.

Remarks: Client transports use this call to actively connect to their remote endpoint. Accepted server transports use it to initialize per-connection state such as streams, schedulers, or framing over an already accepted connection. In-memory or already-open transports may implement it as an idempotent no-op.

### Method `Lakona.Rpc.Core.ITransport.ReceiveFrameAsync(System.Threading.CancellationToken)`

Receives one complete frame.

Parameters:
- `ct`: Cancellation token for the receive operation.

Returns: An owned frame. The caller disposes it after processing. An empty frame means the remote side closed the connection.

### Method `Lakona.Rpc.Core.ITransport.SendFrameAsync(System.ReadOnlyMemory<System.Byte>,System.Threading.CancellationToken)`

Sends one complete frame.

Parameters:
- `frame`: Frame bytes to send. The transport must not retain this memory after the call completes.
- `ct`: Cancellation token for the send operation.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.DecodeKeepAlivePing(System.ReadOnlySpan<System.Byte>)`

Decodes a keepalive ping envelope.

Parameters:
- `data`: Encoded keepalive ping bytes.

Returns: The decoded keepalive ping envelope.

Exceptions:
- `System.InvalidOperationException`: Thrown when the frame type or envelope length is invalid.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.DecodeKeepAlivePong(System.ReadOnlySpan<System.Byte>)`

Decodes a keepalive pong envelope.

Parameters:
- `data`: Encoded keepalive pong bytes.

Returns: The decoded keepalive pong envelope.

Exceptions:
- `System.InvalidOperationException`: Thrown when the frame type or envelope length is invalid.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.DecodePush(Lakona.Rpc.Core.TransportFrame)`

Decodes a server-to-client push envelope from a transport frame.

Parameters:
- `data`: Encoded push frame.

Returns: A decoded push frame whose payload slice references `data`.

Exceptions:
- `System.InvalidOperationException`: Thrown when the frame type or envelope length is invalid.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.DecodeRequest(Lakona.Rpc.Core.TransportFrame)`

Decodes a request envelope from a transport frame.

Parameters:
- `data`: Encoded request frame.

Returns: A decoded request frame whose payload slice references `data`.

Exceptions:
- `System.InvalidOperationException`: Thrown when the frame type or envelope length is invalid.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.DecodeResponse(Lakona.Rpc.Core.TransportFrame)`

Decodes a response envelope from a transport frame.

Parameters:
- `data`: Encoded response frame.

Returns: A decoded response frame whose payload slice references `data`.

Exceptions:
- `System.InvalidOperationException`: Thrown when the frame type or envelope length is invalid.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.EncodeKeepAlivePing(Lakona.Rpc.Core.RpcKeepAlivePingEnvelope)`

Encodes a keepalive ping envelope into a transport frame.

Parameters:
- `ping`: Ping timestamp data.

Returns: An owned transport frame containing the encoded keepalive ping.

Exceptions:
- `System.ArgumentNullException`: Thrown when `ping` is `null`.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.EncodeKeepAlivePong(Lakona.Rpc.Core.RpcKeepAlivePongEnvelope)`

Encodes a keepalive pong envelope into a transport frame.

Parameters:
- `pong`: Pong timestamp data.

Returns: An owned transport frame containing the encoded keepalive pong.

Exceptions:
- `System.ArgumentNullException`: Thrown when `pong` is `null`.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.EncodePush(Lakona.Rpc.Core.RpcPushEnvelope)`

Encodes a server-to-client push envelope into a transport frame.

Parameters:
- `push`: Push metadata and serialized method payload.

Returns: An owned transport frame containing the encoded push envelope.

Exceptions:
- `System.ArgumentNullException`: Thrown when `push` is `null`.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.EncodeRequest(Lakona.Rpc.Core.RpcRequestEnvelope)`

Encodes a request envelope into a transport frame.

Parameters:
- `req`: Request metadata and serialized method payload.

Returns: An owned transport frame containing the encoded request envelope.

Exceptions:
- `System.ArgumentNullException`: Thrown when `req` is `null`.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.EncodeResponse(System.UInt32,Lakona.Rpc.Core.RpcStatus,System.ReadOnlyMemory<System.Byte>,System.String)`

Encodes response fields into a transport frame.

Parameters:
- `requestId`: Identifier of the request being answered.
- `status`: Response status.
- `payload`: Serialized return payload or empty bytes for non-success responses.
- `errorMessage`: Optional UTF-8 error text included with the response.

Returns: An owned transport frame containing the encoded response envelope.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.EncodeResponse(Lakona.Rpc.Core.RpcResponseEnvelope)`

Encodes a response envelope into a transport frame.

Parameters:
- `resp`: Response metadata, status, serialized payload, and optional error message.

Returns: An owned transport frame containing the encoded response envelope.

Exceptions:
- `System.ArgumentNullException`: Thrown when `resp` is `null`.

### Method `Lakona.Rpc.Core.RpcEnvelopeCodec.PeekFrameType(System.ReadOnlySpan<System.Byte>)`

Reads the frame type byte from an encoded RPC envelope without decoding the full frame.

Parameters:
- `data`: Encoded envelope bytes.

Returns: The frame type stored in the first byte.

Exceptions:
- `System.InvalidOperationException`: Thrown when `data` is empty.

### Method `Lakona.Rpc.Core.RpcMethod.constructor(System.Int32,System.Int32)`

Creates a method descriptor from stable protocol ids.

Parameters:
- `serviceId`: Stable service id declared by `Lakona.Rpc.Core.RpcServiceAttribute`.
- `methodId`: Stable method id declared by `Lakona.Rpc.Core.RpcMethodAttribute`.

### Method `Lakona.Rpc.Core.RpcPushFrame.constructor(System.Int32,System.Int32,Lakona.Rpc.Core.TransportFrame)`

Initializes a decoded push frame.

Parameters:
- `serviceId`: Generated numeric identifier for the target client service.
- `methodId`: Generated numeric identifier for the target client method.
- `payload`: Serialized push payload.

### Method `Lakona.Rpc.Core.RpcPushFrame.Dispose`

Releases the underlying payload frame.

### Method `Lakona.Rpc.Core.RpcNotificationMethod.constructor(System.Int32,System.Int32)`

Creates a notification descriptor from stable protocol ids.

Parameters:
- `serviceId`: Stable service id declared by `Lakona.Rpc.Core.RpcServiceAttribute`.
- `methodId`: Stable notification method id declared by `Lakona.Rpc.Core.RpcNotificationAttribute`.

### Method `Lakona.Rpc.Core.RpcRequestFrame.constructor(System.UInt32,System.Int32,System.Int32,Lakona.Rpc.Core.TransportFrame)`

Initializes a decoded request frame.

Parameters:
- `requestId`: Client-assigned request identifier.
- `serviceId`: Generated numeric identifier for the target service.
- `methodId`: Generated numeric identifier for the target method.
- `payload`: Serialized method argument payload.

### Method `Lakona.Rpc.Core.RpcRequestFrame.Dispose`

Releases the underlying payload frame.

### Method `Lakona.Rpc.Core.RpcResponseFrame.constructor(System.UInt32,Lakona.Rpc.Core.RpcStatus,Lakona.Rpc.Core.TransportFrame,System.String)`

Initializes a decoded response frame.

Parameters:
- `requestId`: Identifier of the request being answered.
- `status`: Response status.
- `payload`: Serialized return payload.
- `errorMessage`: Optional server error message.

### Method `Lakona.Rpc.Core.RpcResponseFrame.Dispose`

Releases the underlying payload frame.

### Method `Lakona.Rpc.Core.TransportSecurityConfig.ResolveKey`

Resolves the configured encryption key.

Returns: `Lakona.Rpc.Core.TransportSecurityConfig.EncryptionKey` when present, otherwise decoded `Lakona.Rpc.Core.TransportSecurityConfig.EncryptionKeyBase64`, otherwise `null`.

Exceptions:
- `System.FormatException`: Thrown when `Lakona.Rpc.Core.TransportSecurityConfig.EncryptionKeyBase64` is not valid base64.

### Property `Lakona.Rpc.Core.IRpcConnectionAcceptor.ListenAddress`

Human-readable listen address for logs and diagnostics.

### Property `Lakona.Rpc.Core.RpcKeepAliveOptions.Disabled`

Shared disabled keepalive configuration.

### Property `Lakona.Rpc.Core.RpcKeepAliveOptions.Enabled`

Enables keepalive probing.

### Property `Lakona.Rpc.Core.RpcKeepAliveOptions.Interval`

Maximum time without receiving any frame before a keepalive ping is sent.

### Property `Lakona.Rpc.Core.RpcKeepAliveOptions.MeasureRtt`

Measures round-trip time from keepalive ping/pong timestamps when enabled.

### Property `Lakona.Rpc.Core.RpcKeepAliveOptions.Timeout`

Maximum time to wait for an inbound frame after a ping before disconnecting the session.

### Property `Lakona.Rpc.Core.RpcKeepAlivePingEnvelope.TimestampTicksUtc`

UTC timestamp ticks captured by the sender.

### Property `Lakona.Rpc.Core.RpcKeepAlivePongEnvelope.TimestampTicksUtc`

UTC timestamp ticks copied from the matching ping.

### Property `Lakona.Rpc.Core.RpcMethod.MethodId`

Stable method id used on the wire.

### Property `Lakona.Rpc.Core.RpcMethod.ServiceId`

Stable service id used on the wire.

### Property `Lakona.Rpc.Core.RpcPushEnvelope.MethodId`

Generated numeric identifier for the target client method.

### Property `Lakona.Rpc.Core.RpcPushEnvelope.Payload`

Serialized push payload.

### Property `Lakona.Rpc.Core.RpcPushEnvelope.ServiceId`

Generated numeric identifier for the target client service.

### Property `Lakona.Rpc.Core.RpcPushFrame.MethodId`

Generated numeric identifier for the target client method.

### Property `Lakona.Rpc.Core.RpcPushFrame.Payload`

Serialized push payload.

### Property `Lakona.Rpc.Core.RpcPushFrame.ServiceId`

Generated numeric identifier for the target client service.

### Property `Lakona.Rpc.Core.RpcNotificationMethod.MethodId`

Stable notification method id used on the wire.

### Property `Lakona.Rpc.Core.RpcNotificationMethod.ServiceId`

Stable service id used on the wire.

### Property `Lakona.Rpc.Core.RpcRequestEnvelope.MethodId`

Generated numeric identifier for the target method.

### Property `Lakona.Rpc.Core.RpcRequestEnvelope.Payload`

Serialized method argument payload.

### Property `Lakona.Rpc.Core.RpcRequestEnvelope.RequestId`

Client-assigned request identifier used to correlate the response.

### Property `Lakona.Rpc.Core.RpcRequestEnvelope.ServiceId`

Generated numeric identifier for the target service.

### Property `Lakona.Rpc.Core.RpcRequestFrame.MethodId`

Generated numeric identifier for the target method.

### Property `Lakona.Rpc.Core.RpcRequestFrame.Payload`

Serialized method argument payload.

### Property `Lakona.Rpc.Core.RpcRequestFrame.RequestId`

Client-assigned request identifier used to correlate the response.

### Property `Lakona.Rpc.Core.RpcRequestFrame.ServiceId`

Generated numeric identifier for the target service.

### Property `Lakona.Rpc.Core.RpcResponseEnvelope.ErrorMessage`

Optional server error message associated with non-success responses.

### Property `Lakona.Rpc.Core.RpcResponseEnvelope.Payload`

Serialized return payload.

### Property `Lakona.Rpc.Core.RpcResponseEnvelope.RequestId`

Identifier of the request being answered.

### Property `Lakona.Rpc.Core.RpcResponseEnvelope.Status`

Response status.

### Property `Lakona.Rpc.Core.RpcResponseFrame.ErrorMessage`

Optional server error message associated with non-success responses.

### Property `Lakona.Rpc.Core.RpcResponseFrame.Payload`

Serialized return payload.

### Property `Lakona.Rpc.Core.RpcResponseFrame.RequestId`

Identifier of the request being answered.

### Property `Lakona.Rpc.Core.RpcResponseFrame.Status`

Response status.

### Property `Lakona.Rpc.Core.TransportSecurityConfig.CompressionThresholdBytes`

Minimum frame size before compression is attempted.

### Property `Lakona.Rpc.Core.TransportSecurityConfig.EnableCompression`

Compresses frames before encryption and transmission when enabled.

### Property `Lakona.Rpc.Core.TransportSecurityConfig.EnableEncryption`

Encrypts transformed frames when enabled.

### Property `Lakona.Rpc.Core.TransportSecurityConfig.EncryptionKey`

Raw symmetric encryption key bytes.

### Property `Lakona.Rpc.Core.TransportSecurityConfig.EncryptionKeyBase64`

Base64-encoded symmetric encryption key. Used when `Lakona.Rpc.Core.TransportSecurityConfig.EncryptionKey` is not set.

### Property `Lakona.Rpc.Core.TransportSecurityConfig.IsEnabled`

Indicates whether any frame transformation is enabled.

### Property `Lakona.Rpc.Core.TransportSecurityConfig.MaxDecompressedFrameBytes`

Maximum allowed decompressed frame size.

### Type `Lakona.Rpc.Core.IRemoteEndPointProvider`

Optional interface for transports that can report the remote endpoint of the connected peer. Implement on server-side transports where the remote address is known (TCP, UDP/KCP, WebSocket with HTTP context, etc.).

### Type `Lakona.Rpc.Core.IRpcClient`

Runtime-facing RPC client abstraction used by generated client proxies.

Remarks: Application code normally uses the generated client facade instead of this interface directly. Implementations are responsible for request correlation, response decoding, and server push dispatch.

### Type `Lakona.Rpc.Core.IRpcConnectionAcceptor`

Server-side transport acceptor that yields accepted RPC connections.

Remarks: Concrete transports such as TCP, WebSocket, and KCP implement this interface. The server host owns the accept loop and creates one `RpcSession` per accepted connection.

### Type `Lakona.Rpc.Core.IRpcSerializer`

Serializer for RPC method payloads (arguments and return values). Envelope encoding is handled by `Lakona.Rpc.Core.RpcEnvelopeCodec`.

### Type `Lakona.Rpc.Core.ITransport`

Transport boundary for RPC: sends and receives complete frames (one message). TCP/WS/KCP differences are hidden below this interface.

### Property `Lakona.Rpc.Core.ITransport.IsConnected`

Best-known local connection state for diagnostics. This value is not a synchronization primitive and does not guarantee that the next send or receive will succeed.

### Type `Lakona.Rpc.Core.LengthPrefix`

Network framing: uint32 length prefix (big-endian) + payload bytes. Matches Unity client's LengthPrefix for wire compatibility.

### Type `Lakona.Rpc.Core.RpcNotificationContractAttribute`

Marks an interface as the server-to-client notification contract for a specific RPC service.

### Type `Lakona.Rpc.Core.RpcEnvelopeCodec`

Encodes and decodes Lakona.Rpc wire envelopes.

Remarks: The codec serializes only the transport envelope fields. RPC method payloads are opaque bytes produced by an `Lakona.Rpc.Core.IRpcSerializer`.

### Type `Lakona.Rpc.Core.RpcFrameType`

Identifies the kind of RPC envelope stored in a transport frame.

### Type `Lakona.Rpc.Core.RpcKeepAliveOptions`

Peer liveness probing for RPC transports. Only inbound traffic proves the remote peer is alive; outbound sends do not suppress probes.

### Type `Lakona.Rpc.Core.RpcKeepAlivePingEnvelope`

Keepalive ping envelope sent to measure liveness and optional round-trip time.

### Type `Lakona.Rpc.Core.RpcKeepAlivePongEnvelope`

Keepalive pong envelope sent in response to a keepalive ping.

### Type `Lakona.Rpc.Core.RpcMethod`

Typed descriptor for a client-to-server RPC method.

Type parameters:
- `TArg`: Request DTO type.
- `TResult`: Response DTO type.

### Type `Lakona.Rpc.Core.RpcMethodAttribute`

Marks an interface method as an RPC method. MethodId must be stable within a service. Lakona.Rpc source generation requires exactly one request DTO parameter and generates payload packing/unpacking for it.

### Type `Lakona.Rpc.Core.RpcProtocolLimits`

Central defaults for RPC protocol payload, transport frame, and security transform limits.

### Type `Lakona.Rpc.Core.RpcNotificationAttribute`

Marks an interface method as a server-to-client notification. MethodId must be stable within a notification contract.

### Type `Lakona.Rpc.Core.RpcPushEnvelope`

Mutable push envelope used before encoding a server-to-client notification.

### Type `Lakona.Rpc.Core.RpcPushFrame`

Decoded push envelope with an owned payload frame slice.

### Type `Lakona.Rpc.Core.RpcNotificationMethod`

Typed descriptor for a server-to-client notification method.

Type parameters:
- `TArg`: Notification DTO type.

### Type `Lakona.Rpc.Core.RpcRequestEnvelope`

Mutable request envelope used before encoding a client-to-server RPC request.

### Type `Lakona.Rpc.Core.RpcRequestFrame`

Decoded request envelope with an owned payload frame slice.

### Type `Lakona.Rpc.Core.RpcResponseEnvelope`

Mutable response envelope used before encoding a server-to-client RPC response.

### Type `Lakona.Rpc.Core.RpcResponseFrame`

Decoded response envelope with an owned payload frame slice.

### Type `Lakona.Rpc.Core.RpcServiceAttribute`

Marks an interface as an RPC service. ServiceId must be stable across versions.

### Property `Lakona.Rpc.Core.RpcServiceAttribute.ApiGroup`

Optional generated `client.Api.<group>` group name. Use this to keep generated facade names stable across namespace refactors.

### Property `Lakona.Rpc.Core.RpcServiceAttribute.ApiName`

Optional generated `client.Api.<group>.<service>` property name. Use this to keep generated facade names stable across interface renames.

### Type `Lakona.Rpc.Core.RpcStatus`

Describes framework-level RPC response outcomes only. It does not represent business failures. Non-`Ok` responses are surfaced by the client runtime as `Lakona.Rpc.Core.RpcException`; use `RpcException.Status` for machine-readable observability and retry decisions.

### Type `Lakona.Rpc.Core.RpcVoid`

Singleton marker value used for RPC methods with no return payload.

### Type `Lakona.Rpc.Core.TransportSecurityConfig`

Frame transformation settings applied above a concrete transport.

Remarks: This configuration controls optional compression and symmetric frame encryption performed by `Lakona.Rpc.Core.TransformingTransport`. It is not a TLS replacement and does not authenticate the remote peer by itself.

## Lakona.Rpc.Client

### Event `Lakona.Rpc.Client.RpcClientRuntime.Disconnected`

Raised when the receive loop ends.

Remarks: The event argument is the disconnect reason when one is available. A null value means a normal or locally requested shutdown.

### Method `Lakona.Rpc.Client.RpcClientOptions.constructor(Lakona.Rpc.Core.ITransport,Lakona.Rpc.Core.IRpcSerializer)`

Creates client runtime options.

Parameters:
- `transport`: Concrete transport used by the client.
- `serializer`: Serializer used for RPC method payloads.

Exceptions:
- `System.ArgumentNullException`: Thrown when `transport` or `serializer` is null.

### Method `Lakona.Rpc.Client.RpcClientOptions.CreateConfiguredTransport`

Returns the transport that should be passed to the runtime.

Returns: The original transport when security is disabled; otherwise a `Lakona.Rpc.Core.TransformingTransport` wrapping the original transport.

### Method `Lakona.Rpc.Client.RpcClientOptions.UseSecurity(System.Action<Lakona.Rpc.Core.TransportSecurityConfig>)`

Configures compression or encryption for the client transport.

Parameters:
- `configure`: Configuration callback.

Returns: This options instance.

Exceptions:
- `System.ArgumentNullException`: Thrown when `configure` is null.

### Method `Lakona.Rpc.Client.RpcClientRuntime.constructor(Lakona.Rpc.Client.RpcClientOptions)`

Creates a runtime from client options.

Parameters:
- `options`: Client options containing transport, serializer, keepalive, and security settings.

Exceptions:
- `System.ArgumentNullException`: Thrown when `options` is null.

### Method `Lakona.Rpc.Client.RpcClientRuntime.constructor(Lakona.Rpc.Core.ITransport,Lakona.Rpc.Core.IRpcSerializer,Lakona.Rpc.Core.RpcKeepAliveOptions)`

Creates a runtime from explicit transport and serializer instances.

Parameters:
- `transport`: Connected or connectable transport used by the runtime.
- `serializer`: Serializer used for RPC payloads.
- `keepAlive`: Optional keepalive configuration.

Exceptions:
- `System.ArgumentNullException`: Thrown when `transport` or `serializer` is null.

### Method `Lakona.Rpc.Client.RpcClientRuntime.DisposeAsync`

Stops background loops, fails pending requests, and disposes the transport.

### Method `Lakona.Rpc.Client.RpcClientRuntime.StartAsync(System.Threading.CancellationToken)`

Connects the transport and starts background runtime loops.

Remarks: A runtime is a single-use connection object. Calling `StartAsync` after it has already started throws `InvalidOperationException`; after disconnect or dispose, create a new runtime instead of restarting the old one.

Parameters:
- `ct`: Cancellation token for the initial transport connection.

Exceptions:
- `System.InvalidOperationException`: Thrown when the runtime has already been started.
- `System.ObjectDisposedException`: Thrown when the runtime has been disposed.

### Property `Lakona.Rpc.Client.RpcClientOptions.KeepAlive`

Optional keepalive configuration. Disabled by default.

### Property `Lakona.Rpc.Client.RpcClientOptions.Security`

Mutable frame security configuration.

### Property `Lakona.Rpc.Client.RpcClientOptions.Serializer`

Serializer used for request, response, and push payloads.

### Property `Lakona.Rpc.Client.RpcClientOptions.Transport`

Underlying transport before optional security wrapping.

### Property `Lakona.Rpc.Client.RpcClientRuntime.LastReceiveAt`

Last UTC timestamp at which the runtime received a frame.

### Property `Lakona.Rpc.Client.RpcClientRuntime.LastRtt`

Last measured keepalive round-trip time, when RTT measurement is enabled.

### Property `Lakona.Rpc.Client.RpcClientRuntime.LastSendAt`

Last UTC timestamp at which the runtime sent a frame.

### Property `Lakona.Rpc.Client.RpcClientRuntime.TimedOutByKeepAlive`

Indicates whether the runtime stopped because keepalive timed out.

### Type `Lakona.Rpc.Client.RpcClientOptions`

Configuration used to create a client runtime or generated client facade.

Remarks: The options object owns the selected transport and serializer references. If security is configured, `Lakona.Rpc.Client.RpcClientOptions.CreateConfiguredTransport` wraps the transport in a `Lakona.Rpc.Core.TransformingTransport`.

### Type `Lakona.Rpc.Client.RpcClientRuntime`

Default client runtime for Lakona.Rpc request/response calls and server notification dispatch.

Remarks: The runtime owns background receive, notification, and keepalive loops after `Lakona.Rpc.Client.RpcClientRuntime.StartAsync(System.Threading.CancellationToken)`. Notification handlers run on the runtime notification loop and are not marshalled to the Unity main thread.

### Type `Lakona.Rpc.Client.RpcNotificationPayloadHandler`

Handles a serialized server-to-client notification payload.

Parameters:
- `payload`: Serialized notification payload.

## Lakona.Rpc.Server

### Event `Lakona.Rpc.Server.RpcSession.Disconnected`

Raised when the session receive loop ends.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.BindGeneratedServicesFromAssembly(System.Reflection.Assembly)`

Binds generated services from an assembly.

Parameters:
- `assembly`: Assembly containing generated binder metadata.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.BindGeneratedServicesFromAssemblyContaining`

Binds generated services from the assembly containing `T`.

Type parameters:
- `T`: Type whose assembly contains generated service binders.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.BindGeneratedServicesFromEntryAssembly`

Binds generated services from the process entry assembly.

Returns: This builder.

Exceptions:
- `System.InvalidOperationException`: Thrown when the entry assembly cannot be resolved.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.Build`

Builds a server host.

Returns: A configured server host.

Exceptions:
- `System.InvalidOperationException`: Thrown when serializer or transport configuration is missing.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.ConfigureServices(System.Action<Lakona.Rpc.Server.RpcServiceRegistry>)`

Manually configures service handlers.

Remarks: Generated-support and advanced runtime configuration. Regular applications should prefer generated service binding through `Lakona.Rpc.Server.RpcServerHostBuilder.BindGeneratedServicesFromAssembly(System.Reflection.Assembly)`.

Parameters:
- `configure`: Registry configuration callback.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.Create`

Creates a new server host builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.ResolvePort(System.Int32)`

Resolves the configured port or returns a default.

Parameters:
- `defaultPort`: Default port used when no port was configured.

Returns: The configured port or `defaultPort`.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.RunAsync(System.Threading.CancellationToken)`

Builds and runs the server host.

Parameters:
- `ct`: Cancellation token for the host run loop.

Returns: A task that completes when the host stops.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseAcceptor(System.Func<System.Threading.CancellationToken,System.Threading.Tasks.ValueTask<Lakona.Rpc.Core.IRpcConnectionAcceptor>>)`

Sets a factory for creating the transport acceptor.

Parameters:
- `acceptorFactory`: Factory invoked by the host run loop.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseAcceptor(Lakona.Rpc.Core.IRpcConnectionAcceptor)`

Sets a pre-created transport acceptor.

Parameters:
- `acceptor`: Connection acceptor.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseCommandLine(System.String[])`

Applies supported command-line options to the builder.

Parameters:
- `args`: Command-line arguments, or null.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseKeepAlive(System.TimeSpan,System.TimeSpan)`

Enables keepalive with an interval and timeout.

Parameters:
- `interval`: Maximum idle receive time before sending a ping.
- `timeout`: Maximum idle receive time after a ping before disconnecting.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseKeepAlive(Lakona.Rpc.Core.RpcKeepAliveOptions)`

Sets keepalive options for accepted sessions.

Parameters:
- `keepAlive`: Keepalive options.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseLimits(System.Action<Lakona.Rpc.Server.RpcServerLimits>)`

Mutates server back-pressure limits.

Parameters:
- `configure`: Limit configuration callback.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseLimits(Lakona.Rpc.Server.RpcServerLimits)`

Copies server back-pressure limits from another instance.

Parameters:
- `limits`: Limits to copy.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseLogger(Microsoft.Extensions.Logging.ILogger)`

Uses an `Microsoft.Extensions.Logging.ILogger` instance.

Parameters:
- `logger`: Logger instance.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseLogger(System.Action<System.String>)`

Uses a delegate-backed logger.

Parameters:
- `logger`: Log sink.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UsePort(System.Int32)`

Sets the server port used by transport configuration helpers.

Parameters:
- `port`: TCP/UDP port between 1 and 65535.

Returns: This builder.

Exceptions:
- `System.ArgumentOutOfRangeException`: Thrown when `port` is outside 1..65535.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseSecurity(System.Action<Lakona.Rpc.Core.TransportSecurityConfig>)`

Configures frame compression or encryption for accepted sessions.

Parameters:
- `configure`: Configuration callback.

Returns: This builder.

Exceptions:
- `System.ArgumentNullException`: Thrown when `configure` is null.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseSerializer(Lakona.Rpc.Core.IRpcSerializer)`

Sets the serializer used by all accepted sessions.

Parameters:
- `serializer`: Payload serializer.

Returns: This builder.

Exceptions:
- `System.ArgumentNullException`: Thrown when `serializer` is null.

### Method `Lakona.Rpc.Server.RpcServerHostBuilder.UseSerializer`

Creates and sets a serializer using its public parameterless constructor.

Type parameters:
- `TSerializer`: Serializer type.

Returns: This builder.

### Method `Lakona.Rpc.Server.RpcSession.constructor(Lakona.Rpc.Core.ITransport,Lakona.Rpc.Core.IRpcSerializer,System.Boolean)`

Creates a session and optionally disposes the transport when the session is disposed.

Remarks: `RpcSession` is runtime implementation support for one accepted connection. Regular server applications should use `RpcServerHostBuilder` instead of constructing sessions directly.

Parameters:
- `transport`: Transport for this connection.
- `serializer`: Serializer used for RPC payloads.
- `ownsTransport`: Whether disposing the session also disposes the transport.

### Method `Lakona.Rpc.Server.RpcSession.constructor(Lakona.Rpc.Core.ITransport,Lakona.Rpc.Core.IRpcSerializer,System.String,System.Boolean)`

Creates a session with an explicit context id and transport ownership setting.

### Method `Lakona.Rpc.Server.RpcSession.constructor(Lakona.Rpc.Core.ITransport,Lakona.Rpc.Core.IRpcSerializer,System.String)`

Creates a session with an explicit context id.

Parameters:
- `transport`: Transport for this connection.
- `serializer`: Serializer used for RPC payloads.
- `contextId`: Stable session id used in logs and scoped services.

### Method `Lakona.Rpc.Server.RpcSession.constructor(Lakona.Rpc.Core.ITransport,Lakona.Rpc.Core.IRpcSerializer,Lakona.Rpc.Server.RpcServiceRegistry,System.Boolean)`

Creates a session backed by a service registry and optional transport ownership.

### Method `Lakona.Rpc.Server.RpcSession.constructor(Lakona.Rpc.Core.ITransport,Lakona.Rpc.Core.IRpcSerializer,Lakona.Rpc.Server.RpcServiceRegistry,System.String,System.Boolean,Lakona.Rpc.Core.RpcKeepAliveOptions,Microsoft.Extensions.Logging.ILogger,Lakona.Rpc.Server.RpcServerLimits)`

Creates a fully configured session.

Parameters:
- `transport`: Transport for this connection.
- `serializer`: Serializer used for RPC payloads.
- `registry`: Optional generated service registry.
- `contextId`: Stable session id used in logs and scoped services.
- `ownsTransport`: Whether disposing the session also disposes the transport.
- `keepAlive`: Optional keepalive configuration.
- `logger`: Optional logger.
- `limits`: Optional request concurrency and queue limits.

### Method `Lakona.Rpc.Server.RpcSession.constructor(Lakona.Rpc.Core.ITransport,Lakona.Rpc.Core.IRpcSerializer,Lakona.Rpc.Server.RpcServiceRegistry,System.String)`

Creates a session backed by a service registry with an explicit context id.

### Method `Lakona.Rpc.Server.RpcSession.constructor(Lakona.Rpc.Core.ITransport,Lakona.Rpc.Core.IRpcSerializer,Lakona.Rpc.Server.RpcServiceRegistry)`

Creates a session backed by a service registry.

### Method `Lakona.Rpc.Server.RpcSession.constructor(Lakona.Rpc.Core.ITransport,Lakona.Rpc.Core.IRpcSerializer)`

Creates a session that does not own the transport.

Parameters:
- `transport`: Transport for this connection.
- `serializer`: Serializer used for RPC payloads.

### Method `Lakona.Rpc.Server.RpcSession.DisposeAsync`

Stops the session and disposes owned resources.

### Method `Lakona.Rpc.Server.RpcSession.GetOrAddScopedService(System.Int32,System.Func<Lakona.Rpc.Server.RpcSession,T0>)`

Gets or creates a service instance scoped to this session and service id.

Parameters:
- `serviceId`: Stable service id.
- `factory`: Factory invoked once per session and service id.

Type parameters:
- `TService`: Service implementation type.

Returns: The existing or newly created service instance.

### Method `Lakona.Rpc.Server.RpcSession.SendNotificationAsync(System.Int32,System.Int32,T0,System.Threading.CancellationToken)`

Sends a server-to-client notification.

Parameters:
- `serviceId`: Stable service id.
- `methodId`: Stable notification method id.
- `arg`: Notification DTO instance.
- `ct`: Cancellation token for the send operation.

Type parameters:
- `TArg`: Notification DTO type.

### Method `Lakona.Rpc.Server.RpcSession.Register(System.Int32,System.Int32,Lakona.Rpc.Server.RpcHandler)`

Registers a low-level request handler for one service method.

Remarks: Runtime-internal handler wiring. Regular applications should define RPC contracts and service implementations, then let generated binders register handlers.

Parameters:
- `serviceId`: Stable service id.
- `methodId`: Stable method id.
- `handler`: Request handler.

### Method `Lakona.Rpc.Server.RpcSession.RunAsync(System.Threading.CancellationToken)`

Starts the session, waits for completion, and stops it in a finally block.

Parameters:
- `ct`: Cancellation token linked to the session loop.

### Method `Lakona.Rpc.Server.RpcSession.StartAsync(System.Threading.CancellationToken)`

Connects the transport and starts the session receive loop.

Remarks: A session represents one accepted connection. Calling `StartAsync` after the session has already started, stopped, disconnected, or been disposed throws `InvalidOperationException`.

Parameters:
- `ct`: Cancellation token for the initial transport connection.

Exceptions:
- `System.InvalidOperationException`: Thrown when the session has already been started.

### Method `Lakona.Rpc.Server.RpcSession.StopAsync`

Requests session shutdown and waits for in-flight requests to complete.

Remarks: `StopAsync` is idempotent cleanup, not a pause operation. After it completes, the session is terminal and cannot be started again.

### Method `Lakona.Rpc.Server.RpcSession.WaitForCompletionAsync`

Waits until the session receive loop and in-flight requests complete.

### Property `Lakona.Rpc.Server.RpcServerHostBuilder.KeepAlive`

Keepalive configuration used for accepted sessions.

### Property `Lakona.Rpc.Server.RpcServerHostBuilder.Limits`

Server back-pressure limits.

### Property `Lakona.Rpc.Server.RpcServerHostBuilder.Port`

Explicit server port set by command line parsing or `Lakona.Rpc.Server.RpcServerHostBuilder.UsePort(System.Int32)`.

### Property `Lakona.Rpc.Server.RpcServerHostBuilder.Security`

Frame security configuration applied to accepted transports.

### Property `Lakona.Rpc.Server.RpcServerHostBuilder.ServiceRegistry`

Registry used for generated and manually configured service handlers.

### Property `Lakona.Rpc.Server.RpcSession.ContextId`

Unique identifier for this connection session.

### Property `Lakona.Rpc.Server.RpcSession.LastReceiveAt`

Last UTC timestamp at which this session received a frame.

### Property `Lakona.Rpc.Server.RpcSession.LastSendAt`

Last UTC timestamp at which this session sent a frame.

### Property `Lakona.Rpc.Server.RpcSession.RemoteEndPoint`

Remote endpoint of the connected client, if the underlying transport supports it.

### Type `Lakona.Rpc.Server.RpcHandler`

Low-level handler for a decoded RPC request.

Remarks: Runtime-internal handler wiring. Regular applications should define RPC contracts and service implementations, then let generated binders register handlers.

Parameters:
- `req`: Request envelope.
- `ct`: Cancellation token for request processing.

Returns: Response envelope to send back to the client.

### Type `Lakona.Rpc.Server.RpcServerHostBuilder`

Builder for a multi-session RPC server host.

Remarks: The builder composes serializer, transport acceptor, generated service binding, keepalive, frame security, logging, and back-pressure limits. `Lakona.Rpc.Server.RpcServerHostBuilder.Build` validates required configuration before creating the host.

### Type `Lakona.Rpc.Server.RpcSession`

Runtime for one accepted client connection.

Remarks: A session owns receive, dispatch, optional keepalive, and server push for one transport connection. `RpcSession` is runtime implementation support, not a supported user-authored server host API. Regular server applications should use `Lakona.Rpc.Server.RpcServerHostBuilder` and generated binders.

