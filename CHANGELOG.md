# Changelog

This changelog records significant product and architecture milestones. Routine
maintenance and individual patch details are intentionally omitted, while the
date and package versions of important releases are retained.

## 2026-07-28 — Owned RPC compiler delivery

**Key releases:** `Lakona.Rpc.Core 0.13.4`,
`Lakona.Game.Client 0.4.2`, `Lakona.Game.Server 0.32.11`,
`Lakona.Tool 0.31.36`, and `Lakona Hub 0.5.39`.

- Made `Lakona.Rpc.Core` the versioning owner of its bundled RPC compiler
  extension, so analyzer changes now propagate through the normal NuGet package
  dependency graph and generated-project release inputs.
- Removed the stale package identity and synthetic version from the internal
  `Lakona.ProjectSystem` module; Tool and Hub remain its only release owners.

## 2026-07-28 — Unified game server runtime packaging

**Key releases:** `Lakona.Game.Server 0.32.10`, `Lakona.Tool 0.31.34`, and
`Lakona Hub 0.5.36`.

- Folded cluster contracts, membership, routing, messaging, diagnostics, and
  in-memory validation implementations into `Lakona.Game.Server`; retired the
  standalone `Lakona.Game.Cluster` package while retaining its domain namespace.
- Made the framework-owned TCP + MemoryPack channel and its cluster state model
  one deployment and versioning unit, removing the unused package-level
  extension seam and preserving application RPC serializers outside the
  private cluster channel.
- Retired the standalone `Lakona.Game.Server.Hotfix.Abstractions` package while
  preserving it as an internal assembly boundary for collectible Hotfix type
  identity; `Lakona.Game.Server` now carries that assembly, its compiler
  extension, and build-transitive property wiring as one versioned unit; removed
  the non-functional Actor message recorder and replay surface from the default
  hot path; made Hotfix generation publication atomic after candidate
  activation and gave every generation an awaited shutdown owner; made Game
  Session establishment a prepared, rollback-safe transaction; reduced startup
  to one authoritative runtime configuration and dependency graph; removed the
  unused client-notification Relay, `DispatchProxy`, and reflection fallback in
  favor of the generated command path, and pruned unimplemented reconnect and
  local-admin lifecycle remnants; retired `[HotfixState]` friend-accessor
  generation and method-name string dispatch in favor of generated Actor,
  Service, HTTP, and Timer entry points; removed the parallel named Startup
  Actor plan model so startup registration is exclusively type-safe.

## 2026-07-27 — Framework-owned cluster RPC

**Key releases:** `Lakona.Game.Cluster 0.5.6`,
`Lakona.Game.Server 0.30.0`, `Lakona.Tool 0.31.22`, and
`Lakona Hub 0.5.22`.

- Folded cluster RPC, its TCP transport, and its MemoryPack protocol into
  `Lakona.Game.Server`; retired the standalone Cluster RPC transport and
  serializer packages and removed `UseClusterRpc` from application startup.
- Replaced the schema-driven custom formatter generator with official
  version-tolerant MemoryPack source generation and explicit field orders for
  framework and generated stable Actor DTOs.
- Made endpoint `--serializer` choices client-facing only; generated servers
  no longer expose a cluster transport or serializer selection point.

## 2026-07-27 — Reliable Linux Hub updates

**Key releases:** `Lakona Hub 0.5.21`.

- Linux Hub updates now invoke the distribution package manager through
  PolicyKit and wait for a confirmed installation result, so Ubuntu and RPM
  desktops no longer report success immediately after an unreliable
  `xdg-open` handoff.

## 2026-07-27 — Simplified server and generated authoring packages

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
`Lakona.Game.Server.Hotfix.Abstractions 0.10.0`,
`Lakona.Game.Server 0.29.0`, `Lakona.Tool 0.31.21`, and
`Lakona Hub 0.5.20`.

- Removed the obsolete standalone stable-actor generator package and its
  actor-method generation path, and folded the Hotfix runtime into
  `Lakona.Game.Server`; Hotfix behavior methods remain the generated actor
  authoring model without a separately published runtime package.
- Folded the RPC analyzer assembly into `Lakona.Rpc.Core`, removed its
  independently versioned package, and made
  `Lakona.Game.Server.Hotfix.Abstractions` carry its matching compiler
  extension. Generated projects no longer choose or version either compiler
  assembly separately.
- Kept Shared projects at the cross-client contract seam and moved Server
  generation plus concrete endpoint and cluster adapters to Server.App,
  removing redundant hosting and base-runtime dependencies from generated
  projects and maintained samples; top-level Game packages now carry their RPC
  runtimes, while owning packages also hide compiler-property wiring from
  generated user projects. Project creation guidance now relies on the solution
  build to produce Hotfix output instead of asking users to build it twice.

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
