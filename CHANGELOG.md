# Changelog

This changelog records significant product and architecture milestones. Routine
maintenance and individual patch details are intentionally omitted, while the
date and package versions of important releases are retained.

## 2026-08-17 — Cluster and RPC protocol ownership

**Key releases:** `Lakona.Rpc.Core 0.13.13`, `Lakona.Rpc.Client 0.12.19`,
`Lakona.Rpc.Server 0.16.1`, `Lakona.Rpc.Serializer.Json 0.11.13`,
`Lakona.Rpc.Serializer.MemoryPack 0.11.14`, `Lakona.Rpc.Transport.Kcp 0.11.31`,
`Lakona.Rpc.Transport.Loopback 0.11.14`, `Lakona.Rpc.Transport.Tcp 0.11.18`,
`Lakona.Rpc.Transport.WebSocket 0.11.20`, `Lakona.Game.Client 0.4.15`,
`Lakona.Game.Server 0.40.19`, `Lakona.Tool 0.36.19`, and `Lakona Hub 0.10.19`.

- Kept append, vote, proof, and snapshot-install ingress responsive while a
  Join, Promote, or Ready mutation waits on network replication; overlapping
  mutations remain fail-fast behind the node-owned change slot.
- Centralized the internal cluster protocol identifier and active RPC method
  assignments, retaining removed method IDs as guarded tombstones; protocol v3
  also makes routed Actor target proof one required value and records bounded
  internal failure reasons while keeping caller failures generic.
- Consolidated bounded Membership binary primitives, KCP handshake fields, and
  transport length-prefix parsing under their owning protocol modules, while
  rejecting malformed UTF-8 response errors at the RPC envelope interface;
  lifecycle protocol v4 also binds Create/Ensure and Destroy to distinct typed
  commands and snapshot-owned reflection-free dispatch.

## 2026-08-16 — Cluster Actor and composition invariants

**Key releases:** `Lakona.Game.Server 0.40.13`, `Lakona.Tool 0.36.13`, and
`Lakona.Hub 0.10.13`.

- Made Cluster Actor RPC handlers require the committed Membership owner at
  construction, eliminating the missing-membership path that bypassed cluster
  incarnation, Membership view, and exact activation proofs.
- Preserved distinct Actor Host and Startup Actor descriptor contracts while
  consolidating their validation, immutable metadata, publication bound,
  uniqueness, and ordering behind one internal authority.
- Required explicit Membership transport/state construction and made
  process-local versus clustered notification composition select exactly one
  framework-owned internal dispatcher without concrete-type probing.

## 2026-08-15 — Bounded cluster and Actor recovery

**Key releases:** `Lakona.Game.Server 0.40.10`, `Lakona.Tool 0.36.10`, and
`Lakona.Hub 0.10.10`.

- Closed Actor Location create/destroy recovery windows, retained exact
  activation evidence through failed release, and made shard stabilization
  deadline-bound and supersedable by the latest Membership view; made
  `ActorActivationId` the sole Actor-lifetime fence across directory,
  lifecycle RPC, and remote dispatch. Moved the distributed Actor Location
  implementation, Startup affinity authority, and lifecycle RPC adapter into
  the cluster-owned module while keeping only narrow ports and process-local
  activation evidence in the Actors module. Failed Create compensation now has
  an independent hard deadline and reports an unconfirmed outcome without
  discarding exact recovery evidence.
- Made committed `NodeReference` the sole route-owner fence, kept formation
  contacts endpoint-only, fenced lifecycle creation to the Hotfix generation
  that minted it, pruned departed replica bookkeeping, and made raw Cluster
  transport, protocol, binder, Membership-node, and handler machinery
  assembly-internal. This is a breaking cleanup of unsupported low-level APIs:
  applications must use the high-level server composition surface and rebuild
  against the public Membership observation contracts. Added deterministic
  recovery, pending-affinity, coordinator, allocation, diagnostics, and frozen
  SHA-256 layout coverage; removed per-selection reflection and boxing from
  canonical Actor identity creation. Consolidated bootstrap, restore,
  committed-view publication, and waiter ownership behind the single
  `IClusterMembership` state owner while limiting the hosted service to
  formation and process lifecycle. Committed snapshot restore cannot initialize
  that owner before formation validates and admits the local incarnation.
- Bounded Membership RPC and authority rounds with concurrent control fan-out
  and an enforced proof-renewal budget evaluated against the final registered
  Membership options; made failed Startup descriptor rollback install an
  explicitly non-cancelable process-lifetime local admission fence. This is a
  breaking `IClusterNodeDescriptorRefresher` change: callers must remove the
  cancellation-token argument from `MarkUnavailableAsync`. Removed the
  unreachable direct shard-handoff protocol so every Actor Location ownership
  change uses one survivor-registry recovery model. Readiness now follows the
  authority-backed distributed admission gate, and cluster Membership,
  authority, Actor Location recovery/failure classification, and notification
  backpressure emit bounded metrics and activities without identity tags.

## 2026-08-13 — Actor Location and notification authority redesign

**Key releases:** `Lakona.Game.Server 0.38.4`, `Lakona.Tool 0.35.4`, and
`Lakona.Hub 0.9.4`.

- Replaced the replicated activation protocol and generic cluster routing stack
  with a 1,024-shard, SHA-256 single-owner Actor Location DHT, typed lifecycle
  RPC, exact activation fencing, and explicit recovery barriers.
