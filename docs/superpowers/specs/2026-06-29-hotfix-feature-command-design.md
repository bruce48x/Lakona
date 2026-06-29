# Hotfix Feature Command Design

## Purpose

Lakona should make feature-addressed remote commands feel like a natural part of
the hotfix feature model. A user-authored feature should declare what capability
it provides, which commands it accepts, and how those commands are handled in
one place.

The current split between `HotfixGameFeature` and user-authored
`IFeatureMessageHandler` classes adds unnecessary concepts. The handler shape
also forces business code to inspect `FeatureMessageRequest.Feature`,
`FeatureMessageRequest.Kind`, serialize and deserialize payloads manually, and
return low-level `FeatureMessageReply` values. That is framework plumbing, not
game logic.

## Goals

- Keep `Feature` and `Actor` as separate concepts.
- Keep `HotfixGameFeature` as the user-facing capability descriptor.
- Move feature command handling into the corresponding hotfix feature class.
- Support constructor dependency injection for feature command handlers.
- Hide `IFeatureMessageHandler` from ordinary hotfix business code.
- Complete the existing `HotfixFeatureContext.HandleCommand<TRequest, TReply>`
  declaration path with runtime dispatch.
- Preserve hotfix unload safety by not retaining feature instances across
  reloads.

## Non-Goals

- Do not merge Feature with Actor.
- Do not route feature commands through actor mailboxes by default.
- Do not introduce durable pub/sub, topics, consumer groups, or stream offsets.
- Do not make feature command handlers long-lived runtime objects.
- Do not require generated projects or samples to hand-write cluster message
  handlers.

## User Model

A feature class owns both declaration and command handling:

```csharp
[HotfixFeature("battle-runtime")]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    private readonly IActorLifecycle lifecycle;
    private readonly IActorDirectory directory;
    private readonly IActorDirectoryCache directoryCache;
    private readonly LocalActorNodeIdentity localNode;
    private readonly RoomActors rooms;
    private readonly ILogger<BattleRuntimeFeature> logger;

    public BattleRuntimeFeature(
        IActorLifecycle lifecycle,
        IActorDirectory directory,
        IActorDirectoryCache directoryCache,
        LocalActorNodeIdentity localNode,
        RoomActors rooms,
        ILogger<BattleRuntimeFeature> logger)
    {
        this.lifecycle = lifecycle;
        this.directory = directory;
        this.directoryCache = directoryCache;
        this.localNode = localNode;
        this.rooms = rooms;
        this.logger = logger;
    }

    public static void Configure(HotfixFeatureContext context)
    {
        context.Metadata["region"] = "cn-east";
        context.HandleCommand<AllocateRoomCommand, AllocateRoomReply>(
            nameof(AllocateRoomAsync));

        context.ScheduleActiveActorTicks<RoomActor>(
            TimeSpan.FromMilliseconds(50),
            TickBacklogPolicy.SkipIfPending);
    }

    public async ValueTask<AllocateRoomReply> AllocateRoomAsync(
        HotfixFeatureCommandCall<AllocateRoomCommand> call)
    {
        // Placement, route registration, actor creation, and local actor calls.
    }
}
```

The public authoring model is:

- `HotfixFeatureAttribute` names the capability.
- A public static `Configure(HotfixFeatureContext context)` method declares
  discoverability, metadata, commands, actor ticks, local actors, and hotfix
  feature services.
- Instance command methods implement feature-addressed commands and use
  constructor DI.
- Command methods receive `HotfixFeatureCommandCall<TRequest>` for
  request-specific context.

`IFeatureMessageHandler` remains a stable low-level cluster/RPC adapter and
test boundary. It should not appear in generated project code, sample business
code, hotfix feature declarations, or ordinary authoring documentation.

## Feature Class Lifecycle

Feature classes have two phases:

1. Declaration phase.
2. Command invocation phase.

During declaration, the scanner must not require a public parameterless
constructor and must not instantiate the feature through its runtime
constructor. The scanner invokes a public static `Configure(HotfixFeatureContext
context)` method on the feature type and records the resulting declarations in
`HotfixFeatureDeclaration`.

`HotfixGameFeature` becomes a marker base class for hotfix feature types. It no
longer declares instance `Discoverable`, `Metadata`, or `Configure` members.
The declaration data moves into `HotfixFeatureContext`:

```csharp
public sealed class HotfixFeatureContext
{
    public bool Discoverable { get; set; } = true;
    public IDictionary<string, string> Metadata { get; }
    public IServiceCollection Services { get; }
}
```

