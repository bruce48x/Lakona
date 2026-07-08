# Actor Hosting Without Features Design

Date: 2026-07-08

Status: draft for user review.

## Purpose

Lakona's current Feature model carries too many responsibilities: startup
composition, cluster discovery, lifecycle hooks, service registration,
feature-addressed command dispatch, and deployment topology. This makes users
learn a framework concept that is broader than the game concepts they are
actually trying to express.

This design removes Feature as a user-facing model. The replacement is
actor-first:

- configuration declares which actor kinds a node may host and which named
  startup actor declarations to activate on that node;
- hotfix code declares actor lifecycle hooks and actor placement selectors;
- the cluster directory advertises actor host capability descriptors;
- actor placement and route-directory ownership replace feature discovery and
  feature command dispatch.

The ordinary user model becomes:

- Node: one process and its deployment identity.
- Endpoint / RpcService: client-facing transport and RPC exposure.
- Actor: state, behavior, lifecycle, and cluster placement.
- Hotfix: reloadable business behavior and startup declarations.

Feature is not a public authoring concept in this model.

## Large Change Scope Checkpoint

This is a large cross-cutting change.

Goal: replace Feature startup, discovery, and command dispatch with actor host
configuration, hotfix actor lifecycle attributes, startup actor declarations,
and user-registered actor placement selectors.

Affected surfaces:

- `Lakona.Game.Server` public APIs for feature startup, actor hosting, remote
  actor creation, route lookup, runtime validation, diagnostics, and server
  configuration.
- `Lakona.Game.Cluster` node directory descriptors and actor host discovery.
- `Lakona.Game.Server.Hotfix.Abstractions` authoring APIs for feature
  descriptors, actor lifecycle hooks, and hotfix startup registration.
- `Lakona.Game.Server.Hotfix` scanning, reload, lifecycle coordination, command
  dispatch, and rollback behavior.
- `Lakona.Game.Server.Hotfix.Generators` generated actor accessors, scanner
  diagnostics, and analyzer rules.
- `Lakona.Tool` generated templates and generated `appsettings.json`.
- `samples/Game.Unity.Agar` and `samples/Game.Godot.Chat` server hotfix code,
  Docker Compose files, docs, and tests.
- Current docs under `docs/cluster.md`, `docs/configuration.md`,
  `docs/hotfix/**`, `docs/actor.md`, and tool docs.

Coupling assessment:

- Actor placement, route-directory registration, remote actor creation, hotfix
  reload, and generated actor clients are strongly coupled and should stay under
  one continuity-preserving implementation owner.
- Documentation cleanup, source scans for stale Feature terminology, and final
  sample wording are independent after the runtime contract compiles.
- Template migration can be a separate slice after the generator/runtime API
  shape is stable.

Compatibility stance:

- Breaking changes are acceptable. Lakona is early in development, and keeping
  a compatibility-preserving Feature layer would preserve the main cognitive
  burden this design removes.
- Temporary internal shims may be used during migration, but final user-facing
  docs, generated code, and sample code must not use Feature.

Validation plan:

- Build `Lakona.slnx`.
- Run affected test projects for game server, cluster, hotfix, hotfix
  generators, and tooling.
- Run source scans to reject stale user-facing Feature APIs and docs.
- Run Agar business logic tests and the dedicated Agar E2E smoke script after
  sample migration.
- Run generated project tests to verify template output uses `ActorHosts`,
  `StartupActors`, actor lifecycle attributes, and actor placement APIs.

Versioning impact:

- Any implementation that modifies shippable source under `src/**` must bump
  affected package versions according to `CONTRIBUTING.md`.
- This design document is docs-only and does not require package version bumps.

## Problems With Feature

The current Feature model overloads one concept with several unrelated jobs:

- local dependency registration;
- stable process lifecycle hooks;
- reloadable hotfix feature descriptors;
- cluster-discoverable node capabilities;
- feature-addressed command handlers;
- topology selection through `Lakona:Feature`;
- business names such as `matchmaking`, `state-store`, and `battle-runtime`.