- Decoupled Membership from Actor and Startup affinity state; Startup keeps its
  public API while sticky keys use a typed affinity DHT and replica catalogs.
- Routed notifications directly from the opaque Session locator to the exact
  gateway, with bounded FIFO admission and owner-side quorum authority checks;
  Actor behavior now receives the decoded business key separately from the
  canonical `<actor-name>/<key>` runtime identity.
- Made timed-out Actor retirement cancel queued lifecycle work before returning,
  so delayed deactivation cannot run after the timeout result is observed.
- Completed the explicit Actor lifecycle with cluster-wide, exact-activation
  `Place(id).DestroyAsync()` and post-turn `ActorContext.RequestDeactivation()`;
  Agar now rolls back failed room creation, creates fully running rooms in one
  behavior call, and retires successfully settled rooms. The stable framework
  now owns the reusable `ActorPlacement<TActor, TKey>` selector while generated
  Hotfix code retains only business-key-specific `Place` entry points.

## 2026-08-12 — Process-local Actor boundary cleanup

**Key releases:** `Lakona.Game.Server 0.36.1`, `Lakona.Tool 0.33.4`, and
`Lakona.Hub 0.7.7`.

- Removed the public process-local `InMemoryActorDirectory`, the local
  placement middleman, and an unused generic Actor-directory protocol;
  clustered hosts now explicitly own Actor Location composition.

## 2026-08-11 — Application-owned observability

**Key releases:** `Lakona.Rpc.Server 0.16.0`, `Lakona.Game.Server 0.36.0`,
`Lakona.Rpc.Client 0.12.18`, `Lakona.Game.Client 0.4.14`,
`Lakona.Tool 0.33.3`, and `Lakona Hub 0.7.6`.

- Made RPC and Game runtimes provider-agnostic: applications own logging
  providers, OpenTelemetry SDKs, exporters, sampling, and backends, while
  Lakona emits standard `ILogger`, `Meter`, and `ActivitySource` signals.
- Removed the private `Lakona:Observability` configuration,
  `/_lakona/diagnostics/*` protocol, in-process event buffer, exporter
  switches, and capability markers.
- Published a stable instrumentation-scope catalog, normalized custom metrics
  under `lakona.game.*`, added low-cardinality session population gauges, and
  instrumented RPC request count, response status, and dispatch duration;
  health remains an orchestration probe and Hotfix admin access moved to
  `Lakona:Management:Admin`.

## 2026-08-10 — Application-owned client logging and recoverable cluster startup

**Key releases:** `Lakona.Rpc.Client 0.12.17`, `Lakona.Game.Client 0.4.13`,
`Lakona.Game.Server 0.33.33`, `Lakona.Tool 0.32.35`, and `Lakona Hub 0.6.38`.

- Moved the RPC client default from an embedded console provider to a null
  logger, so runtime consumers depend only on logging abstractions while
  generated projects configure their own logging behavior.
- Made Tool and Hub starter projects install and explicitly wire a Console
  logger factory at the client composition root, where developers can replace
  it with their preferred provider.
- Made stalled cluster startup report scoped membership, authority, promotion,
  and transient-failure diagnostics, while one serialized membership-change
  slot and same-term joint-proposal completion let concurrent startup recover
  without weakening prior-term fencing.

## 2026-08-08 — Session-oriented notifications and term-safe promotion

**Key releases:** `Lakona.Game.Server 0.33.30`, `Lakona.Tool 0.32.32`, and
`Lakona Hub 0.6.35`.

- Removed the connection-scoped callback from generated Hotfix service calls;
  all Lakona.Game business notifications now select a `GameSessionKey` through
  `IClientNotifications`, preserving one interface for routing, admission,
  reliable push, and reconnect behavior while RPC-only callback support remains
  available below the Game layer.
- Made transient joint-consensus learner-promotion replication failures retry
  the same pending log entry only under the originating leader term, preserving
  both-voter-majority safety while term changes remain fail-closed and generated
  three-node clusters can converge.

## 2026-08-07 — Formation-safe recovery and framework-aligned skills

**Key releases:** `Lakona.Game.Server 0.33.27`, `Lakona.Rpc.Server 0.14.17`,
`Lakona.Tool 0.32.29`, and `Lakona Hub 0.6.31`.

- Retried only definitely unexecuted (`Rejected`) activation-directory sends
  within a small cancellation-aware bound, classified formation races with the
  typed `MembershipUnavailable` result, and kept indeterminate delivery,
  joint-consensus, and prior-term proposal outcomes fail-closed.
- Resumed only same-term ordinary membership proposals through the leader
  control loop, while malformed typed RPC payloads return `BadRequest` with
  stable metadata-only diagnostics and application failures remain
  `HandlerError`.
- Aligned the bundled Skill Pack with generated `ActorAccess.Place`,
  application-owned session roles and cleanup, and the current public Skill
  metadata used by both project creators.

## 2026-08-06 — Authoritative contracts, routing, and game ticks

**Key releases:** `Lakona.Rpc.Core 0.13.12`, `Lakona.Rpc.Client 0.12.16`,
`Lakona.Rpc.Server 0.14.15`, `Lakona.Rpc.Serializer.Json 0.11.12`,
`Lakona.Rpc.Serializer.MemoryPack 0.11.13`, `Lakona.Rpc.Transport.Kcp 0.11.30`,
`Lakona.Rpc.Transport.Loopback 0.11.13`, `Lakona.Rpc.Transport.Tcp 0.11.17`,
`Lakona.Rpc.Transport.WebSocket 0.11.19`, `Lakona.Game.Client 0.4.12`,
`Lakona.Game.Server 0.33.16`, `Lakona.Tool 0.32.17`, and `Lakona Hub 0.6.19`.