The scanner records `context.Discoverable` and a string-copy of
`context.Metadata` into `HotfixFeatureDeclaration`. Feature metadata remains
low-cardinality operational metadata and must not include per-player,
per-session, per-room, or request-specific values.

The existing instance `override Configure(HotfixFeatureContext context)` shape
should be replaced rather than kept as a parallel authoring path. Lakona is
early enough that the cleaner model is worth the breaking change, and keeping
both forms would preserve the same lifecycle ambiguity this design removes.
Feature types that define an instance `Configure(HotfixFeatureContext)` method
should fail scanner validation with a diagnostic that points to the static
declaration shape.

During command invocation, the runtime activates a fresh feature instance from
the current hotfix provider with `ActivatorUtilities`, invokes the selected
command method, and disposes the instance after the returned `ValueTask`
completes.

This mirrors hotfix service instance activation: constructor dependencies come
from the current hotfix generation provider with fallback to stable root
services, and instances are not retained across calls or reloads.

## Command Call Context

Add a hotfix command context:

```csharp
public sealed class HotfixFeatureCommandCall<TRequest> : IHotfixCallContext
{
    public TRequest Request { get; }
    public string FeatureName { get; }
    public FeatureCommandId CommandId { get; }
    public string CorrelationId { get; }
    public NodeId SourceNode { get; }
    public DateTimeOffset ExpiresAt { get; }
    public CancellationToken CancellationToken { get; }
    public IServiceProvider Services { get; }
}
```

The context carries per-request data. Constructor dependencies remain the
preferred way to receive ordinary services. `Services` exists for framework
integration and rare dynamic cases, not as the normal dependency pattern.

## Dispatch Rules

`HotfixFeatureContext.HandleCommand<TRequest, TReply>(methodName)` declares one
feature command. `TRequest` must declare `[FeatureCommand(id)]`.

The scanner records:

- feature name;
- feature type;
- request type;
- reply type;
- command id;
- method name.

The dispatch table validates each command declaration:

- The target method must exist on the declaring feature type.
- The target method may be an instance method or a static method.
- The normal documented shape is an instance method:

```csharp
ValueTask<TReply> MethodName(HotfixFeatureCommandCall<TRequest> call)
```

- A static method with the same shape is allowed as an advanced low-allocation
  path.
- Void-returning feature commands are out of scope for the first version; use
  an explicit empty reply DTO.
- Duplicate `(feature name, command id)` declarations fail hotfix validation.

At runtime, the framework-owned feature message adapter:

1. Receives `FeatureMessageRequest`.
2. Rejects expired messages before hotfix dispatch.
3. Parses `FeatureMessageRequest.Kind` into `FeatureCommandId`.
4. Resolves the current hotfix command by `(FeatureName, FeatureCommandId)`.
5. Deserializes the request payload with the configured feature message
   serializer.
6. Builds `HotfixFeatureCommandCall<TRequest>`.
7. Invokes the command method through the current hotfix dispatch table.
8. Serializes the reply DTO.
9. Maps framework failures to `FeatureMessageReply` status values.

Business code returns a reply DTO or throws. It does not construct
`FeatureMessageReply` for normal command outcomes.

Typed feature commands use the invariant-culture decimal representation of
`FeatureCommandId` as the wire `Kind`. Blank values, non-integer values, zero,
negative values, and overflow values are invalid typed command ids and return
`ClusterSendStatus.Rejected` before payload deserialization. Legacy string
command kinds are not supported by the typed feature-command path.

The default stable cluster endpoint binds one framework-owned
`IFeatureMessageHandler` that dispatches typed hotfix feature commands. It does
not fan out through `IEnumerable<IFeatureMessageHandler>` from the hotfix
provider. Advanced stable hosts may replace the default handler at the stable
cluster/RPC boundary, but then they own the whole low-level feature-message
surface. There is no mixed precedence between typed hotfix commands and
hotfix-registered low-level handlers.

## Error Model

Framework failures use `ClusterSendStatus`:

- unknown feature or command: `FeatureNotFound`;
- expired request: `Expired`;
- missing handler method or invalid method shape during validation: reload
  failure, previous hotfix generation remains active;
- request serialization or deserialization failure:
  `DeserializationFailed`;
- reply serialization failure: `SerializationFailed`;
- command cancellation with `HotfixFeatureCommandCall<TRequest>.CancellationToken`
  requested: propagate cancellation to the local RPC/caller path;
- `OperationCanceledException` when the command cancellation token is not
  requested: `Failed`;
- unhandled command exception: `Failed` with diagnostic message.

Business rejection should be modeled in the reply DTO. For example,
`AllocateRoomReply` should include `Succeeded` and `Message` rather than
returning `ClusterSendStatus.Rejected` for ordinary room-capacity or rule
failures. `Rejected` remains available for framework-level admission failures
where the request cannot be interpreted as a valid command.

