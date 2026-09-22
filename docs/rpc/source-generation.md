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

Contract discovery traverses namespaces and nested types once in both the current
compilation and referenced contract assemblies. Public service and notification
interfaces nested in public non-generic classes are supported. Nesting does not
create a separate service-id scope: distinct services must still use unique ids.

Generated type references preserve Roslyn's fully qualified representation,
including qualification inside generic arguments. Built-in type keywords such as
`string` and `int` remain valid C# keywords rather than receiving a `global::`
prefix. Payload serialization support remains the selected serializer's concern.

Generated service calls return runtime ValueTasks directly. Void methods use
`RpcVoidTask.FromResult` to discard RpcVoid without adding an async task layer
which could reorder response continuations. See
[Ordered Message Entry](architecture.md#ordered-message-entry) for the runtime
ordering guarantee.

Generated RPC glue is compiler output. New Lakona projects must not contain
project-local `Generated/` RPC source folders, codegen scripts, editor
postprocessors, or tool manifests for day-to-day RPC generation.

`Lakona.Rpc.Analyzers` is an internal assembly boundary, not a package users
install. `Lakona.Rpc.Core` carries the matching assembly under NuGet's analyzer
path; `Lakona.Rpc.Client` and `Lakona.Rpc.Server` bring Core in transitively.
Shared contract projects may reference Core directly.

## Project Configuration

Generated Lakona Game server projects declare one project role:

```xml
<!-- Server.App -->
<LakonaProjectRole>ServerApp</LakonaProjectRole>

<!-- Server.Hotfix -->
<LakonaProjectRole>Hotfix</LakonaProjectRole>
```

The RPC and Hotfix compiler extensions both derive the stable generated server
namespace from `$(RootNamespace).Generated`. `ServerApp` enables server RPC
glue and stable Hotfix service binding; `Hotfix` disables those stable outputs
and enables Hotfix-only validation and replaceable implementations. A Game
project must not set a separate generated server namespace.

The role and root namespace are exposed to Roslyn as `build_property.*` values through
`CompilerVisibleProperty`. `Server.App` owns stable generated RPC binders and
service-call contexts; `Server.Hotfix` owns replaceable implementations and
actor behavior. The complete ownership and dispatch contract lives in
[Generated Hotfix Service Binding](../hotfix/service-binding.md).

`LakonaProjectRole` is optional for RPC-only projects, but every non-empty value
must be `ServerApp` or `Hotfix` (case-insensitive). The compiler reports
`LAKONA20048` with both supported values when a project declares an unknown
role, so a typo cannot silently disable role-owned generation and Hotfix
validation. `RootNamespace` is already a compiler-visible property owned by the
.NET SDK; Lakona's transitive build asset exposes only the Lakona-owned role
property and does not redeclare that SDK contract.

RPC-only projects outside Lakona Game may continue to use
`LakonaRpcGenerateServer` and `LakonaRpcServerGeneratedNamespace`; these are the
generic RPC generator controls, not Game project role controls.

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
the generated `RpcClient`. The wrapper supplies the typed API and static callback
receiver matching. `LakonaGameClientLifecycle` in `Lakona.Game.Client` owns the
runtime behavior described in [Game Client Lifecycle](../session.md#game-client-lifecycle).
Business RPC services are
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

The generated wrapper passes a callback-binding action to the runtime; the
runtime creates and owns each `RpcClientRuntime` and invokes that action before
starting it. The same API dispatch target survives connection replacement.
Connection, recovery, and disposal behavior are defined in
[Game Client Lifecycle](../session.md#game-client-lifecycle).
Lifecycle behavior is tested in the client runtime, with generated-client
integration tests covering typed calls, callbacks, and recovery. Generator
tests retain C# 9 compilation and contract-binding checks.

An explicit MSBuild `false` disables generated game client wrapper output.
Tool-generated Unity, Tuanjie, Godot, and console clients enable it by default.

## Ownership

- `Lakona.Rpc.Core` owns runtime attributes, shared contracts, and delivery of
  the matching compiler extension.
- The internal `Lakona.Rpc.Analyzers` project owns compile-time diagnostics and
  source-generation implementation.
- `Lakona.Tool` owns generated project files and package references, but does
  not write generated RPC glue as source files.

## Notification Contract Association

```csharp
[RpcService(10, NotificationContract = typeof(IPlayerCallback))]
public interface IPlayerService
{
    // RPC methods
}

[RpcNotificationContract]
public interface IPlayerCallback
{
    [RpcNotification(1)]
    void OnMatchmakingStatus(MatchmakingStatusUpdate update);
}
```

`[RpcNotificationContract]` is a parameterless interface marker. The
`RpcServiceAttribute.NotificationContract` named argument is the single
association authority: the generator discovers every marked notification
contract and associates it with the service that names it, without reading any
service pointer from the marker.

The generator rejects a service whose `NotificationContract` refers to a type
that is not a marked notification contract, and it validates the association
as one-to-one: a notification contract referenced by more than one RPC service
is an error, so the reverse ownership is proven by explicit inverse-map
validation instead of duplicated declaration. A marked but unreferenced
notification contract stays unused and produces no service-bound notification
glue.

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
