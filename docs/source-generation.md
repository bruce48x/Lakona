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

Generated projects declare `CompilerVisibleProperty` for
`LakonaHotfixGenerateStableRpcServices` and `LakonaHotfixProject`, so Roslyn exposes them to the analyzer
as a `build_property.*` value. Package consumers also receive that
compiler-visible property name through the generator package's
`buildTransitive` props once packaged.

`Server.App` owns stable RPC service proxies, endpoint binders, required
hotfix service contract providers, service-scoped `*ServiceCall<TRequest>`
contexts, actor state, and actor DTOs. `Server.Hotfix`
owns replaceable service implementations, lifecycle implementations, behavior
code, `[HotfixComponent]` helpers, and a behavior-derived `ActorAccess` root.
Components are emitted as generation-scoped singleton registrations. Concrete
classes without a hotfix role are rejected, and data carriers must remain in
the stable App or Contracts assembly. Public instance methods
in sealed partial `[HotfixBehaviorOf]` classes define the actor API its selectors expose. The
hotfix generator emits constrained `Local<TActor>(id)` and `Route<TActor>(id)`
selectors plus generic
`CallAsync` / `PostAsync` helpers that accept static method selectors such as
`static behavior => behavior.JoinAsync`; it does not emit wrapper members whose names mirror
the behavior methods or one plural collection class per actor.
`LKNHOTFIX040` requires that exact direct, noncapturing selector shape so a call
site cannot hide mutable state or indirect runtime method choice.

Each generated hotfix-backed RPC service receives one stable call-context type.
For example, `IChatService` with `IChatCallback` produces
`ChatServiceCall<TRequest>`, whose `Callback` property is already typed as
`IChatCallback`. Hotfix handlers therefore repeat only the request type, while
the shared service contract remains the single source of truth for its callback
association. Services without a notification contract receive the same call
shape without a `Callback` property.

`Lakona.Game.Server.Generators`, for stable non-hotfix actor methods, emits the
same single-root selection model in `Lakona.Game.Server.Generated`. Because C#
does not convert an unbound instance method such as `RoomActor.JoinAsync`
directly to an open delegate, that path uses a typed lambda. The default
hotfix path keeps the direct behavior implementation as the navigable symbol.

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

The generated wrapper is a stable facade. Application code calls `ConnectAsync`
once; after a Game Session is established, the wrapper may create multiple
internal RPC connection generations during framework-managed recovery. The
initial connection sequence is:

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

Before a Game Session is established, a connection failure fails normally.
After establishment, disconnect starts bounded recovery on the same wrapper.
The public `Api`, service proxies, and callback receivers remain stable while
the internal RPC client and transport are replaced. Terminal recovery outcomes
raise `Disconnected`; disposal cancels and joins any in-progress recovery.

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
  access, generic actor call helpers, and hotfix diagnostics are separate hotfix
  generator products even when they are packaged in one analyzer assembly.
- Shared naming, type-display, and literal-escaping helpers should be factored
  as helpers, not used as a reason to keep unrelated emitters in one large
  generator file.

Generated support APIs should also shrink over time. Generated server binders
use `RpcConnectionInfo`, `RpcNotificationChannel`, and typed registration;
`RpcSession` stays behind the runtime-internal boundary described in
[public-api-boundaries.md](api-stability/public-api-boundaries.md).
