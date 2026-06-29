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
  commands, actor ticks, local actors, feature metadata, and hotfix feature
  services.
- Instance command methods implement feature-addressed commands and use
  constructor DI.
- Command methods receive `HotfixFeatureCommandCall<TRequest>` for
  request-specific context.

`IFeatureMessageHandler` remains a low-level framework adapter and advanced
escape hatch. It should not appear in generated project code, sample business
code, or ordinary authoring documentation.

## Feature Class Lifecycle

Feature classes have two phases:

1. Declaration phase.
2. Command invocation phase.

During declaration, the scanner must not require a public parameterless
constructor and must not instantiate the feature through its runtime
constructor. The scanner invokes a public static `Configure(HotfixFeatureContext
context)` method on the feature type and records the resulting declarations in
`HotfixFeatureDeclaration`.

The existing instance `override Configure(HotfixFeatureContext context)` shape
should be replaced rather than kept as a parallel authoring path. Lakona is
early enough that the cleaner model is worth the breaking change, and keeping
both forms would preserve the same lifecycle ambiguity this design removes.

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
3. Resolves the current hotfix command by `(FeatureName, FeatureCommandId)`.
4. Deserializes the request payload with the configured feature message
   serializer.
5. Builds `HotfixFeatureCommandCall<TRequest>`.
6. Invokes the command method through the current hotfix dispatch table.
7. Serializes the reply DTO.
8. Maps framework failures to `FeatureMessageReply` status values.

Business code returns a reply DTO or throws. It does not construct
`FeatureMessageReply` for normal command outcomes.

## Error Model

Framework failures use `ClusterSendStatus`:

- unknown feature or command: `FeatureNotFound`;
- expired request: `Expired`;
- missing handler method or invalid method shape during validation: reload
  failure, previous hotfix generation remains active;
- request serialization or deserialization failure:
  `DeserializationFailed`;
- reply serialization failure: `SerializationFailed`;
- command cancellation: cancellation when caller cancellation is active,
  otherwise `Failed`;
- unhandled command exception: `Failed` with diagnostic message.

Business rejection should be modeled in the reply DTO. For example,
`AllocateRoomReply` should include `Succeeded` and `Message` rather than
returning `ClusterSendStatus.Rejected` for ordinary room-capacity or rule
failures. `Rejected` remains available for framework-level admission failures
where the request cannot be interpreted as a valid command.

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
4. Replace `HotfixFeatureMessageHandler` fan-out over
   `IEnumerable<IFeatureMessageHandler>` with framework dispatch into the
   current hotfix command table.
5. Keep `IFeatureMessageHandler` available for low-level framework and advanced
   tests, but remove it from generated project and sample business code.
6. Migrate Agar:
   - move `BattleRuntimeFeatureMessageHandler.HandleAsync` into
     `BattleRuntimeFeature.AllocateRoomAsync`;
   - move `StateStoreFeatureMessageHandler` command handling into
     `StateStoreFeature`;
   - replace manual JSON and `FeatureMessageReply` handling with typed request
     and reply DTOs.
7. Update documentation in `docs/cluster.md`, `docs/configuration.md`,
   `docs/hotfix/architecture.md`, and `docs/hotfix/actor-behavior.md`.
8. Update generated templates so starter features use static `Configure` and
   no feature message handler class.

## Compatibility Decision

This is a breaking hotfix authoring change:

- feature classes no longer need a public parameterless constructor;
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
- command declarations validate instance method shape;
- command declarations validate constructor activation;
- duplicate `(feature, command id)` fails reload;
- framework feature message adapter dispatches to the current hotfix feature
  command;
- failed hotfix reload keeps previous command dispatch active;
- Agar remote room allocation works without
  `BattleRuntimeFeatureMessageHandler`;
- generated starter output contains no user-authored
  `IFeatureMessageHandler`.