- Made `RpcServiceAttribute.NotificationContract` the single association
  authority between an RPC service and its notification contract by replacing
  the reverse `RpcNotificationContractAttribute.ServiceType` pointer with a
  parameterless interface marker, and enforced one-to-one ownership through
  explicit source-generation validation.
- Removed the stale half-cluster discovery path: full game servers now route
  through committed membership and exact node incarnations, membership ingress
  returns one retryable `NotLeader` result with bounded hint following, and
  cluster diagnostics resolve authoritative runtime health options at request
  time.
- Made the Agar server authoritative for input ticks: clients submit intent
  plus their last received server tick, the server assigns input ticks, and
  skipped ticks replay as one batch notification without the concurrent
  in-flight race that dropped one-shot events.

## 2026-08-05 — Bounded runtime lifecycle and focused tooling workflows

**Key releases:** `Lakona.Game.Server 0.33.7`, `Lakona.Tool 0.32.8`, and
`Lakona Hub 0.6.9`.

- Made framework termination cancel the exact RPC Session lease, close its
  transport, and release endpoint connection capacity without a public closer
  extension point.
- Made non-retained termination remove recovery state immediately and bounded
  retained terminal outcomes by the exact Session resume deadline, with
  mandatory cleanup of Session indexes and opaque tickets.
- Added fixed-rate structured diagnostics for activation replica read, repair,
  quorum commit, and extra-copy failures without exposing Actor identities or
  introducing high-cardinality metric labels.
- Simplified project tooling surfaces and moved Hub environment, SDK, and
  update lifecycles behind focused, independently tested workflows.

## 2026-08-04 — Role-driven Game server generation

**Key releases:** `Lakona.Rpc.Core 0.13.11`, `Lakona.Rpc.Client 0.12.15`,
`Lakona.Rpc.Server 0.14.14`, `Lakona.Rpc.Serializer.Json 0.11.11`,
`Lakona.Rpc.Serializer.MemoryPack 0.11.12`, `Lakona.Rpc.Transport.Kcp 0.11.29`,
`Lakona.Rpc.Transport.Loopback 0.11.12`, `Lakona.Rpc.Transport.Tcp 0.11.16`,
`Lakona.Rpc.Transport.WebSocket 0.11.18`, `Lakona.Game.Client 0.4.11`,
`Lakona.Game.Server 0.33.3`, `Lakona.Tool 0.32.3`, and `Lakona Hub 0.6.3`.

- Replaced four independent Game server generator switches with the single
  `LakonaProjectRole` contract for stable App and replaceable Hotfix projects.
- Made the RPC and Hotfix generators derive the same stable namespace from the
  App project's SDK-owned `RootNamespace`, eliminating mismatched binder and
  proxy output without redeclaring the compiler-visible SDK property.
- Added a direct compiler error for unknown `LakonaProjectRole` values so a
  typo cannot silently disable Game server generation or Hotfix validation.

## 2026-08-03 — Clone-ready generation and bounded runtime delivery

**Key releases:** `Lakona.Rpc.Core 0.13.10`, `Lakona.Rpc.Client 0.12.14`,
`Lakona.Rpc.Server 0.14.13`, `Lakona.Rpc.Serializer.Json 0.11.10`,
`Lakona.Rpc.Serializer.MemoryPack 0.11.11`, `Lakona.Rpc.Transport.Kcp 0.11.28`,
`Lakona.Rpc.Transport.Loopback 0.11.11`, `Lakona.Rpc.Transport.Tcp 0.11.15`,
`Lakona.Rpc.Transport.WebSocket 0.11.17`, `Lakona.Game.Client 0.4.10`,
`Lakona.Game.Server 0.33.0`, `Lakona.Tool 0.32.0`, and
`Lakona Hub 0.6.0`.

- Made Tool and Hub restore exact Unity or Tuanjie dependencies before
  publishing source and track the verified `Assets/Packages` tree; generated
  clients now use framework-owned Session establishment and retain only the
  latest complete world snapshot awaiting the scene thread.
- Made Loopback closure symmetric with bounded directional backpressure and
  scheduled isolated KCP updates from protocol-reported deadlines instead of
  offering every connection work on every scheduler scan.
- Reduced generated notification dispatch to typed and serialized numeric
  contracts, removed dead project-mutation paths, made Hotfix activation order
  deterministic, and surfaced cleanup warnings plus build/version lifecycle
  diagnostics without rolling back an already published generation.
- Folded the Hotfix authoring interface into `Lakona.Game.Server`; generated
  App and Hotfix projects now model one application split for reload, with
  Hotfix inheriting the framework interface through App instead of referencing
  a separate abstractions assembly.

## 2026-08-02 — Bounded game runtime and authoritative multiplayer samples

**Key releases:** `Lakona.Rpc.Server 0.14.12`,
`Lakona.Rpc.Transport.Kcp 0.11.26`, `Lakona.Game.Server 0.32.42`,
`Lakona.Tool 0.31.79`, and `Lakona Hub 0.5.92`.

