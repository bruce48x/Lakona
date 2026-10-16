# Hotfix Components

`[HotfixComponent]` is the application model for node-local tools, resource
initialization and shared business operations. Logic lives in `Server.Hotfix`.
An optional `[ComponentState]` in `Server.App` holds data and resource handles
that survive replacement of the Hotfix assembly. Components without business
state need no State class; the framework does not generate an anonymous one.

## Declaration

```csharp
// Server.App, with LakonaProjectRole=ServerApp
[ComponentState]
public sealed class RegionState
{
    internal IReadOnlyDictionary<int, RegionConfig> Regions =
        new Dictionary<int, RegionConfig>();
}

// Server.Hotfix, with LakonaProjectRole=Hotfix
[HotfixComponent]
[NodeRole("data")]
public sealed class RegionConfigUtil(RegionState state, NpgsqlDataSource dataSource)
{
    [ComponentStart]
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        // Load with finite awaited I/O, then publish a complete stable snapshot.
        state.Regions = await LoadRegionsAsync(dataSource, cancellationToken);
    }

    public RegionConfig Get(int id) => state.Regions[id];
    // LoadRegionsAsync and the stable RegionConfig DTO are application-defined.
}
```

Components are public, sealed, top-level, non-generic classes with exactly one
public constructor. Inject dependencies directly and keep dependency fields
private and readonly. A component may inject at most one marked State. An injected
State type has exactly one component owner; consumers inject that component, never
its State. App can expose State fields to its paired Hotfix assembly through
`InternalsVisibleTo`. This is an ownership contract, not a security boundary.

State injection uses a required, unkeyed, by-value parameter of its concrete
marked type. Arrays, collections, factories, Lazy and other nested generic
wrappers containing State are unsupported, including for non-component
consumers. Ordinary stable resource interfaces and wrappers remain supported.
`ILogger<State>` is a logging category, so it does not count as State injection.
Retain State in at most one private readonly field or private get-only auto-property,
using that same concrete State type. A captured primary-constructor parameter
also counts as storage: use it directly, or initialize one member and use that
member thereafter. Do not retain both or erase State to object/interface storage.
Private computed views add no storage. These violations use `LAKONA20061`;
runtime composition also rejects wrapped injection, duplicate stored State fields
(including compiler-generated fields) and multiple component owners before Start.

State classes are public, sealed, top-level, non-generic App classes with a
public parameterless constructor. Constructors only initialize basic structures.
Application assembly discovery and generated `LakonaProjectRole` metadata
identify them; filenames and directory names do not. The framework registers
each concrete State type once in root DI. Applications must not override this
registration, create State manually or shadow it in generation DI.

State classes are optional; their `[ComponentState]` marker is mandatory. A
component directly writing fields or properties of a constructor-injected
ServerApp object must use marked State (`LAKONA20062`). Manually registering an
unmarked carrier as a Singleton does not exempt these writes. Mark the carrier
and remove its manual registration, or use the operations of a stable resource.

The compile-time check covers constructor parameters and their directly assigned
component fields/get-only properties, including primary constructors and
transparent casts. Assignments, compound/coalescing/deconstruction assignments,
increments/decrements and `ref`/`out` arguments are writes. Reads, resource API
calls, ordinary method DTO parameters and newly constructed objects remain
valid. App identity comes from `LakonaProjectRole` assembly metadata, not names;
third-party client types are not inferred to be State because they are mutable.
This direct-write check does not follow local aliases or inspect called method
bodies and nested object graphs. Those paths still obey the State ownership
contract; runtime discovery only recognizes explicitly marked State.

`LakonaProjectRole` uses the existing project-role rules: surrounding whitespace
is ignored and `ServerApp`/`Hotfix` comparisons are case-insensitive. Component
and State diagnostics use the same parsing as generation.

Both hooks are optional and independent. Methods are identified by attribute,
not by name. Each kind appears at most once, and a method cannot carry both:

```csharp
[ComponentStart]
public ValueTask StartAsync(CancellationToken cancellationToken) => default;

[ComponentStop]
public ValueTask StopAsync(CancellationToken cancellationToken) => default;
```

The signature is exactly a public instance non-generic method returning
`ValueTask`, with one mandatory by-value `CancellationToken`. `Task`, generic
returns, optional tokens, `ref`, `in`, `out`, and context wrappers are unsupported.
Omit unused hooks instead of adding empty methods.

## Stable service registration

The optional `[ComponentConfigureServices]` method belongs to the
`[HotfixComponent]` in `Server.Hotfix`, alongside Start and Stop. App State only
holds stable data/resource handles; it cannot declare this method. No State is
needed merely to register a stable resource.

```csharp
// Server.Hotfix
[HotfixComponent]
[NodeRole("data")]
public sealed class PostgresUserStore(NpgsqlDataSource dataSource)
{
    [ComponentConfigureServices]
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AgarGamePostgres")
            ?? throw new InvalidOperationException("PostgreSQL configuration is required.");
        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));
    }
}
```

