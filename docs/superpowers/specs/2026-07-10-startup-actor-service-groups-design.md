# Startup Actor Service Groups Design

## Status

Active. The design is approved and implementation resumed on 2026-07-11.

## Problem

The current Startup Actor model requires two declarations for one intent:

- hotfix code registers a startup name and constructs an explicit `ActorId`;
- `Lakona:StartupActors` configuration selects which registered names start on
  a node.

Business code must then know the same actor id to call the actor. This exposes
framework lifecycle identity, permits code/config drift, and does not model
multiple healthy replicas of a logical startup service.

Matchmaking and leaderboard are not global singleton actors. Multiple capable
nodes may each start one local replica. Calls select a healthy replica through
a user-defined policy. If a replica fails, another replica may receive new
requests; in-memory state is not replicated and may be lost.

## Goal

Provide one Startup Actor service group per actor type. Users declare startup
and selection once in hotfix code, access it through a generated keyed
`Startup(key)` ref, and never construct its internal actor id. The key supplies
routing affinity to the fixed selector; it does not identify a physical actor
or create independent actor state.

## Non-Goals

- No state replication, leader election, consensus, or queue recovery.
- No multiple named Startup groups for one actor type.
- No per-call selection-policy override.
- No built-in preferred, random, round-robin, or hash policy enum.
- No second application component model disguised as DI services.

Applications that need several logical groups of one shape should use keyed
actors or distinct actor types.

## Public Authoring Model

Hotfix startup registration becomes:

```csharp
[HotfixStartup]
public static class GameHotfixStartup
{
    [HotfixConfigureActors]
    public static void ConfigureActors(ActorHostBuilder actors)
    {
        actors.RegisterStartup<MatchmakingActor, MatchmakingQueueId>(
            static context => context.Candidates
                .OrderBy(candidate => StableHash(context.Key, candidate.NodeId))
                .First());

        actors.RegisterStartup<WorkerActor, WorkAffinityKey>(
            static context => context.Candidates[
                Random.Shared.Next(context.Candidates.Count)]);
    }
}
```

The selector is fixed at registration. The framework defines only the selector
contract and validates its result; selection policy belongs to the user.
`StableHash` above is an application-owned function, not a framework helper.

Generated actor collections expose:

```csharp
await matchmaking.Startup(new MatchmakingQueueId("default")).CallAsync(
    MatchmakingBehavior.EnqueueAsync,
    request,
    cancellationToken);
```

`Startup` requires a typed selection key but no group name, actor id, or
strategy argument. `TKey` is available as `context.Key` when the registered
selector runs. A stable-hash selector can therefore keep the same logical
queue, tenant, region, or other affinity key on the same healthy replica while
the candidate set is unchanged.

The key is selection input only. Different keys may select the same physical
replica and share that replica's actor state. Applications that require one
isolated state instance per key must use normal keyed actors instead.

## Configuration Model

`Lakona:StartupActors` is removed.

The two remaining declarations have separate responsibilities:

- `RegisterStartup<TActor, TKey>(selector)` says the actor type has one logical
  Startup service group, defines the typed routing-affinity key, and defines
  how callers choose a replica.
- `Lakona:ActorHosts` says which actor types the current node may host.

Every node whose `ActorHosts` contains a registered Startup Actor type creates
one local replica. A gateway with an empty `ActorHosts` list creates none.

The removed configuration key is rejected by startup validation with an
actionable message instead of being silently ignored.

## Replica Identity

The framework derives an internal id from actor type and node id:

```txt
<actor-name>/@startup/<node-id>
```

The id is stable for a stable node id and supports diagnostics, local hosting,
and actor lifecycle. It is not exposed by generated business APIs.

## Discovery Model

Node membership explicitly advertises successfully started replicas through
`StartupActorDescriptor` entries. A descriptor contains low-cardinality data:

- actor name;
- node id and node epoch from the enclosing node record;
- hotfix BuildTag and policy hash;
- optional placement metadata already permitted by actor-host descriptors.

Actor-host capability and ready Startup replica are distinct. A node is not a
candidate until local creation and lifecycle start complete successfully.

Node registration, SQL storage, cluster RPC serialization, and in-memory node
directories preserve these descriptors. Lease expiration or node-dead state
removes the replica from candidate queries without a separate service registry.

## Selection and Invocation

The framework queries ready, non-expired nodes advertising the requested
Startup Actor type and compatible hotfix metadata. Candidates are presented in
stable node-id order so deterministic selectors remain deterministic.

