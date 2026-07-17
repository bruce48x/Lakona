# Changelog

This changelog records significant product and architecture milestones. Routine
maintenance and individual patch details are intentionally omitted, while the
date and package versions of important releases are retained.

## 2026-07-17 — Direct hotfix method selectors

**Key releases:** `Lakona.Game.Server.Hotfix.Abstractions 0.8.0`, `Lakona.Game.Server.Hotfix.Generators 0.8.0`, `Lakona.Game.Server.Hotfix 0.11.0`, `Lakona.Game.Server 0.21.0`, `Lakona.Tool 0.28.0`, and `Lakona Hub 0.3.9`.

- Replaced generated actor and timer `Entries` wrappers with static method
  selectors that preserve direct IDE navigation to behavior implementations.
- Added compile-time enforcement for the exact
  `static module => module.Method` selector shape, preventing captures and
  indirect method selection.
- Kept selector resolution generation-scoped by sharing one DI-owned module
  instance between dispatch, actor calls, and timer callbacks without pinning
  unloaded hotfix assemblies.

## 2026-07-16 — Generation-scoped Hotfix component model

**Key releases:** `Lakona.Game.Server.Hotfix.Abstractions 0.7.0`, `Lakona.Game.Server.Hotfix.Generators 0.7.0`, `Lakona.Game.Server.Hotfix 0.10.0`, `Lakona.Game.Server 0.20.0`, `Lakona.ProjectSystem 0.3.0`, `Lakona.Tool 0.27.0`, and `Lakona Hub 0.3.7`.

- Unified behaviors, RPC services, lifecycle handlers, and timer callbacks as
  generation-scoped, DI-owned instances with generated actor and timer entries.
- Added `[HotfixComponent]` registration, constructor injection, and
  provider-owned disposal while making queued actor work and durable timers
  resolve against the active generation.
- Rejected unclassified component classes and unsafe module state at compile
  time, removing service-locator activation and manual registration fallbacks.

## 2026-07-16 — Production-ready Lakona Hub experience

**Key release:** `Lakona Hub 0.3.8`.

- Hardened the native cross-platform Hub with persistent projects, workspace
  state, development-tool choices, crash recovery, and portable window resizing.
- Added discovery and version-aware launching for Unity, Tuanjie, Godot, and
  server IDEs, plus compatible .NET 10 detection and explicit private-SDK
  installation with verified downloads.
- Made localized actions, settings, dialogs, and borderless-window resizing
  responsive; centralized Hub visual roles and removed duplicate status state.

## 2026-07-16 — Synchronous client notification admission

**Key releases:** `Lakona.Game.Server 0.18.15`,
`Lakona.Game.Server.Hotfix.Generators 0.5.8`,
`Lakona.ProjectSystem 0.1.8`, and `Lakona.Tool 0.25.29`.

- Changed generated client-notification publication to return a synchronous
  admission status without `await` or a caller cancellation token, while
  per-session FIFO drains own route resolution, reliable sequencing,
  serialization, and actual network delivery.
- Replaced the ambiguous `Delivered` status with `Accepted` and added explicit
  `Backpressure` when a session queue is full.
- Restored Agar battle code to direct notification calls because slow client
  sends no longer block its high-frequency room tick.

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