- Made disconnects and KCP background faults terminal, released Session and
  admission resources before observers run, and rebuilt disconnected cluster
  RPC clients on demand without ambiguous request replay.
- Bounded Hotfix Timer population, exposed low-cardinality Actor activation
  metadata, and made `[ActorMethod]` and `[ActorIgnore]` authoritative runtime
  and generated-dispatch contracts.
- Moved `Game.Unity.Agar` to server-authoritative frame synchronization, added a
  playable Unity MMO sample, and reduced generated clients to one supported
  multiplayer renderer per engine.

## 2026-08-01 — Deterministic Hotfix startup and bounded reliable push

**Key releases:** `Lakona.Game.Client 0.4.8`,
`Lakona.Game.Server 0.32.37`, `Lakona.Tool 0.31.71`, and
`Lakona Hub 0.5.83`.

- Made each Hotfix assembly's optional `[HotfixStartup]` class its single
  composition root and stabilized discovery and diagnostic ordering before any
  Actor or service registration executes.
- Replaced per-acknowledgement background RPCs with a client-owned,
  single-consumer reliable-push pump with cumulative coalescing, one in-flight
  call, negotiated deadlines, generation fencing, and owned shutdown.

## 2026-07-31 — Self-forming clusters with bounded connection admission

**Key releases:** `Lakona.Rpc.Server 0.14.11`,
`Lakona.Rpc.Transport.Kcp 0.11.25`, `Lakona.Game.Server 0.32.36`,
`Lakona.Tool 0.31.69`, and `Lakona Hub 0.5.80`.

- Replaced writable node directories, heartbeats, bootstrap flags, and seed
  lists with replicated membership that self-forms one canonical cluster view,
  recovers learners across leader changes and compaction, and fences unsafe
  eviction or return.
- Made generated `ActorAccess` the business facade for local, startup, and
  cluster-aware placement while keeping activation transactions and Hotfix
  lifecycle behind their hosting owners.
- Added bounded KCP receive backpressure plus RPC connection, pending Game
  Handshake, and handshake-deadline budgets with exactly-once lease cleanup for
  overloaded or silent connections.

## 2026-07-30 — Writer-first RPC hot paths and tighter Hotfix interfaces

**Key releases:** `Lakona.Rpc.Core 0.13.8`,
`Lakona.Rpc.Client 0.12.12`, `Lakona.Rpc.Server 0.14.8`,
`Lakona.Rpc.Serializer.Json 0.11.8`,
`Lakona.Rpc.Serializer.MemoryPack 0.11.9`,
`Lakona.Rpc.Transport.Kcp 0.11.23`,
`Lakona.Rpc.Transport.Loopback 0.11.8`,
`Lakona.Rpc.Transport.Tcp 0.11.13`,
`Lakona.Rpc.Transport.WebSocket 0.11.15`,
`Lakona.Game.Client 0.4.6`, `Lakona.Game.Server 0.32.26`,
`Lakona.Tool 0.31.58`, and `Lakona Hub 0.5.69`.

- Changed the serializer extension contract to write typed client requests,
  typed server responses, and typed server notifications directly into their
  final pooled RPC envelopes while retaining decoded notification metadata as
  an owned frame slice.
- Kept client notification reception lossless and non-blocking with its
  intentionally unbounded queue, adding coalesced count/byte high-water
  warnings so representative load tests can guide any later overload policy.
- Moved server and Hotfix packaging behind one `Lakona.ProjectSystem` boundary
  shared by Tool and Hub, added a Hub package action beside “Open server”, and
  standardized generated projects on the alphanumeric `LakonaBuildTag` in
  `Server/BuildTag.props`. Full and Hotfix package names now identify their
  kind, BuildTag, and automatic UTC version, refuse collisions, and share one
  deployment authority. Server packing now rebuilds its bundled Hotfix
  abstractions and analyzer during normal packs, reuses the verified Release
  outputs for `--no-build`, and prevents stale binaries from entering local or
  published packages. Removed the no-op
  `FriendOf` metadata so paired App-to-Hotfix assembly grants and analyzer
  rules remain the only internal Actor state access model.

## 2026-07-29 — Explicit boundaries, direct Actor frames, and project-ready generation

**Key releases:** `Lakona.Rpc.Core 0.13.7`,
`Lakona.Rpc.Client 0.12.11`, `Lakona.Rpc.Server 0.14.7`,
`Lakona.Rpc.Serializer.Json 0.11.7`,
`Lakona.Rpc.Serializer.MemoryPack 0.11.8`,
`Lakona.Rpc.Transport.Kcp 0.11.22`,
`Lakona.Rpc.Transport.Loopback 0.11.7`,
`Lakona.Rpc.Transport.Tcp 0.11.12`,
`Lakona.Rpc.Transport.WebSocket 0.11.14`,
`Lakona.Game.Client 0.4.5`, `Lakona.Game.Server 0.32.21`,
`Lakona.Tool 0.31.49`, and `Lakona Hub 0.5.53`.

- Replaced remaining friend-assembly coupling across RPC hosting, Hotfix
  timers, and ProjectSystem adapters with explicit framework interfaces, and
  made every `ILakonaGameServer` operation a required compile-time contract.
- Narrowed Hotfix service invocation to generated numeric RPC ids and HTTP
  endpoint slots, split generation into product-local modules, and changed
  cross-node Hotfix Actor calls to cached typed MemoryPack codecs that write
  directly into owned RPC envelope buffers without per-call reflection,
  copied payload arrays, or general cluster-message wrapping.