This makes simple questions hard:

- Is a Feature a business feature, a node capability, or a lifecycle module?
- Does `Lakona:Feature=[]` mean this node does nothing, even if it exposes RPC
  services?
- Should actor creation happen through Feature commands or actor APIs?
- Which parts reload with hotfix and which parts are stable process lifecycle?

The answer should be actor-first. Users create actors, call actors, start fixed
actors, and configure which nodes may host actor kinds.

## Design Principles

- Do not replace Feature with another broad container concept.
- Configuration controls deployment topology, not business identity rules.
- Code controls actor identity, placement strategy, and lifecycle behavior.
- Actor host discovery is specific: a node can host a named actor kind.
- Actor placement is only used before an actor has an owner route.
- Existing actor routes win over placement selection.
- Startup actor declarations create actors; actor lifecycle hooks perform
  actor-specific initialization and cleanup.
- User-registered placement selectors must be constrained enough for the
  framework to enforce single-owner actor semantics.

## Configuration

### Actor Hosts

`Lakona:ActorHosts` declares which actor kinds the current node may host.

Example:

```json
{
  "Lakona": {
    "Node": {
      "Id": "battle-1"
    },
    "ActorHosts": [ "room" ]
  }
}
```

Rules:

- Actor host names are actor names, not feature names.
- Actor names are generated from `[ActorName]` or the existing actor naming
  convention.
- Unknown actor host names fail startup.
- Duplicate actor host names fail startup.
- Omitted `ActorHosts` means this node does not host application actors by
  default.
- No "all actors" or omitted-means-all behavior exists.

The node directory publishes ready actor host descriptors for the active node:

```txt
NodeActorHostDescriptor
  ActorName
  PolicyHash
  BuildTag
  Metadata
```

Metadata must stay low-cardinality. Region, pool, tier, and capacity bucket are
acceptable. Per-player, per-room, request, or actor id values are not.

### Startup Actors

`Lakona:StartupActors` activates named startup actor declarations on the current
node. It does not contain raw actor ids.

Example:

```json
{
  "Lakona": {
    "Node": {
      "Id": "data-1"
    },
    "ActorHosts": [ "matchmaking", "leaderboard", "user" ],
    "StartupActors": [ "matchmaking", "leaderboard" ]
  }
}
```

Rules:

- Startup actor names are declaration names registered by hotfix code.
- Unknown startup actor names fail startup.
- Duplicate startup actor names fail startup.
- Omitted `StartupActors` means this node starts no fixed application actors.
- A startup actor declaration may create one or more concrete actors.
- The declaration owns actor ids and multiplicity.
- If a startup declaration creates actor kind `room`, the current node must also
  list `room` in `ActorHosts`; otherwise startup fails.
- There is no `AllHosts` startup or placement mode. To start an actor on a node,
  list the startup declaration in that node's own configuration.

If a startup declaration needs deployment parameters, the configuration entry
may use an object shape:

```json
{
  "Name": "matchmaking-shards",
  "Options": {
    "Count": 8
  }
}
```

The declaration binds `Options` to a strongly typed options object and uses
code-owned identity rules to derive actor ids. Configuration supplies deployment
scale, not raw actor ids.

## Hotfix Startup Surface

Hotfix code uses a package-level startup surface instead of Feature classes.

Example:

```csharp
public static class HotfixStartup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<MatchmakingNotifier>();
        services.TryAddSingleton<RoomNotifier>();
    }

    public static void ConfigureActors(ActorHostBuilder actors)
    {
        actors.RegisterStartup(
            "matchmaking",
            static context => ActorStartupPlan.Create<MatchmakingActor>(
                ActorId.From("default")));

        actors.RegisterStartup(
            "leaderboard",
            static context => ActorStartupPlan.Create<LeaderboardActor>(
                ActorId.From("global")));

        actors.RegisterPlacement<RoomActor, RoomId>(
            static context => SelectRoomHost(context.Candidates, context.Key));
    }

    private static ActorHostCandidate SelectRoomHost(
        IReadOnlyList<ActorHostCandidate> candidates,
        RoomId roomId)
    {
        var index = StableHash(roomId.Value) % candidates.Count;
        return candidates[index];
    }
}
```

