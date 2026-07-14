# RPC

Lakona.Rpc is the typed communication foundation under Lakona. It exists so
game code can describe service contracts once, share DTOs between server and
Unity/Godot clients, and let generated glue handle frames, dispatch,
callbacks, and transport differences.

RPC is infrastructure. Business semantics live in contracts and DTOs, not in
transport code.

## Design Principles

### Contracts Own Semantics

RPC service interfaces, method ids, notification contracts, and DTOs live in a
shared assembly. Server and client compile the same contract source so protocol
drift is not a normal workflow.

```csharp
[RpcService(10, NotificationContract = typeof(IPlayerCallback))]
public interface IPlayerService
{
    [RpcMethod(1)]
    ValueTask<LoginReply> LoginAsync(LoginRequest request);
}
```

Stable numeric ids are part of the protocol contract. Do not reuse published
service ids, method ids, or notification ids for different meanings.

### Generated Code Owns Glue

`Lakona.Rpc.Analyzers` reads shared contracts at compile time and emits client
facades, notification binders, server binders, and generated service metadata.

Generated RPC glue is compiler output. New projects must not contain
project-local `Generated/` RPC source folders, codegen scripts, editor
postprocessors, or tool manifests for day-to-day RPC generation.

See [source-generation.md](source-generation.md) for the source-generation
contract.

### Runtime Owns Frames And Sessions

The runtime turns generated method calls into request, response, and push
frames. Server applications should use high-level host configuration and
generated binders. They should not hand-write `RpcSession` loops or
`serviceId:methodId` dispatch dictionaries.

Public API commitment boundaries are documented in
[api-stability/public-api-boundaries.md](api-stability/public-api-boundaries.md).

### Transport And Serializer Are Replaceable

Transports and serializers are extension points. Gameplay code should not care
whether the connection uses TCP, WebSocket, KCP, loopback, JSON, or MemoryPack.

KCP updates are scheduled independently per registered transport. The global
interval tick may enqueue due work, but a registration is never executed
concurrently with itself and a blocked connection must not delay updates for
other connections.

Custom transports, connection acceptors, and serializers belong behind stable
extension interfaces such as `ITransport`, `IRpcConnectionAcceptor`, and
`IRpcSerializer`.

### Callback Is Part Of The Contract

Server-to-client push is modeled through notification contracts. A callback
contract is not a separate event bus; it is the reverse direction of the same
typed RPC session.

For replayable game notifications above RPC callbacks, publish notification
intent through the Lakona game session APIs. The game framework owns reliable
push sequencing, acknowledgement, and replay policy.

### Framework Status Is Not Business Failure

`RpcStatus` describes framework outcomes such as missing handlers, handler
failure, overload, bad request, or protocol error. Business failures such as
login rejection, room full, invalid move, or cooldown not ready belong in
business DTOs.

See [protocol/rpc-status-error-model.md](protocol/rpc-status-error-model.md)
for status semantics.

## Maintainer References

- [source-generation.md](source-generation.md)
- [protocol/wire-protocol-v1.md](protocol/wire-protocol-v1.md)
- [protocol/rpc-status-error-model.md](protocol/rpc-status-error-model.md)
- [api-stability/public-api-boundaries.md](api-stability/public-api-boundaries.md)