- Tool and Hub now generate projects with the matching official Lakona Skill
  Pack and engine-aware `.gitattributes` rules for .NET, Unity/Tuanjie, or
  Godot repositories.

## 2026-07-28 — Unified runtime and explicit package ownership

**Key releases:** `Lakona.Rpc.Core 0.13.5`,
`Lakona.Rpc.Client 0.12.9`, `Lakona.Rpc.Server 0.14.4`,
`Lakona.Rpc.Serializer.Json 0.11.5`,
`Lakona.Rpc.Serializer.MemoryPack 0.11.6`,
`Lakona.Rpc.Transport.Kcp 0.11.20`,
`Lakona.Rpc.Transport.Loopback 0.11.5`,
`Lakona.Rpc.Transport.Tcp 0.11.10`,
`Lakona.Rpc.Transport.WebSocket 0.11.12`,
`Lakona.Game.Client 0.4.3`, `Lakona.Game.Server 0.32.12`,
`Lakona.Tool 0.31.37`, and `Lakona Hub 0.5.40`.

- Made `Lakona.Game.Server` the deployment and versioning owner of cluster
  contracts, routing, its private TCP + MemoryPack channel, and the internal
  Hotfix Abstractions and Generators assets; retired their standalone package
  identities while preserving the Hotfix type-identity assembly seam.
- Made Hotfix publication atomic with awaited generation shutdown, made Game
  Session establishment rollback-safe, and reduced startup to one authoritative
  runtime graph with typed actor registration and generated dispatch instead of
  message recording, reflection fallback, or string-dispatched Hotfix state.
- Made `Lakona.Rpc.Core` own its compiler extension, replaced RPC friend access
  with explicit protocol and connection interfaces, and removed the internal
  `Lakona.ProjectSystem` package identity so Tool and Hub remain its release
  owners.

## 2026-07-27 — Simplified authoring and framework-owned cluster RPC

**Key releases:** `Lakona.Rpc.Core 0.13.3`,
`Lakona.Rpc.Client 0.12.7`, `Lakona.Rpc.Server 0.14.2`,
`Lakona.Rpc.Serializer.Json 0.11.3`,
`Lakona.Rpc.Serializer.MemoryPack 0.11.4`,
`Lakona.Rpc.Transport.Kcp 0.11.18`,
`Lakona.Rpc.Transport.Loopback 0.11.3`,
`Lakona.Rpc.Transport.Tcp 0.11.8`,
`Lakona.Rpc.Transport.WebSocket 0.11.10`,
`Lakona.Game.Client 0.4.1`,
`Lakona.Game.Cluster.Rpc 0.6.4`,
`Lakona.Game.Cluster.Rpc.Transport.Tcp 0.1.4`,
`Lakona.Game.Cluster.Rpc.Serializer.Json 0.1.4`,
`Lakona.Game.Cluster.Rpc.Serializer.MemoryPack 0.1.4`,
`Lakona.Game.Cluster 0.5.6`,
`Lakona.Game.Server.Hotfix.Abstractions 0.10.0`,
`Lakona.Game.Server 0.30.0`, `Lakona.Tool 0.31.22`, and
`Lakona Hub 0.5.22`.

- Removed the obsolete standalone stable-actor generator package and its
  actor-method path, folded the Hotfix runtime into `Lakona.Game.Server`, and
  bundled compiler extensions with their owning packages so generated projects
  no longer select or version analyzer packages independently.
- Folded cluster RPC, TCP transport, and the version-tolerant MemoryPack
  protocol into `Lakona.Game.Server`; generated endpoint serializer choices are
  now client-facing only, while Shared and Server.App retain clear contract and
  concrete-hosting responsibilities.
- Made Linux Hub updates invoke the distribution package manager through
  PolicyKit and wait for the confirmed install result instead of reporting
  success after an `xdg-open` handoff.

## 2026-07-26 — Single-source Hotfix HTTP services

**Key releases:** `Lakona.Game.Server.Hotfix.Abstractions 0.9.0`,
`Lakona.Game.Server.Hotfix 0.14.0`,
`Lakona.Game.Server.Hotfix.Generators 0.11.0`,
`Lakona.Game.Server 0.28.0`, `Lakona.Tool 0.31.15`, and
`Lakona Hub 0.5.15`.

- Moved Application HTTP declarations beside their handlers in
  `Server.Hotfix`, removed stable App-side HTTP interfaces and user-authored
  numeric method ids, and retained typed warm dispatch through host-assigned
  endpoint slots.
- Made the initial Hotfix generation publish the process-local route manifest;
  management pre-validation and later behavior reloads must preserve service
  names, methods, and routes while listener isolation, admission, deadlines,
  and generation leases remain stable-host responsibilities.

## 2026-07-25 — Explicit server runtime and authoring boundaries

**Key releases:** `Lakona.Game.Cluster 0.5.5`,
`Lakona.Game.Cluster.Rpc 0.6.3`,
`Lakona.Game.Cluster.Rpc.Transport.Tcp 0.1.3`,
`Lakona.Game.Cluster.Rpc.Serializer.Json 0.1.3`,
`Lakona.Game.Cluster.Rpc.Serializer.MemoryPack 0.1.3`,
`Lakona.Game.Server.Hotfix.Abstractions 0.8.5`,
`Lakona.Game.Server.Hotfix 0.13.2`,
`Lakona.Game.Server.Hotfix.Generators 0.10.1`,
`Lakona.Game.Server 0.27.0`, `Lakona.Tool 0.31.14`, and
`Lakona Hub 0.5.14`.