Rules:

- `ConfigureServices` registers hotfix-generation services only.
- `ConfigureActors` registers startup declarations and actor placement
  selectors only.
- Startup registration returns data. It must not perform network, database, or
  actor runtime I/O.
- Placement selector registration provides a pure selection function. It must
  not receive an `IServiceProvider`.
- The scanner rejects missing, duplicate, or invalid startup declaration names.
- The scanner rejects duplicate placement selectors for the same actor kind.

The final implementation may choose a different type name for `ActorHostBuilder`
or `ActorStartupPlan`, but the responsibilities remain the same.

## Actor Lifecycle Attributes

Actor lifecycle moves to actor behavior methods.

Example:

```csharp
[HotfixBehaviorOf(typeof(MatchmakingActor))]
public static partial class MatchmakingBehavior
{
    [ActorStart]
    public static async ValueTask StartAsync(
        MatchmakingActor self,
        ActorStartCall call)
    {
        await self.StartTimerAsync(
            new MatchmakingTimerStartRequest(),
            call.CancellationToken).ConfigureAwait(false);
    }

    [ActorStop]
    public static async ValueTask StopAsync(
        MatchmakingActor self,
        ActorStopCall call)
    {
        await self.StopTimerAsync(
            new MatchmakingTimerStopRequest(),
            CancellationToken.None).ConfigureAwait(false);
    }
}
```

Rules:

- `[ActorStart]` and `[ActorStop]` are method attributes on
  `[HotfixBehaviorOf(typeof(TActor))]` classes.
- Each actor can declare at most one start method and one stop method.
- The first parameter must be the actor type from `HotfixBehaviorOf`.
- The second parameter must be `ActorStartCall` or `ActorStopCall`.
- Return type must be `ValueTask`.
- Lifecycle methods run inside the actor turn.
- Actor creation fails and rolls back if `[ActorStart]` fails.
- Actor stop logs cleanup failures and continues shutdown according to the actor
  runtime stop contract.
- Stop cleanup that must run should use a noncancelable cleanup token.

Actor lifecycle hooks are hotfix behavior. They reload with the hotfix
generation. Existing running actors keep their identity and route; behavior
method resolution follows the current hotfix generation according to the actor
runtime dispatch model.

## Actor Placement API

Generated actor collections expose placement for actors that have registered
placement selectors.

Example:

```csharp
await rooms.Place(roomId)
    .CreateAsync(createRoom, cancellationToken)
    .ConfigureAwait(false);
```

Placement is only used when no owner route exists for the actor id. Normal actor
calls use the route directory.

Create / ensure flow:

1. Check the route directory for an existing `(ActorName, ActorId)` owner.
2. If an owner exists, use that route.
3. Query ready nodes that advertise a matching `NodeActorHostDescriptor`.
4. Filter candidates to descriptors with compatible `PolicyHash` and `BuildTag`.
5. Sort candidates by `NodeId` using ordinal comparison.
6. Invoke the registered placement selector.
7. Reject a selector result that is not one of the candidate nodes.
8. Send an internal actor host create / ensure request to the selected node.
9. The target creates the actor and registers the route through a single-owner
   compare-and-set path.
10. If route registration reports an existing owner, discard the duplicate
    creation attempt and route to the existing owner.

The selector contract is intentionally constrained:

```csharp
public readonly struct ActorPlacementContext<TKey>
{
    public TKey Key { get; }
    public IReadOnlyList<ActorHostCandidate> Candidates { get; }
}
```

Selector rules:

- The selector is synchronous.
- The selector is expected to be deterministic for persistent actors.
- The selector must not perform I/O.
- The selector may use low-cardinality candidate metadata.
- The selector must return a candidate from `Candidates`.
- Exceptions fail placement with a structured error.

