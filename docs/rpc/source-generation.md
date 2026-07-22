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

Generated Lakona server projects also set hotfix generator role properties.
The same `Lakona.Game.Server.Hotfix.Generators` analyzer package can run in
both server projects, but its outputs are role-gated:

```xml
<!-- Server.App -->
<LakonaHotfixGenerateStableRpcServices>true</LakonaHotfixGenerateStableRpcServices>

<!-- Server.Hotfix -->
<LakonaHotfixGenerateStableRpcServices>false</LakonaHotfixGenerateStableRpcServices>
<LakonaHotfixProject>true</LakonaHotfixProject>
```

The role properties are exposed to Roslyn as `build_property.*` values through
`CompilerVisibleProperty`. `Server.App` owns stable generated RPC binders and
service-call contexts; `Server.Hotfix` owns replaceable implementations and
actor behavior. The complete ownership and dispatch contract lives in
[Generated Hotfix Service Binding](../hotfix/service-binding.md).

Generated client projects opt into client glue with the matching client
generation property or framework-owned Unity analyzer defaults. Generated
Unity and Tuanjie clients do not contain a project-local RPC generation marker
file. Their generated client API is emitted into `Client.Generated`.

Game client wrappers are an additional opt-in for projects that use
Lakona.Game:

```xml
<LakonaGameGenerateClient>true</LakonaGameGenerateClient>
```

When enabled, the generator emits `LakonaGameClient` in the same namespace as
the generated `RpcClient`. The wrapper owns the framework handshake, heartbeat,
recovery, and static callback receiver matching. Business RPC services are
exposed through `gameClient.Api`, so game client code uses the generated wrapper
as its single connection entry point.

Generated application code should look like this:

```csharp
using Client.Generated;

await using var gameClient = new LakonaGameClient(options, callbackReceiver);
await gameClient.ConnectAsync(cancellationToken);

var login = gameClient.Api.Shared.Login;
```

Users should not construct or store the generated `RpcClient`, invoke framework
handshake methods, create callback binding containers, or hard-code framework
RPC ids. `RpcClient` remains an RPC-only client; the generated game wrapper is
the stable Game entry point. Its connection and recovery contract is documented
in [Session Lifecycle](../session.md).

An explicit MSBuild `false` disables generated game client wrapper output.
Tool-generated Unity, Tuanjie, Godot, and console clients enable it by default.

## Ownership

- `Lakona.Rpc.Core` owns runtime attributes and shared contracts.
- `Lakona.Rpc.Analyzers` owns compile-time diagnostics and source generation.
- `Lakona.Tool` owns generated project files and package references, but does
  not write generated RPC glue as source files.

## Generator Maintainability

Generators are allowed to hide runtime glue from user projects, but they must
not become an unbounded compatibility layer. When a generator emits multiple
runtime products, the implementation should be split by product boundary:

- RPC contract discovery, diagnostics, client facades, notification binders,
  and server binders belong to the RPC generator boundary.
- Hotfix state accessors, stable RPC service proxies, behavior-derived actor
  access, generic actor call helpers, and hotfix diagnostics are separate hotfix
  generator products even when they are packaged in one analyzer assembly.
- Shared naming, type-display, and literal-escaping helpers should be factored
  as helpers, not used as a reason to keep unrelated emitters in one large
  generator file.

Generated server binders use `RpcConnectionInfo`, `RpcNotificationChannel`, and
typed registration. They must not reference `RpcSession`, which stays behind
the runtime-internal boundary described in
[public-api-boundaries.md](public-api-boundaries.md).