The signature is exactly public static non-generic `void(IServiceCollection,
IConfiguration)`, with two mandatory by-value parameters, no `async`, and at
most one marked method per component. Names are not binding. State itself remains
framework-registered; the hook cannot register or replace any marked State or component.
All declarations are validated before any registration method executes.

The component's existing optional `NodeRole` filters registration as well as
activation; unmarked means every node. Filtering occurs before construction or
registration, so data-only configuration is not required on gateway/battle nodes.

Methods execute once per host Build, after App `AddServices`, in ordinal component
full-name order. They only register services and validate configuration, without
constructing components, resolving services, building a temporary provider or doing
I/O. They cannot rely on another registration method having run. Normal DI
registration ordering and `TryAdd` semantics apply; there is no registration
dependency graph or priority API.

This entry accepts unkeyed singleton registrations of closed stable service and
implementation types, using `AddSingleton<TService, TImplementation>` or a factory.
Instances, scoped/transient registrations and open generics are unsupported here.
The root must be able to resolve every factory dependency without Hotfix logic or
ComponentStart. Keep full host composition in App `AddServices` when needed.
Keep resource libraries in App's deployment dependency closure so their types
are shared by every Hotfix load context.

Factory registrations are materialized through root DI immediately after Build,
including earlier descriptors hidden by last-registration semantics. The framework
then clears their Hotfix factory delegates. DI keeps the stable resources and
owns disposal; no second provider or resource cache is introduced. Factories must
return stable objects that retain no Hotfix objects, callbacks, Types or metadata.
Obvious invalid return types are rejected, but arbitrary third-party object graphs
are not scanned. A rejected/partially initialized factory owns its partial work;
previous successful resources are released with root DI if startup fails.

Registrations belong to root DI. A registered singleton is shared by every
Hotfix generation and is disposed by root DI after component Stop and generation
cleanup. The first Hotfix assembly is loaded before root Build and handed directly
to HotfixManager for first activation; a moving version pointer cannot select a
different initial assembly. A host disposed without Start unloads this prepared
assembly too. Hotfix Validate/reload validates the declaration but does not repeat
registration. Editing registration logic takes effect only after node restart.
Changing stable types still requires rebuilding App and Hotfix together. Existing
`[HotfixConfigureServices]` remains the separate entry
for generation-owned services; its singleton has one instance per generation.

## Startup

The framework filters components by the existing `NodeRole` rules before
construction. An unmarked component runs on every node. A component requiring
a component excluded from the local role fails validation; it does not enable
that dependency automatically.

Constructor dependencies form a DAG. Dependencies start before consumers,
serially, with deterministic full-name ordering for unrelated nodes. Even
components without hooks participate in the activation record. Constructors
only assemble dependencies: they cannot do I/O, modify State, or use data that
requires Start to have completed. Every parameter must be resolvable before
Start; for an asynchronously created client inject its stable owner, rather
than a factory that fails until a connection exists.

Components are injected by concrete type only, including from Behavior, RPC
and HTTP services. Component interfaces/base aliases, collections, keyed or
optional injection, factories, overrides and service locators are unsupported.
The generated concrete singleton registration is authoritative. Stable
infrastructure dependencies such as `ILogger<T>`, `IConfiguration` or a root
database pool still use normal DI. Opaque infrastructure factories cannot hide
component lifecycle dependencies.

Initial component Start completes inside candidate activation before Startup
Actors, RPC/listener admission and Ready publication. Metadata validation runs
without Start. A failed initial Start keeps the node NotReady and stops only
successfully activated components, in reverse order, using an independent
shutdown token. The failing component owns cleanup of its partially created
resources. The framework preserves the original error, reports cleanup failures
and continues other stops. There is no automatic retry or forced termination;
restart after failed initialization. Cancellation is a request, not proof that
an operation has finished.

## Reload and stable State

Each generation gets new singleton component objects; ordinary constructor
injection supplies the same root State objects. A normal reload runs neither
Start nor Stop. V1 can perform Start and V3 the final Stop, so lifecycle data
must not be kept in component instance fields.

The first published generation freezes the effective component set, including
stateless helpers. Identity is `Type.FullName`, with no extra ID or alias.
Adding, removing or renaming a component requires restart. Candidates cannot
change State binding or introduce a dependency inconsistent with the recorded
activation order. Rejection leaves the previous generation published. Changing
Start code does not apply new initialization to an already activated node.

State can contain stable DTOs, collections, synchronization primitives and
stable external resource handles. It must not retain Hotfix objects, delegates,
callbacks, event subscriptions, tasks, providers or reflection metadata from a
collectible generation, including through containers or `object`. State type
layout and stable dependencies require an App rebuild and node restart. An old
and a new generation may overlap while sharing State; use stable synchronization
or immutable snapshots and maintain data semantics across versions. A component
provides no mailbox, mutual exclusion or thread-safety guarantee.