The framework cannot prove a selector is pure, but the API should make impure
selectors awkward by not passing services, configuration roots, or async hooks.

## Route Policy Consistency

Each actor placement selector receives a generated `PolicyHash`.

The hash should include:

- actor name;
- selector registration identity;
- placement key type identity;
- current hotfix BuildTag or equivalent generation identity;
- metadata schema used by the selector, when declared.

Nodes publish the policy hash for actor kinds they can host. Placement callers
filter candidates by the policy hash of their current hotfix generation. A mixed
cluster with different placement policies fails placement instead of silently
choosing divergent owners.

Existing actor routes remain authoritative even if the placement policy changes.
Policy changes affect only future create / ensure operations for actors without
routes.

## Replacing Feature Commands

Feature commands disappear from user-facing authoring.

Current examples migrate as follows:

- State-store user actor creation:
  - old: discover `state-store`, send `CreateUserActorRequest`;
  - new: `users.Place(userId).EnsureAsync(...)`.
- Battle-runtime room allocation:
  - old: discover `battle-runtime`, send `BattleRuntimeRoomAllocationRequest`;
  - new: `rooms.Place(roomId).CreateAsync(...)`, then call room behavior through
    generated actor refs.
- Matchmaking and leaderboard fixed actors:
  - old: start from `MatchmakingFeature.StartAsync` and
    `LeaderboardFeature.StartAsync`;
  - new: register startup declarations and activate them through
    `Lakona:StartupActors`.

Command-specific idempotency should become actor behavior idempotency. Actor
creation remains protected by route-directory single-owner registration.

## Cluster Directory Model

Feature discovery is replaced by actor host discovery.

Node records include:

```txt
NodeRecord
  ClusterName
  NodeId
  NodeEpoch
  State
  Endpoints
  ActorHosts
  LeaseExpiresAt
```

`ActorHosts` entries are not Feature descriptors. They describe actor kinds the
node is currently allowed to host and the policy identity under which it can
host them.

Discovery answers:

> Which ready nodes can host actor kind X under policy hash Y?

It does not answer:

> Which node owns actor id X?

Actor ownership remains the route directory's responsibility.

## Startup Ordering And Rollback

Startup actor declarations run after the hotfix generation is loaded and before
client-facing listeners accept traffic for a freshly started server process.

Ordering rules:

- Runtime validates all configured startup actor names before starting any of
  them.
- Startup declarations run in configuration order.
- If a declaration creates multiple actors, its returned plan determines the
  stable per-declaration order.
- If actor creation fails, startup fails.
- Actors created by earlier startup declarations are stopped in reverse creation
  order.
- Actor start hooks participate in the same rollback path.

On hotfix reload:

- Reload does not rerun startup actor declarations that remain active on the
  node.
- Actor lifecycle behavior methods resolve through the current hotfix
  generation.
- Removing a startup declaration from configuration is a process topology
  change and requires process restart or an explicit future topology-reconcile
  feature. It is not part of this design.

## Configuration Examples

Single-node generated starter with a chat actor:

```json
{
  "Lakona": {
    "Node": {
      "Id": "dev-1"
    },
    "ActorHosts": [ "chat-room" ],
    "StartupActors": [ "chat-room" ],
    "Endpoints": [
      {
        "Transport": "websocket",
        "Serializer": "json",
        "Host": "127.0.0.1",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "chat" ]
      }
    ]
  }
}
```

Agar data node:

```json
{
  "Lakona": {
    "Node": {
      "Id": "data-1"
    },
    "ActorHosts": [ "user", "matchmaking", "leaderboard" ],
    "StartupActors": [ "matchmaking", "leaderboard" ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.1:21001",
      "Serializer": "memorypack"
    }
  }
}
```

Agar gateway node:

```json
{
  "Lakona": {
    "Node": {
      "Id": "gateway-1"
    },
    "ActorHosts": [],
    "Endpoints": [
      {
        "Transport": "websocket",
        "Serializer": "memorypack",
        "Host": "0.0.0.0",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "player" ]
      }
    ]
  }
}
```