The registered selector receives the caller's typed key and a non-empty
read-only candidate list. The framework verifies that the returned candidate
belongs to that exact list. Selector exceptions and invalid results become
typed selection failures. Failover re-runs the same selector with the same key
and the failed candidate removed.

The chosen replica is invoked locally when owned by the current node and
through normal node-directed remote actor invocation otherwise. Startup refs
use the same behavior method ids, serializer, cancellation, and diagnostics as
keyed actor refs.

## Failover Semantics

Automatic reselection is allowed only when the framework can establish that
the behavior was not executed:

- node unavailable before acceptance;
- stale node record or node-epoch mismatch;
- route/handler unavailable before behavior dispatch.

The failed candidate is removed and the same user selector runs again against
the remaining candidates. Timeout, caller cancellation, serialization errors,
and any failure after acceptance are returned without retry to avoid duplicate
business effects.

Failover does not transfer state. A matchmaking queue held only by the failed
replica may be lost, and clients may need to enqueue again.

## Lifecycle

Startup and publication order is:

1. Load and validate the hotfix startup declarations.
2. Intersect registered Startup types with local `ActorHosts`.
3. Create the framework-derived local actor replica.
4. Run its actor-start lifecycle.
5. Publish the ready `StartupActorDescriptor` in node membership.
6. Start accepting it as a selection candidate.

Removal and shutdown reverse visibility before execution:

1. Withdraw the descriptor or mark the node non-ready.
2. Stop routing new Startup calls to the replica.
3. Drain and stop the local actor.
4. Remove its internal local identity.

Hotfix reload diffs Startup declarations. Added declarations start and publish
eligible local replicas. Removed declarations withdraw and stop replicas.
Selector-only changes affect new resolutions after the new snapshot publishes.

## Failure Types

- `StartupActorUnavailableException`: no healthy compatible replicas.
- `StartupActorSelectionException`: selector threw or returned a non-candidate.
- Existing typed actor call failures remain unchanged after a replica is
  selected.

Failures include actor type and low-cardinality status. They do not include
internal actor ids, request values, correlation ids, or user data in metrics.

## Generator Changes

For every generated actor collection, the generator emits a typed
`Startup(TKey key)` ref with the same behavior-first `CallAsync` and `PostAsync`
surface as keyed refs. The ref is usable only when a matching startup
declaration is present at runtime; otherwise invocation returns
`StartupActorUnavailableException`.

No generated lifecycle, spawn, actor-id, or per-call strategy surface is added.

## Tests

The implementation requires tests for:

1. Registration accepts one typed-key Startup declaration per actor type and
   rejects duplicates or key-type mismatches.
2. A node starts a replica only when its `ActorHosts` contains that type.
3. A successful local start publishes a descriptor; failed start does not.
4. Two nodes publish two candidates and a typed-key stable-hash selector keeps
   the same key on the same candidate while the candidate set is unchanged.
5. A clearly unavailable selected node is removed and the selector runs again
   with the original key.
6. Timeout after acceptance does not retry another replica.
7. Selector exception and outsider result produce typed failures.
8. Hotfix add/remove diffs start, withdraw, drain, and stop replicas in order.
9. Node, SQL, and MemoryPack directory adapters round-trip startup descriptors.
10. Generated source exposes `.Startup(TKey key)` and no Startup Actor id or
    per-call strategy.
11. Repository scans reject `Lakona:StartupActors` in samples, templates, docs,
    and configuration.

The Agar sample migrates matchmaking and leaderboard to `.Startup`. Focused
business tests cover one available replica. Framework integration tests cover
two replicas and failover. The final acceptance remains the existing Agar
three-node Docker plus Unity PlayMode smoke test.

## Documentation

- `docs/actor.md` documents Startup service groups versus keyed actors.
- `docs/cluster.md` documents replica advertisement and selection.
- `docs/configuration.md` removes `Lakona:StartupActors` and explains the
  intersection of hotfix Startup declarations with `ActorHosts`.
- Starter and Agar documentation use `.Startup` without actor ids.

## Compatibility and Versioning

This intentionally removes the old named startup registration and
`Lakona:StartupActors` configuration model. Old APIs, template output, docs,
and sample usage are removed in the same change; no compatibility shim remains.

Every modified package under `src/**` is bumped by one minor version with patch
reset to zero. The package-version graph determines dependency-closure bumps,
including generators, adapters, and Lakona.Tool.

## Delivery Order

This design begins only after node-directed actor replies are implemented and
validated. It is delivered through separate milestones for cluster descriptor
shape, runtime lifecycle/resolution, generated API, sample migration, docs,
and final integration validation.
