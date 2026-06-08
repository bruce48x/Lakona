# Lakona.Rpc.Core

Shared abstractions and wire-level contracts for Lakona.Rpc.

`Lakona.Rpc.Core` does not depend on concrete serializer or transport implementations.
Use it together with `Lakona.Rpc.Client` / `Lakona.Rpc.Server` and optional serializer/transport packages.
The NuGet package also carries Lakona.Rpc contract analyzers so invalid or duplicate RPC ids surface during normal C# editing/builds.

## Install

```bash
dotnet add package Lakona.Rpc.Core
```

## Documentation

API reference: https://bruce48x.github.io/Lakona.Rpc/reference/api/

Design boundary: https://bruce48x.github.io/Lakona.Rpc/concepts/design-boundary/

## Includes

- RPC attributes: `RpcServiceAttribute`, `RpcMethodAttribute`, `RpcNotificationContractAttribute`, `RpcNotificationAttribute`
- Contract analyzers for non-positive ids and duplicate service/method/notification ids
- Transport and serializer abstractions: `ITransport`, `IRpcSerializer`, `IRpcClient`
- Envelopes, status, exceptions, and notification diagnostics: `RpcRequestEnvelope`, `RpcResponseEnvelope`, `RpcStatus`, `RpcException`, `RpcNotificationHandlerExceptionContext`, `RpcUnhandledNotificationContext`, `RpcVoid`
- Envelope codec: `RpcEnvelopeCodec`
- Shared framing/security helpers: `LengthPrefix`, `TransportFrameCodec`, `TransformingTransport`, `TransportSecurityConfig`
