# IRemoteActorSerializer Registration Gap

Date: 2026-06-24

Scope reviewed: how `IRemoteActorSerializer` reaches the DI container for
cross-machine actor calls, across:

- `src/Lakona.Game.Server/Actors/ActorServiceCollectionExtensions.cs`
- `src/Lakona.Game.Server/Actors/IRemoteActorSerializer.cs`
- `src/Lakona.Game.Server.Generators/TypedActorGenerator.cs`
- `src/Lakona.Game.Server/Hosting/LakonaGameServer.cs`
- `src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs`
- `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs`
- `src/Lakona.Tool/Rendering/Server/HotfixRenderer.cs`

## Findings

1. **Nobody registers `IRemoteActorSerializer` in DI.**

   The generated `XxxActors`, `XxxDistributedRef`, `XxxRemoteRef`, and
   `XxxClusterHandler` types all require `IRemoteActorSerializer` as a
   constructor parameter (when the actor is not `[ActorLocalOnly]`). Yet no
   code path registers it:

   - `ActorServiceCollectionExtensions.AddLakonaGameServerActors` — no.
   - `LakonaClusterEndpointServiceCollectionExtensions.AddLakonaGameClusterEndpoint` — no.
   - `LakonaGameServer.RunAsync` hosting pipeline — no.
   - Generated `AddXxxActors` extension method — no (only registers the
     `Actors` type and the `ClusterHandler`).
   - `lakona-tool new` templates (`ServerAppRenderer`, `HotfixRenderer`) — no.

   The first user who writes a non-local-only actor and calls
   `.Remote(nodeId, key).MethodAsync()` will get a DI resolution failure at
   runtime. The only existing `IRemoteActorSerializer` implementations are in
   test files (`TypedActorDispatcherTests.JsonRemoteActorSerializer`).

2. **The gap is invisible today because sample actors happen to be local-only.**

   Agar's `RoomActor`, `MatchmakingActor`, `UserActor`, and
   `LeaderboardActor` are used only through `IActorRuntime.AskAsync` /
   `TellAsync` (local, node-scoped calls). The zero-template `ChatRoomActor`
   from `lakona-tool new` is likewise local-only. Cross-machine remote
   invocation (`Remote(nodeId, key).MethodAsync()`) is not exercised by any
   sample, so the missing registration has never surfaced as a bug.

3. **The split between endpoint, cluster, actor, and framework-control
   serialization is undocumented.**

   There are three serialization layers operating at different levels:

   | Layer | Interface | What it serializes | Selector |
   |---|---|---|---|
   | Client-facing business endpoint | `IRpcSerializer` | generated business RPC request/reply DTOs | `Lakona:Endpoints[]:Serializer` |
   | Cluster and remote actor | `IRpcSerializer` plus `IRemoteActorSerializer` adapter | cluster protocol DTOs, feature messages, notification relay commands, remote actor request/reply payloads | `Lakona:Cluster:Serializer` |
   | Client-facing framework control | `LakonaInternalCodec` | handshake, heartbeat, reliable push ack, session termination notice | fixed framework codec |

   Endpoint `Serializer` should not be reused implicitly for cluster traffic
   because one node can expose multiple client-facing endpoints with different
   serializers. Cluster traffic needs its own explicit selector. Remote actor
   payloads should follow the cluster selector because remote actor calls travel
   over the cluster channel.

4. **`lakona-tool new` should not generate a JSON-only actor serializer.**

   A new project created with `lakona-tool new` currently has no
   `IRemoteActorSerializer` registration anywhere. The original options were:

   - **Option A**: `lakona-tool new` generates a concrete serializer class
     (e.g. `Server.App/Serialization/ActorSerializer.cs`) and registers it
     in the generated `Program.cs` or via a generated extension method.
   - **Option B**: The framework provides a default implementation (e.g.
     `JsonRemoteActorSerializer` using `System.Text.Json`) and registers it
     in `AddLakonaGameServerActors` as a `TryAddSingleton`, letting users
     override with their own binary serializer.
   - **Option C**: The generated `AddXxxActors` method also registers a
     default `IRemoteActorSerializer` if one isn't already in the container.

   After review, the right variant of Option B is a framework default
   `IRemoteActorSerializer` that adapts the configured cluster `IRpcSerializer`,
   not a standalone JSON implementation. If a user selected `memorypack` when
   the project was generated, business RPC payloads, cluster RPC payloads, and
   remote actor payloads should all use MemoryPack.

## Suggested Resolution

- Add `Lakona:Cluster:Serializer` with supported values `json` and
  `memorypack`. All nodes that exchange cluster RPC traffic must use the same
  cluster serializer.
- Make `lakona-tool new` write `Lakona:Cluster:Serializer` from the same
  `--serializer` choice whenever a generated template emits cluster
  configuration.
- Use the cluster serializer in both
  `AddLakonaGameClusterEndpoint` and `LakonaClusterRpcServerConfigurator`
  instead of hard-coding `JsonRpcSerializer`.
- Add a default `IRemoteActorSerializer` implementation that adapts the
  configured cluster `IRpcSerializer`. Register it with `TryAddSingleton` so
  advanced users can override it before framework defaults run.
- Keep `LakonaInternalCodec` for client-facing framework control messages.
  Handshake, heartbeat, reliable push ack, and session termination notice do
  not follow endpoint or cluster serializer selection.
- Add DI and roundtrip coverage for `json` and `memorypack` cluster
  serializers, including resolving generated non-local actor accessors and
  serializing remote actor request/reply payloads.