Agar battle node:

```json
{
  "Lakona": {
    "Node": {
      "Id": "battle-1"
    },
    "ActorHosts": [ "room" ],
    "Endpoints": [
      {
        "Transport": "kcp",
        "Serializer": "memorypack",
        "Host": "0.0.0.0",
        "Port": 20001,
        "RpcServices": [ "battle" ]
      }
    ],
    "Cluster": {
      "Endpoint": "tcp://10.0.0.3:21003",
      "Serializer": "memorypack"
    }
  }
}
```

## Generated API Shape

Generated actor accessors should separate local, routed, and placement calls:

```csharp
await rooms.Local(roomId)
    .StartAsync(request, cancellationToken)
    .ConfigureAwait(false);

await rooms.Route(roomId)
    .SubmitInputAsync(input, cancellationToken)
    .ConfigureAwait(false);

await rooms.Place(roomId)
    .CreateAsync(createRoom, cancellationToken)
    .ConfigureAwait(false);
```

Meaning:

- `Local(id)` calls an actor already hosted on the current node.
- `Route(id)` calls the current route owner.
- `Place(id)` creates or ensures ownership by running the registered placement
  selector when no route exists.

Generated `Place(id)` is available only when the actor has a placement selector.

## Analyzer And Validation Rules

Analyzer and runtime validation should reject:

- `HotfixFeatureAttribute`, `HotfixGameFeature`, feature lifecycle methods, and
  feature command registration in new hotfix projects;
- `Lakona:Feature` in generated projects and new samples;
- unknown `ActorHosts` names;
- unknown `StartupActors` names;
- duplicate `ActorHosts` or `StartupActors`;
- startup declarations that create actor kinds not listed in `ActorHosts`;
- duplicate actor placement selectors;
- generated actor `Place(id)` calls for actors without placement selectors;
- placement selector return values outside the candidate set;
- mixed candidate policy hashes for an actor placement operation.

Source-scan tests should ensure generated templates, samples, and user-facing
docs do not reintroduce Feature terminology for application topology.

## Migration Milestones

1. Add actor host descriptors, configuration binding, and validation.
2. Add hotfix startup scanning for `ConfigureServices`, `ConfigureActors`,
   startup declarations, and placement selector registration.
3. Add `[ActorStart]` and `[ActorStop]` scanning, dispatch, and rollback.
4. Add internal remote actor create / ensure transport and route-directory
   compare-and-set integration.
5. Generate actor `Place(id)` accessors.
6. Migrate Agar state-store, matchmaking, leaderboard, and battle-runtime flows.
7. Migrate generated starter templates.
8. Remove public Feature APIs and feature command authoring from docs and
   samples.
9. Run source scans, affected tests, generated project tests, and Agar E2E.
10. Apply package version bumps for changed shippable packages.

## Review Gates

- Architecture review before implementation starts.
- Runtime review after actor host descriptors and placement create / ensure
  semantics compile.
- Hotfix reload and lifecycle review after `[ActorStart]` / `[ActorStop]`
  behavior is implemented.
- Generator and template review after generated actor accessors and starter
  configuration change.
- Final integration review after sample migration and source scans.

## Final Contract Summary

- Feature is removed from the public user model.
- `Lakona:ActorHosts` declares which actor kinds this node may host.
- `Lakona:StartupActors` activates named actor startup declarations on this
  node.
- Startup declarations own actor ids and multiplicity.
- Actor lifecycle is expressed with `[ActorStart]` and `[ActorStop]` behavior
  method attributes.
- Hotfix startup registers services, startup actor declarations, and actor
  placement selectors.
- Placement selector code chooses from framework-provided candidate hosts.
- Existing actor routes are authoritative.
- Route-directory compare-and-set protects single-owner actor creation.
- Policy hashes prevent mixed placement selector generations from silently
  splitting ownership.
