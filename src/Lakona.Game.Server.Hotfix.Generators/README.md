# Lakona.Game.Server.Hotfix.Generators

Internal Roslyn analyzer assembly for Lakona.Game server Hotfix authoring.
Consumers do not install this project as a package. The matching analyzer is
delivered by `Lakona.Game.Server`.

Source generators for Lakona.Game server Hotfix behaviors and generated RPC
and Application HTTP service binding.

Public instance methods in sealed partial `[HotfixBehaviorOf]` classes define the actor API.
Stable App projects own actor state, actor identity, and actor DTOs. Hotfix
projects own the generated `ActorAccess` root and readonly selectors that
expose those methods to service and lifecycle code. The generator does not
emit one plural collection class per actor. Stable runtime services, actor
metadata, and the stable cluster handler provide the cross-node dispatch
boundary for route lookup, local dispatch, remote dispatch, serialization, and
actor-call error mapping.

`[ActorMethod("stable-name")]` separates a method's wire identity from its C#
name; generated method keys and ids use the explicit wire name while selectors
continue to bind the C# symbol. `[ActorIgnore]` removes public composition
helpers before remote method-shape validation and generation. Diagnostics
`LKNHOTFIX046` and `LKNHOTFIX047` reject empty wire names and conflicting
`[ActorMethod]`/`[ActorIgnore]` declarations respectively.

Generated remote calls retain their compile-time request and result types.
The stable runtime closes typed MemoryPack codecs when a Hotfix snapshot is
published, then writes Actor headers and DTOs directly into the owned cluster
RPC envelope buffers. Per-call reflection, dynamic serializer dispatch, copied
payload arrays, and general `ClusterMessage` wrapping are not part of the
generated Actor path.

Non-public fields and properties declared by an `Actor<TKey>` are owned by the
actor's unique `[HotfixBehaviorOf]` class. Diagnostic `LKNHOTFIX031` rejects
direct access from other classes, including other code in the Hotfix assembly.
Explicitly public actor state remains available to normal callers. Stable App
projects should keep actor state `internal` and grant their Hotfix assembly
`InternalsVisibleTo`; the analyzer supplies the finer type-level restriction
that C# does not provide.

Attributed hotfix modules may retain only private readonly dependencies assigned
directly from their activation constructor. Diagnostic `LKNHOTFIX032` rejects
generation-owned counters, caches, collections, properties, and events. Actor
and timer calls use direct static method selectors, so implementation symbols
remain navigable while no hotfix method delegate or callback name crosses the
stable runtime boundary. Diagnostic `LKNHOTFIX040` rejects captures and
indirect selector expressions.

Hotfix projects set `<LakonaProjectRole>Hotfix</LakonaProjectRole>`. In that
closed project role, every user-defined class must declare a framework
role such as `[HotfixService]`, `[HotfixLifecycle]`, `[HotfixBehaviorOf]`,
`[HotfixTimer]`, or `[HotfixComponent]`. Diagnostic `LKNHOTFIX037` rejects
unclassified classes so DTOs and state cannot accidentally remain in the
collectible assembly. `[HotfixComponent]` classes are generated as
generation-scoped singleton registrations and remain subject to
`LKNHOTFIX032`. Abstract/data base classes therefore belong in stable
assemblies. Pure static utilities are allowed, but `LKNHOTFIX038` rejects
static fields, auto-properties, and events that create hidden state roots.

Stable App projects set `<LakonaProjectRole>ServerApp</LakonaProjectRole>`.
Both the RPC generator and this generator derive their stable generated
namespace from `$(RootNamespace).Generated`, so binder references and Hotfix
service proxies cannot drift into different namespaces.

For server app projects, the generator emits RPC service binders, required hotfix contract providers, and service-scoped call contexts such as `ChatServiceCall<TRequest>`. `LakonaGameServer.RunAsync` discovers the binders automatically from the application assembly; generated projects do not call a builder extension. Generated proxies construct the service-scoped readonly call wrapper, pass the active RPC connection id, and route calls through the hotfix dispatcher. When a service declares a notification contract, its generated call exposes a strongly typed `Callback` property without repeating the callback type in every handler signature. Generated endpoint binders make hotfix-backed services visible to `Lakona:Endpoints[]:RpcServices` validation; service names come from `ApiName` when set, otherwise from the RPC interface name such as `IChatService` -> `chat`. Hand-written service marker files are no longer part of generated projects.

Hotfix projects may declare complete Application HTTP classes with
`[LakonaHttpService("service-name")]` and annotate handlers with
`[LakonaHttpEndpoint(method, route)]`. The generator rejects invalid handler
shapes, duplicate service names, duplicate routes, and reserved management
routes. Handlers accept `LakonaHttpCall` and return
`ValueTask<LakonaHttpResponse>`; application code does not declare numeric HTTP
method ids or a parallel stable interface. The call contains a bounded request
snapshot detached from `HttpContext` and generation-scoped services; handlers
treat snapshot values as read-only and observe cooperative cancellation.
