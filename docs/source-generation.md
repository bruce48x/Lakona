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
```

Generated projects declare `CompilerVisibleProperty` for
`LakonaHotfixGenerateStableRpcServices`, so Roslyn exposes it to the analyzer
as a `build_property.*` value. Package consumers also receive that
compiler-visible property name through the generator package's
`buildTransitive` props once packaged.

`Server.App` owns stable RPC service proxies, endpoint binders, required
hotfix service contract providers, actor state, and actor DTOs. `Server.Hotfix`
owns replaceable service implementations, lifecycle implementations, behavior
code, and behavior-derived actor selectors and refs. Public extension methods
in `[HotfixBehaviorOf]` classes define the actor API those refs expose.

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
the generated `RpcClient`. The wrapper owns the generated `RpcClient`,
framework handshake, Game heartbeat, and static callback receiver type checks.
Business RPC services are exposed through `gameClient.Api`, so game client code
uses the generated wrapper as its single connection entry point.

Generated application code should look like this:

```csharp
using Client.Generated;

await using var gameClient = new LakonaGameClient(options, callbackReceiver);
await gameClient.ConnectAsync(cancellationToken);

var login = gameClient.Api.Shared.Login;
```

Users should not construct or store the generated `RpcClient`, build
`GameClientHello`, call handshake methods, create callback binding containers,
or hard-code framework heartbeat RPC ids. `RpcClient` remains a pure RPC client
and does not know about `LakonaGameClient`; the generated wrapper composes the
generated `RpcClient` with `Lakona.Game.Client.LakonaGameClientCore`.

The generated wrapper is single-use. `ConnectAsync` is the only normal
connection entry point and performs the full framework connection sequence:

1. validate the wrapper is not disposed and has not already started;
2. move the core state to connecting;
3. bind generated callbacks from the supplied receivers;
4. connect the internal RPC client;
5. run the framework game handshake;
6. apply the `ServerHello` protocol version, reliable-push policy, and
   heartbeat policy;
7. start the framework heartbeat with the server-owned policy;
8. mark the wrapper ready and open the `Api` gate.

`gameClient.Api` must stay closed until step 8 completes. Accessing it before a
successful connection throws:

```txt
LakonaGameClient is not connected. Call ConnectAsync first.
```

After a connection failure or disconnect, the wrapper is not reusable; dispose
it and create a new generated `LakonaGameClient`.

Generated constructors accept game client options:

```csharp
public LakonaGameClient(LakonaGameClientOptions gameOptions, params object[] callbackReceivers)
```

`LakonaGameClientOptions` inherits `RpcClientOptions`, so one options object
configures transport, serializer, transport keepalive, RPC logging, and game
heartbeat behavior. `RpcClientOptions` remains the entry type for RPC-only
clients that do not use `LakonaGameClient`.

Callback receivers are optional, but null receivers are invalid. A receiver may
implement multiple generated callback contracts. A receiver that implements no
known generated callback contract is allowed and ignored. Supplying more than
one receiver for the same callback contract is invalid. The generator should
use static type checks for known callback contracts, not runtime
reflection-based discovery.

An explicit MSBuild `false` disables generated game client wrapper output.
Tool-generated Unity, Tuanjie, Godot, and console clients should enable game
client generation by default without runtime, platform, or game-version
metadata.

`GameClientHello` carries only `ProtocolVersion = 1`; platform, game version,
build id, runtime, and capability metadata are application concerns, not
default framework handshake fields.

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
  refs, behavior wrappers, and hotfix diagnostics are separate hotfix generator
  products even when they are packaged in one analyzer assembly.
- Shared naming, type-display, and literal-escaping helpers should be factored
  as helpers, not used as a reason to keep unrelated emitters in one large
  generator file.

Generated support APIs should also shrink over time. In particular, generated
server binders must stop exposing `RpcSession` in public signatures before
`RpcSession` can be moved fully behind the runtime-internal boundary described
in [public-api-boundaries.md](api-stability/public-api-boundaries.md).