- Replaced the bespoke management HTTP stack with one root ASP.NET Core
  application, isolated product listeners, bounded request snapshots,
  distributed admission and drain, pinned Hotfix generations, and generated
  typed contracts; Agar exposes its operations behavior through this boundary.
- Removed the SQL-backed cluster-directory package, schema lifecycle, and
  configuration surface; framework membership now has one authoritative
  ephemeral replicated control plane, while durable product data remains
  application-owned.
- Added project-scoped Agent Skills for Application HTTP and business-domain
  server organization, and reorganized Agar's App and Hotfix modules as their
  validated reference layout.

## 2026-07-24 — Server ownership and lifecycle hardening

**Key releases:** `Lakona.Game.Server 0.25.6`, `Lakona.Tool 0.31.11`,
and `Lakona Hub 0.5.11`.

- Kept `LakonaGameServer.RunAsync` as the sole startup entry point and made
  bootstrap, module, Hotfix, framework, shutdown, provider-disposal, readiness,
  and stable-resource ownership explicitly ordered.
- Removed the obsolete actor kernel, timer subsystem, and compatibility surface,
  collapsing ids, refs, lifecycle, registry, and dispatch into one internal
  mailbox work-item model.
- Made stop admission and terminal drain authoritative and disposal race-safe;
  Agar now creates PostgreSQL and Redis clients only on the data node that owns
  its durable Actors.

## 2026-07-23 — Application resources and durable Agar persistence

**Key releases:** `Lakona.Game.Server 0.25.0`, `Lakona.Tool 0.31.5`,
and `Lakona Hub 0.5.5`.

- Added automatically discovered stable application modules with pre-provider
  registration, asynchronous startup, deterministic reverse rollback,
  framework-first shutdown, and unified Ready/NotReady lifecycle diagnostics.
- Migrated the Agar sample's durable users to Dapper + Npgsql and its
  leaderboard to Redis, with both adapters owned by `Server.App` and required
  to connect before each node becomes ready.

## 2026-07-22 — RPC connection boundary

**Key releases:** `Lakona.Rpc.Server 0.14.0`,
`Lakona.Rpc.Analyzers 0.5.0`, `Lakona.Game.Server 0.24.0`,
`Lakona.Game.Server.Hotfix.Generators 0.9.0`,
`Lakona.Game.Cluster.Rpc 0.6.1`,
`Lakona.Game.Cluster.Rpc.Transport.Tcp 0.1.1`,
`Lakona.Game.Cluster.Rpc.Serializer.Json 0.1.1`,
`Lakona.Game.Cluster.Rpc.Serializer.MemoryPack 0.1.1`,
`Lakona.Tool 0.31.2`, and `Lakona Hub 0.5.2`.

- Separated opaque RPC connection identity from transport display names and
  introduced typed/raw registration seams that centralize serialization,
  connection-scoped activation, disposal, notifications, and response encoding.
- Migrated generated, Game, Hotfix, and Cluster binders to
  `RpcConnectionInfo` and `RpcNotificationChannel`; `RpcSession` and direct
  handler registration are now runtime-internal.
- Moved Unity RPC sample contracts to sample-owned `Shared` packages so clients
  and servers compile the same authoritative contract source without a server
  dependency on the client project.

## 2026-07-21 — Implicit Actor placement and explicit runtime composition

**Key releases:** `Lakona.Game.Cluster 0.5.4`,
`Lakona.Game.Cluster.Rpc 0.6.0`,
`Lakona.Game.Cluster.Rpc.Transport.Tcp 0.1.0`,
`Lakona.Game.Cluster.Rpc.Serializer.Json 0.1.0`,
`Lakona.Game.Cluster.Rpc.Serializer.MemoryPack 0.1.0`,
`Lakona.Game.Cluster.Sql 0.4.4`,
`Lakona.Game.Server.Hotfix.Abstractions 0.8.4`,
`Lakona.Game.Server.Hotfix 0.12.4`, `Lakona.Game.Server 0.23.1`,
`Lakona.Tool 0.31.1`, and `Lakona Hub 0.5.1`.

- Made rendezvous hashing implicit when an Actor has no placement override and
  added `RegisterStartup<TActor, TKey>()` as its Startup-affinity counterpart,
  while retaining selector overloads for product-specific algorithms; made
  activation reconciliation preserve older replica-set records across scale-out
  and added versioned release tombstones for repeated Actor lifecycles.
- Removed the unused node-advertisement seam and made cluster RPC an explicit
  `UseClusterRpc` composition of one bidirectional transport and one serializer
  protocol, with pre-RPC peer negotiation and separately installable TCP,
  JSON, and MemoryPack adapters.
- Made generated servers reference only their selected endpoint and cluster
  implementations, removed the cluster serializer string setting and the
  misleading project persistence selector, and kept project tooling package
  versions aligned.

## 2026-07-20 — Replicated cluster ownership

**Key releases:** `Lakona.Game.Cluster 0.5.2`,
`Lakona.Game.Cluster.Rpc 0.5.1`, `Lakona.Game.Cluster.Rpc.MemoryPack 0.4.1`,
`Lakona.Game.Cluster.Sql 0.4.2`, `Lakona.Game.Server.Hotfix.Abstractions 0.8.1`,
`Lakona.Game.Server.Hotfix 0.12.1`, `Lakona.Game.Server 0.22.1`, and
`Lakona.Tool 0.29.1`.

