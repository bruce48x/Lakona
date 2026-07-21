# Changelog

This changelog records significant product and architecture milestones. Routine
maintenance and individual patch details are intentionally omitted, while the
date and package versions of important releases are retained.

## 2026-07-21 — Implicit Actor placement and explicit runtime composition

**Key releases:** `Lakona.Game.Cluster 0.5.4`,
`Lakona.Game.Cluster.Rpc 0.6.0`,
`Lakona.Game.Cluster.Rpc.Transport.Tcp 0.1.0`,
`Lakona.Game.Cluster.Rpc.Serializer.Json 0.1.0`,
`Lakona.Game.Cluster.Rpc.Serializer.MemoryPack 0.1.0`,
`Lakona.Game.Cluster.Sql 0.4.4`,
`Lakona.Game.Server.Hotfix.Abstractions 0.8.4`,
`Lakona.Game.Server.Hotfix 0.12.4`, `Lakona.Game.Server 0.23.0`,
`Lakona.ProjectSystem 0.4.0`, `Lakona.Tool 0.30.0`, and `Lakona Hub 0.4.0`.

- Made rendezvous hashing implicit when an Actor has no placement override and
  added `RegisterStartup<TActor, TKey>()` as its Startup-affinity counterpart,
  while retaining selector overloads for product-specific algorithms.
- Removed the unused node-advertisement seam and made cluster RPC an explicit
  `UseClusterRpc` composition of one bidirectional transport and one serializer
  protocol, with pre-RPC peer negotiation and separately installable TCP,
  JSON, and MemoryPack adapters.
- Made generated servers reference only their selected endpoint and cluster
  implementations, removed the cluster serializer string setting, and kept
  project tooling package versions aligned.

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
`Lakona.ProjectSystem 0.3.1`, `Lakona.Tool 0.28.1`, and `Lakona Hub 0.3.15`.

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
`Lakona.ProjectSystem 0.3.0`, `Lakona.Tool 0.27.0`, and `Lakona Hub 0.3.8`.

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
