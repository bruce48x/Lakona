# Seeded Actor Directory Design

## Goal

Make distributed actor ownership visible to every cluster node. When one node
creates an actor through `ActorHosting`, generated actor references on another
node must resolve the same owner and route the call successfully.

The Actor Directory remains ephemeral. Restarting the directory seed may clear
actor ownership records; durable actor recovery and directory replication are
outside this change.

## Root Cause

`AddLakonaGameServerActors` currently installs a separate
`InMemoryActorDirectory` in every process. A remote actor-host request can
therefore create and register an actor successfully on a battle node while the
gateway still sees an empty local directory.

The repository also contains `ActorDirectoryClient`,
`IActorDirectoryHostClient`, and actor-directory discovery labels, but there is
no production host client or handler. Every node advertises the same directory
label, so completing that path without leader election or shared state would
allow different callers to select inconsistent directories.

## Architecture

Actor Directory follows the existing seeded Node Directory and Route Directory
model:

1. A cluster node whose configured seed is its own cluster endpoint owns the
   local `InMemoryActorDirectory`.
2. A node whose seed points to another cluster endpoint installs a seeded
   `IActorDirectory` client.
3. The seed registers an Actor Directory cluster-message handler backed by its
   local `IActorDirectory`.
4. The seeded client performs resolve, register, and unregister calls directly
   against the configured seed endpoint through the existing ClusterMessage
   RPC method. The cluster envelope uses the configured cluster serializer;
   the Actor Directory request and reply remain opaque JSON payloads owned by
   `Lakona.Game.Server`.
5. `ActorHosting`, placement, generated actor refs, and the local directory
   cache keep using `IActorDirectory`; business code receives no new API or
   configuration.

The seed-selection rule is exactly the rule already used for Node Directory and
Route Directory. Actor ownership therefore has one source of truth in the
current topology and introduces no second discovery or failover model.

Reusing the existing cluster-message envelope also preserves package direction:
`Lakona.Game.Cluster.Rpc` does not depend on server actor types, and its
MemoryPack formatter catalog does not need Actor Directory DTOs.

## Removed Incomplete Surface

Remove the unused discoverable-host path:

- `ActorDirectoryClient`
- `IActorDirectoryHostClient`
- `ActorDirectoryLabels`
- unconditional actor-directory labels on cluster node registration

These types describe a multi-host directory design that lacks leader election,
replication, and production transport. Keeping them would imply unsupported
semantics and create two competing Actor Directory architectures.

## Failure Semantics

- Transport, serialization, or seed unavailability is surfaced as
  `ActorDirectoryUnavailableException` with the original failure retained.
- Caller cancellation remains cancellation and is not wrapped.
- Directory conflict and ownership-mismatch statuses pass through unchanged.
- No actor call or lookup implicitly creates an actor.

## Compatibility

This is an intentional cleanup for an early-stage framework. The unused public
discoverable-host types are removed rather than preserved. Normal consumers of
`IActorDirectory`, `ActorHosting`, and generated actor selectors require no
changes.

## Tests and Acceptance

The implementation is test-driven and must cover:

- seeded Actor Directory client resolve/register/unregister RPC round trips;
- cluster endpoint dependency injection choosing local directory on the seed
  and seeded directory on remote nodes;
- cluster RPC binding of the Actor Directory service;
- a cross-node contract where an actor registered through one remote client is
  resolvable through another;
- cancellation and unavailable-seed failure classification;
- removal of actor-directory discovery labels and obsolete types;
- the complete `Lakona.Game.Server.Tests` suite;
- Agar business-logic tests;
- the audited three-node Agar Unity E2E, including successful guest login and
  the explicit five-second matchmaking acceptance assertion.

## Scope Checkpoint

- **Affected package:** `Lakona.Game.Server`.
- **Affected tests and sample:** `Lakona.Game.Server.Tests` and the existing
  Game.Unity.Agar three-node smoke path.
- **Strong coupling:** Actor Directory RPC protocol, DI selection, server
  binding, and failure translation stay under one implementation owner.
- **Independent work:** only final review is delegated after the integrated
  implementation and tests are complete.
- **Versioning:** `Lakona.Game.Server` already has the required `0.12.0` release
  bump on this integrated branch; it must not receive a second bump for commits
  that belong to the same unreleased change set.
- **Deferred:** Startup Actor service-group implementation remains paused until
  this prerequisite and the Agar five-second acceptance path pass.