Declaration, ownership, registration and direct-access diagnostics catch common
violations. Complex reference escapes remain a usage contract; there is no heap
scanner or general State migration system. Do not expose a State object or a
mutable collection from it. An `IReadOnlyDictionary` view alone does not make a
mutable backing dictionary safe.

## Shutdown and resource ownership

Standard shutdown freezes publication and closes managed ingress using the
existing node admission and Actor mechanisms. ActorStop runs before component
Stop. The latest component implementation performs Stop in reverse **recorded**
activation order, after existing generation leases (including older generations
sharing State) finish. Provider cleanup and load-context retirement then proceed;
root DI performs final stable resource disposal last.

Components do not intercept ordinary methods or count arbitrary direct calls.
Owners of custom callers must stop those sources and await completion before
component shutdown. A shutdown token does not justify disposing resources still
in use. The framework waits for existing execution to finish, so code that
ignores shutdown can delay process exit. Stop failures do not skip other Stop
hooks or provider cleanup.

Register synchronously constructible stable clients through the component's
`ComponentConfigureServices` method, or use App `AddServices` for global host
composition. Component registration is automatically role-filtered. Asynchronous
business initialization belongs in component Start. For a resource created in
Start, retain a local candidate, await creation/probe completely, transfer it
to State only on success, and dispose the candidate on failure. State may
implement mechanical `IDisposable`/`IAsyncDisposable`; root DI owns that final
release. Stop can gracefully close a resource but must not duplicate its final
disposal. Do not register a second DI owner for a State-owned resource.

## Background work

Components do not support detached tasks, timers, recurring loops, long-lived
callbacks or event subscriptions into Hotfix. Await all finite I/O within the
call. Do not abandon resource creation with `WaitAsync(token)` and leave a
successful late result without an owner. Configure finite client timeouts.

Use Actor + Behavior and `ActorTimer` for scheduled work; a timer callback can
await a component call within the existing managed execution scope. Adding
Start/Stop to a tool does not turn it into an Actor or a background-worker host.

## Agar example

The runnable example is [Game.Unity.Agar](../../samples/Game.Unity.Agar/README.md).
Its [PostgresUserStore](../../samples/Game.Unity.Agar/Server/Hotfix/Users/PostgresUserStore.cs),
[RedisLeaderboardStore](../../samples/Game.Unity.Agar/Server/Hotfix/Leaderboard/RedisLeaderboardStore.cs)
and [RedisLeaderboardState](../../samples/Game.Unity.Agar/Server/App/Infrastructure/Redis/RedisLeaderboardState.cs)
demonstrate the two resource ownership patterns:

| Concern | Stable App | Hotfix |
| --- | --- | --- |
| PostgreSQL | Stable Npgsql dependency; no registration class or State needed | `Users/PostgresUserStore` registers root-owned NpgsqlDataSource in ComponentConfigureServices on data nodes, initializes schema/probes in Start and owns user SQL |
| Redis leaderboard | `RedisLeaderboardState` owns the connection and startup key prefix, with mechanical final disposal | `Leaderboard/RedisLeaderboardStore` connects/probes in Start, runs leaderboard operations, and closes gracefully in Stop |
| Consumers | Stable Actor fields and persisted DTOs | User/Leaderboard Behavior and operations HTTP inject the concrete stores |
| Stateless tools | No State | `RoomRules`, `RoomNotifier`, `MatchmakingNotifier` remain normal components |
| Scheduled work | Stable Actor timer ownership | Matching ticks and room frame forwarding remain ActorTimer callbacks |

For example, changing user Save SQL replaces behavior on the next generation
while preserving the PostgreSQL pool. Changing Redis read logic preserves the
connection and key prefix. Editing Start's connection string selection does
not reconnect a running node; that requires restart. The latest Redis Store
closes the connection created by the first generation through the same State.

Persistence tests use real PostgreSQL/Redis with isolated schemas and prefixes.
The dedicated three-node E2E verifies data/gateway/battle role behavior and real
client traffic. In-process framework hosts use the same component composition,
State discovery, validation and lifecycle implementation as DLL hosting.

## Upgrade from application modules

This is a breaking change with no compatibility adapter. `ILakonaModule`, its
context and discovery/runtime path have been removed. Move stable registrations
to the Hotfix component's `ComponentConfigureServices`; move business initialization
and teardown to its hooks. Move cross-generation data into an optional marked State. Replace Store
interface injection with concrete component injection and migrate tests which
overrode components. Rebuild App and Hotfix together and restart the node.

Compile-time diagnostics: `LAKONA20061` covers component/hook/injection
and registration contracts, `LAKONA20062` State declarations and unmarked injected
App state writes, and `LAKONA20063` direct ownership violations.
`LAKONA20057` remains the component dependency-cycle diagnostic;
custom registration cannot downgrade a forbidden component graph to a warning.
