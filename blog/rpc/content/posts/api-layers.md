+++
title = "API Layers"
date = 2026-06-05T00:00:00+08:00
+++

Lakona.Rpc exposes several kinds of public APIs. `public` means the type can be referenced by C#, but it does not always mean the type is a normal application entry point.

Use this page to decide which APIs your project should depend on directly.

## Stable User API

These APIs are intended for regular application projects. They are the safest APIs to build long-lived code on.

- RPC contract attributes such as `[RpcService]`, `[RpcMethod]`, `[RpcNotificationContract]`, and `[RpcNotification]`.
- Generated client facade and generated server binders.
- `RpcClientOptions`.
- `RpcClientRuntime` when used through the generated client flow.
- `RpcServerHostBuilder`.
- `RpcServerHost`.
- `RpcServerLimits`.
- Official transport constructors.
- Official serializer constructors.
- `RpcKeepAliveOptions`.
- `RpcException`.
- `RpcStatus`.

User-facing tutorials and starter projects should stay inside this layer whenever possible.

## Stable Extension API

These APIs are intended for projects that need custom transports, serializers, or connection acceptors.

- `ITransport`
- `IRpcSerializer`
- `IRpcConnectionAcceptor`
- `IRemoteEndPointProvider`
- `RpcAcceptedConnection`
- `TransportFrame`

Lakona.Rpc supports this extension model because official transport and serializer packages cannot cover every project requirement.

## Generated-Support API

These APIs primarily support source-generated glue. Users may see them, but their compatibility is tied to matching `Lakona.Rpc.Analyzers` and runtime package versions.

- `IRpcClient`
- `RpcMethod<TArg, TResult>`
- `RpcNotificationMethod<TArg>`
- `RpcGeneratedServicesBinderAttribute`
- `RpcGeneratedServiceBinder`
- `RpcServiceRegistry`, while generated server binding still depends on registry plumbing

If this layer changes, rebuild the project so the source generator emits fresh glue code.

## Runtime Internal API

These APIs may appear in reference docs while the project is still before a hard freeze, but they are not normal application extension points.

- `RpcSession`
- `RpcHandler`
- `RpcSessionHandler`
- direct `(serviceId, methodId)` handler registration
- low-level session notification sending

Server applications should not create `RpcSession` directly. Use `RpcServerHostBuilder`, generated service binders, service implementation classes, and notification contracts.

## Protocol and Infrastructure API

These APIs support protocol tooling, tests, diagnostics, transport internals, and package cooperation. They are not business application entry points.

- `RpcEnvelopeCodec`
- RPC envelope and decoded frame DTOs
- `RpcFrameType`
- `RpcProtocolLimits`
- `LengthPrefix`
- `TransportFrameCodec`
- `TransformingTransport`
- `TransportSecurityConfig`
- `PooledFrameBufferWriter`

Use this layer only when building tooling, diagnostics, or lower-level integrations. Application code should normally express behavior through contracts and DTOs instead.