Expiration remains separate from cancellation. An expired request returns
`Expired` even if the caller token is not canceled. A caller-canceled request
should stop before dispatch when possible, and a command that observes
`call.CancellationToken` should throw `OperationCanceledException` rather than
returning a business failure DTO.

## Dependency Injection

Feature command instance activation follows the same rule as hotfix service
activation:

- Constructor dependencies resolve from the current hotfix provider.
- The hotfix provider falls back to stable root services.
- The feature instance is disposed after the command completes if it implements
  `IDisposable` or `IAsyncDisposable`.
- Validation should attempt to activate feature types with instance command
  methods so constructor problems fail hotfix reload.

Feature declaration must not require constructor DI. Static `Configure` is the
declaration boundary.

## Interaction With Actors

Feature commands are the boundary for capability-level work:

- selecting or verifying the owning node;
- registering actor routes;
- creating local actors;
- checking capacity and idempotency;
- orchestrating the first calls into newly created actors.

Once a concrete actor exists, ordinary business behavior should use generated
actor refs such as `rooms.Get(id)`, `rooms.Local(id)`, and `rooms.Remote(node,
id)`.

This preserves the existing distinction:

```txt
Feature command: who can admit and place this capability-level command?
Actor call: where is this concrete state object currently owned?
```

## Migration Plan

1. Add `HotfixFeatureCommandCall<TRequest>`.
2. Change hotfix feature scanning to use static `Configure` instead of
   parameterless feature construction.
3. Extend `HotfixDispatchTable` with feature command bindings and validation.
4. Add `Discoverable` and `Metadata` declaration state to
   `HotfixFeatureContext`, and remove instance declaration members from
   `HotfixGameFeature`.
5. Replace `HotfixFeatureMessageHandler` fan-out over
   `IEnumerable<IFeatureMessageHandler>` with framework dispatch into the
   current hotfix command table.
6. Keep `IFeatureMessageHandler` available only for the stable low-level
   cluster/RPC boundary and tests, and remove it from generated project and
   sample business code.
7. Define typed command wire `Kind` parsing as invariant-culture decimal
   `FeatureCommandId` parsing, with invalid values mapped to `Rejected`.
8. Migrate Agar:
   - move `BattleRuntimeFeatureMessageHandler.HandleAsync` into
     `BattleRuntimeFeature.AllocateRoomAsync`;
   - move `StateStoreFeatureMessageHandler` command handling into
     `StateStoreFeature`;
   - replace manual JSON and `FeatureMessageReply` handling with typed request
     and reply DTOs.
9. Update the affected sample and generated-project `BuildTag` values because
   the hotfix-visible dispatch and authoring boundary changes.
10. Bump versions for every modified shippable package under `src/**`, and
   update package release constants or templates when generated output depends
   on the new model.
11. Update documentation in `docs/cluster.md`, `docs/configuration.md`,
   `docs/hotfix/architecture.md`, and `docs/hotfix/actor-behavior.md`.
12. Update generated templates so starter features use static `Configure` and
   no feature message handler class.

## Compatibility Decision

This is a breaking hotfix authoring change:

- feature classes no longer need a public parameterless constructor;
- feature discoverability and metadata are declared through
  `HotfixFeatureContext` instead of instance properties;
- feature declarations move from instance override `Configure` to public static
  `Configure`;
- ordinary business code no longer implements `IFeatureMessageHandler`;
- low-level `FeatureMessageReply` remains in cluster APIs for framework
  plumbing and advanced tests, but the documented server authoring model uses
  typed command request and reply DTOs.

## Testing Requirements

Add focused tests for:

- scanner accepts feature classes with constructor dependencies when they have
  static `Configure`;
- scanner rejects missing or malformed static `Configure`;
- scanner rejects old instance `Configure(HotfixFeatureContext)` declarations;
- static declarations preserve discoverability and metadata;
- command declarations validate instance method shape;
- command declarations validate constructor activation;
- duplicate `(feature, command id)` fails reload;
- invalid wire `Kind` values return `Rejected`;
- caller cancellation before dispatch and during command execution follows the
  documented cancellation path;
- expiration remains distinct from cancellation;
- framework feature message adapter dispatches to the current hotfix feature
  command;
- stable low-level `IFeatureMessageHandler` replacement does not mix with
  hotfix command-table dispatch;
- failed hotfix reload keeps previous command dispatch active;
- Agar remote room allocation works without
  `BattleRuntimeFeatureMessageHandler`;
- generated starter output contains no user-authored
  `IFeatureMessageHandler`.