- Added exact cluster/node incarnation identities, immutable membership views,
  bounded in-memory consensus log and snapshots, election fencing, renewable
  quorum proofs, and one serialized replica lifecycle.
- Added a fail-closed distributed-work gate, ordered recovery barrier, and
  supervised host path whose explicit bootstrap or learner join commits Ready
  and re-proves the new view before opening traffic.
- Added unordered-contact multi-node join, joint-consensus promotion/removal,
  stable-NodeId incarnation replacement, Ready descriptor refresh, and
  heartbeat-driven voter log/view catch-up after missed commits.
- Replaced seed hot paths with local membership discovery, exact incarnation
  sending, sticky quorum-replicated Actor activations, receiver fencing, and
  sticky Startup key affinity with separately fenced replica activations while
  retaining custom placement selectors.
- Added self-describing session locators and bounded exact-gateway notification
  batching with a configurable 10 ms default, count/byte limits, per-session
  and process-wide backpressure, while preserving synchronous admission.
- Made the Room Actor owner return its own gameplay endpoint directly to
  matchmaking, keeping transport selection in Agar business code and removing
  the sample's separate endpoint Adapter; also removed its cluster Postgres
  setting.

## 2026-07-19 — Session identity simplification

**Key releases:** `Lakona.Game.Abstractions 0.3.0`,
`Lakona.Game.Client 0.4.0`, `Lakona.Game.Cluster.Rpc 0.5.0`,
`Lakona.Game.Cluster.Rpc.MemoryPack 0.4.0`,
`Lakona.Game.Server.Hotfix 0.12.0`, `Lakona.Game.Server 0.22.0`,
`Lakona.Rpc.Analyzers 0.4.0`, `Lakona.Tool 0.29.0`, and
`Lakona Hub 0.3.21`.

- Removed Game Session `Generation` from public APIs, internal protocol payloads,
  reliable-push metadata, lifecycle events, routing keys, and generated clients;
  the framework now uses the globally unique `SessionId` as the session identity.
- Simplified reliable-push cursor and heartbeat state to be keyed by `SessionId`,
  while retaining independent route-directory versions for cluster ownership.

## 2026-07-17 — Direct Hotfix selectors and reliable Hub workspace

**Key releases:** `Lakona.Game.Server.Hotfix.Abstractions 0.8.0`,
`Lakona.Game.Server.Hotfix.Generators 0.8.0`,
`Lakona.Game.Server.Hotfix 0.11.0`, `Lakona.Game.Server 0.21.0`,
`Lakona.Tool 0.28.1`, and `Lakona Hub 0.3.15`.

- Replaced generated Actor and timer wrappers with compile-time-checked static
  method selectors, preserving direct IDE navigation and generation-safe Hotfix
  unloading.
- Made Hub project creation and workspace state reliable across restarts,
  renamed project roots, localization, and the default WebSocket, OpenUPM, and
  no-database workflow.

## 2026-07-16 — Generation-scoped Hotfix and responsive game delivery

**Key releases:** `Lakona.Game.Server.Hotfix.Abstractions 0.7.0`,
`Lakona.Game.Server.Hotfix.Generators 0.7.0`,
`Lakona.Game.Server.Hotfix 0.10.0`, `Lakona.Game.Server 0.20.0`,
`Lakona.Tool 0.27.0`, and `Lakona Hub 0.3.8`.

- Unified Hotfix behaviors, services, lifecycle handlers, components, and timer
  callbacks as generation-scoped DI instances with compile-time classification
  and provider-owned disposal.
- Made generated client notifications use synchronous bounded admission with
  explicit `Accepted` and `Backpressure` outcomes while background FIFO drains
  own routing and delivery, protecting high-frequency Room ticks from slow
  clients.
- Hardened the cross-platform Hub with persistent workspace recovery and
  version-aware discovery and launching for Unity, Tuanjie, Godot, server IDEs,
  and compatible .NET SDKs.

## 2026-07-15 — Native Hub delivery and resilient Hotfix dispatch

**Key releases:** `Lakona Hub 0.2.12`, `Lakona.ProjectSystem 0.1.7`,
`Lakona.Tool 0.25.27`, `Lakona.Game.Server 0.18.13`,
`Lakona.Game.Server.Hotfix 0.8.4`, and
`Lakona.Game.Server.Hotfix.Generators 0.5.7`.

- Delivered native, warning-clean NativeAOT Hub installers on Windows, macOS,
  and Linux, with final-executable smoke checks and package-manager-safe Linux
  updates.
- Strengthened Hotfix execution by pinning actor calls to one runtime
  generation and preserving serializer-safe reliable notifications.

## 2026-07-14 — Generated runtime and desktop tooling

**Key releases:** `Lakona.Game.Server 0.18.10`,
`Lakona.Game.Server.Hotfix.Generators 0.5.5`,
`Lakona.ProjectSystem 0.1.2`, and `Lakona.Tool 0.25.22`.

- Replaced reflective Hotfix and actor invocation with generated typed dispatch,
  unified actor access behind one injectable root, and removed contention from
  runtime hot paths without weakening lifecycle or ordering guarantees.
- Introduced the Avalonia-based Lakona Hub with shared project inspection and
  creation, editor discovery, language and update settings, and self-contained
  cross-platform releases that bundle the .NET 10 SDK.
