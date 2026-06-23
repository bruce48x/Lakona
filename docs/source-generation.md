# RPC Source Generation

Lakona.Rpc uses Roslyn source generators as the normal RPC glue route for
generated Lakona projects and hand-written applications.

## Contract

RPC service interfaces, method ids, notification contracts, and DTOs live in a
shared assembly. `Lakona.Rpc.Analyzers` reads those contracts at compilation
time and emits:

- client facades and service clients
- notification binders
- server binders
- generated binder assembly metadata

Generated RPC glue is compiler output. New Lakona projects must not contain
project-local `Generated/` RPC source folders, codegen scripts, editor
postprocessors, or tool manifests for day-to-day RPC generation.

## Project Configuration

Generated server projects opt into server glue with:

```xml
<LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>
<LakonaRpcServerGeneratedNamespace>Server.App.Generated</LakonaRpcServerGeneratedNamespace>
```

Generated client projects opt into client glue with the matching client
generation property or Unity-compatible analyzer configuration.

Game client wrappers are an additional opt-in for projects that use
Lakona.Game:

```xml
<LakonaGameGenerateClient>true</LakonaGameGenerateClient>
<LakonaGameClientRuntime>godot</LakonaGameClientRuntime>
<LakonaGameClientPlatform>godot</LakonaGameClientPlatform>
<LakonaGameClientGameVersion>chat</LakonaGameClientGameVersion>
```

When enabled, the generator emits `LakonaGameClient` in the same namespace as
the generated `RpcClient`. The wrapper owns the generated `RpcClient`,
framework handshake, Game heartbeat, and static callback receiver type checks.
Business RPC services are exposed through `gameClient.Api`, so game client code
uses the generated wrapper as its single connection entry point.

## Ownership

- `Lakona.Rpc.Core` owns runtime attributes and shared contracts.
- `Lakona.Rpc.Analyzers` owns compile-time diagnostics and source generation.
- `Lakona.Tool` owns generated project files and package references, but does
  not write generated RPC glue as source files.
