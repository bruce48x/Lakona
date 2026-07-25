# Lakona.Game.Server.Hotfix.Generators

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

Hotfix projects set `<LakonaHotfixProject>true</LakonaHotfixProject>`. In that
closed project role, every user-defined class must declare a framework
role such as `[HotfixService]`, `[HotfixLifecycle]`, `[HotfixBehaviorOf]`,
`[HotfixTimer]`, or `[HotfixComponent]`. Diagnostic `LKNHOTFIX037` rejects
unclassified classes so DTOs and state cannot accidentally remain in the
collectible assembly. `[HotfixComponent]` classes are generated as
generation-scoped singleton registrations and remain subject to
`LKNHOTFIX032`. Abstract/data base classes therefore belong in stable
assemblies. Pure static utilities are allowed, but `LKNHOTFIX038` rejects
static fields, auto-properties, and events that create hidden state roots.

The generator discovers `[HotfixState]` partial classes and emits generated friend accessors for private fields.

```csharp
[HotfixState]
public sealed partial class PlayerActor : Actor<PlayerId>
{
    private int exp;
}
```

Generates an editor-hidden accessor similar to:

```csharp
public int __hotfix_exp()
{
    return exp;
}
```

Types marked `[HotfixState]` must be partial. Nested hotfix state also requires partial containing types. Compiler-generated backing fields, static fields, and const fields are ignored.

Generated accessors are public by necessity: they live in the stable assembly
and must be callable from the separate hotfix assembly. They are hidden from
normal IntelliSense but are not a security boundary. `[FriendOf]` identifies the
intended Hotfix behavior relationship; it does not prevent other code with a
stable actor reference from calling generated `__hotfix_` members.

For server app projects, the generator emits RPC service binders, required hotfix contract providers, and service-scoped call contexts such as `ChatServiceCall<TRequest>`. `LakonaGameServer.RunAsync` discovers the binders automatically from the application assembly; generated projects do not call a builder extension. Generated proxies construct the service-scoped readonly call wrapper, pass the active RPC connection id, and route calls through the hotfix dispatcher. When a service declares a notification contract, its generated call exposes a strongly typed `Callback` property without repeating the callback type in every handler signature. Generated endpoint binders make hotfix-backed services visible to `Lakona:Endpoints[]:RpcServices` validation; service names come from `ApiName` when set, otherwise from the RPC interface name such as `IChatService` -> `chat`. Hand-written service marker files are no longer part of generated projects.

Stable server app interfaces may also declare
`[LakonaHttpService("service-name")]` and annotate each method with
`[LakonaHttpEndpoint(methodId, method, route)]`. The generator emits stable
ASP.NET endpoint registration and required-Hotfix-contract metadata. The
contract method accepts `LakonaHttpRequest`; its `[HotfixService]`
implementation accepts `LakonaHttpCall` and returns the same
`ValueTask<LakonaHttpResponse>`. Candidate validation rejects missing methods
and mismatched return types. The call contains a bounded request snapshot
detached from `HttpContext` and generation-scoped services; handlers treat
snapshot values as read-only and observe cooperative cancellation.