- Completed Unity and Tuanjie project-generation baselines and added explicit
  Unity editor-version selection while keeping other engines pinned to their
  supported versions.

## 2026-07-13 — Generated multiplayer projects and local operations

**Key releases:** `Lakona.Game.Server 0.18.8`,
`Lakona.Game.Server.Hotfix 0.8.1`, and `Lakona.Tool 0.25.16`.

- Unified health and local-admin routes on `Lakona:Management:Http`, with
  loopback-safe defaults and migration guidance for generated local servers.
- Made Game Session notifications resolve through the live RPC connection and
  discovered Hotfix host assembly identities, removing generated-project
  callback and assembly-name coupling.
- Improved generated arena projects with server-pushed player state, clearer
  cross-engine gameplay presentation, and reliable Unity-family desktop input
  and window defaults.

## 2026-07-11 — Resilient game runtime and game-first scaffolding

**Key releases:** `Lakona.Game.Abstractions 0.2.7`,
`Lakona.Game.Client 0.3.11`, `Lakona.Game.Server 0.18.2`,
`Lakona.Game.Server.Hotfix 0.8.0`, `Lakona.Rpc.Analyzers 0.3.9`,
`Lakona.Game.Server.Hotfix.Generators 0.5.2`, `Lakona.Tool 0.25.7`,
`Lakona.Cluster 0.5.0`, `Lakona.Cluster.Rpc 0.4.0`,
`Lakona.Cluster.Rpc.MemoryPack 0.3.0`, and `Lakona.Cluster.Sql 0.4.0`.

- Added keyed Startup Actor service groups with application-defined selection,
  replica failover, transactional publication during hotfix reload, and
  compile-time enforcement that non-public actor state remains owned by its
  unique Hotfix behavior.
- Added persistent and remote node and actor directories, unified local health,
  diagnostics, and administration endpoints, and strengthened startup readiness.
- Replaced the default Chat scaffold with a server-authoritative top-down arena
  for Unity, Tuanjie, Godot, and Console, using engine primitives with no art
  assets; added endpoint-scoped reliable push and negotiated 60-second Game
  Session resume for ordered recovery after short network transitions.
- Redesigned the generated arena across Unity, Tuanjie, and Godot with a live
  battlefield login, broadcast-style HUD, segmented health, readable input,
  slower monsters, and stronger projectile and hit feedback.

## 2026-07-10 — Unified cluster actor protocol

- Unified actor wire names and node-directed reply delivery, removing synthetic
  reply routes and tightening cluster ownership validation.
- Moved reliable-push sequencing to the gateway that owns the client session.

## 2026-07-09 — Attribute-driven hotfix actor hosting

- Replaced configuration-duplicated actor startup discovery with generated,
  attribute-driven hotfix registration and lifecycle handling.

## 2026-07-08 — Routed actor call API

- Added explicit cluster actor hosting and generated `Local`, `Route`, and
  completion-aware actor call APIs.
- Migrated the Agar sample to generated routing while preserving five-second
  matchmaking and battle entry behavior.

## 2026-06-27 — Runtime configuration and diagnostics

**Key releases:** `Lakona.Rpc.Server 0.13.2`,
`Lakona.Rpc.Analyzers 0.3.3`, and `Lakona.Game.Server 0.8.14`.

- Added JSON-array environment binding and hotfix service-provider fallback.
- Improved generated binder and method names in runtime diagnostics.

## 2026-06-22 — Serializer-neutral cluster RPC

**Key releases:** `Lakona.Cluster.Rpc 0.2.2`,
`Lakona.Cluster.Rpc.MemoryPack 0.1.0`, `Lakona.Game.Server 0.8.4`,
`Lakona.Rpc.Serializer.MemoryPack 0.11.2`, and `Lakona.Tool 0.12.10`.

- Separated cluster RPC contracts from their MemoryPack adapter and aligned
  generated projects with the new package boundaries.

## 2026-06-17 — Transactional project generation

**Key release:** `Lakona.Tool 0.12.1`.

- Reworked `lakona-tool new` into a transactional full-project generator with
  root documentation, repository metadata, safer ignore rules, and optional Git
  initialization.

## 2026-06-12 — Generated clients and load testing

**Key releases:** `Lakona.Game.LoadTesting 0.1.0`,
`Lakona.Tool 0.10.6`, `Lakona.Game.Server 0.5.2`,
`Lakona.Game.Server.Hotfix 0.2.2`, and
`Lakona.Game.Server.Hotfix.Generators 0.1.3`.

- Added generated console clients and a reusable load-testing package.
- Expanded generated hotfix service discovery and server integration.

## 2026-06-10 — Production network listeners

**Key releases:** `Lakona.Rpc.Transport.Kcp 0.11.15`,
`Lakona.Rpc.Transport.Tcp 0.11.6`, `Lakona.Rpc.Transport.WebSocket 0.11.8`,
and `Lakona.Tool 0.8.21`.

- Added DNS hostname and IPv6 support to TCP, WebSocket, and KCP listeners with
  loopback-safe local defaults.

## 2026-06-07 — Unified Lakona platform

**Key releases:** `Lakona.Tool 0.7.0` and `Lakona.Game.Server 0.4.0`.

- Established the unified project-generation workflow and moved the actor
  mailbox runtime behind the public `Lakona.Game.Server.Actors` boundary.
