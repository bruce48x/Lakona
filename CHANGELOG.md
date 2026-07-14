# Changelog

This changelog records significant product and architecture milestones. Routine
maintenance and individual patch details are intentionally omitted, while the
date and package versions of important releases are retained.

## 2026-07-15 — Serializer-safe reliable push

**Key releases:** `Lakona.Game.Server 0.18.12`,
`Lakona.ProjectSystem 0.1.5`, and `Lakona.Tool 0.25.25`.

- Routed retained JSON notification commands through generated serialized
  dispatch before the active endpoint serializer emits the wire payload,
  restoring reliable callback delivery for MemoryPack endpoints.

## 2026-07-15 — Service-scoped Hotfix calls

**Key releases:** `Lakona.Game.Server 0.18.11`,
`Lakona.Game.Server.Hotfix 0.8.3`,
`Lakona.Game.Server.Hotfix.Generators 0.5.6`,
`Lakona.ProjectSystem 0.1.4`, and `Lakona.Tool 0.25.24`.

- Generated one readonly `*ServiceCall<TRequest>` context per RPC service so
  Hotfix handlers inherit their strongly typed callback contract instead of
  repeating it on every method.
- Migrated generated projects and maintained samples to the service-scoped
  authoring model, and removed Agar's unused login callback contract.

## 2026-07-14 — Contention-free runtime hot paths

**Key releases:** `Lakona.Game.Server 0.18.10`,
`Lakona.Game.Server.Hotfix 0.8.2`,
`Lakona.Game.Server.Hotfix.Generators 0.5.5`,
`Lakona.Game.Server.Hotfix.Abstractions 0.5.1`,
`Lakona.Game.Cluster 0.5.1`, `Lakona.Game.Cluster.Rpc 0.4.1`,
`Lakona.Game.Cluster.Rpc.MemoryPack 0.3.1`, `Lakona.Game.Cluster.Sql 0.4.1`,
`Lakona.Game.LoadTesting 0.1.2`, `Lakona.Rpc.Server 0.13.9`,
`Lakona.Rpc.Analyzers 0.3.12`, `Lakona.Rpc.Transport.Kcp 0.11.17`,
`Lakona.ProjectSystem 0.1.2`, and `Lakona.Tool 0.25.22`.

- Made Hotfix services generation-owned and replaced reflective service and
  Actor invocation with generated numeric typed dispatch; added value-type
  request contexts and generated value-type client-notification targets.
- Removed global contention from session, route, node, cluster-client, KCP,
  load-recorder, mailbox-metric, and diagnostics-buffer hot paths while
  preserving lifecycle, replay, ordering, epoch, and cleanup semantics.
- Updated generated projects and samples to use completion-friendly typed
  notification methods without per-send targets, callback lambdas,
  `DispatchProxy`, reflection, or argument lists.

## 2026-07-14 — Unified generated actor access

**Key releases:** `Lakona.Game.Server 0.18.9`,
`Lakona.Game.Server.Generators 0.3.1`,
`Lakona.Game.Server.Hotfix.Generators 0.5.4`,
`Lakona.ProjectSystem 0.1.1`, and `Lakona.Tool 0.25.21`.

- Replaced one generated plural collection per actor with a single injectable
  `ActorAccess` root and constrained `Route<TActor>`, `Local<TActor>`,
  `Place<TActor>`, and `Startup<TActor>` selectors.
- Kept selection allocation-free through readonly value-type selectors,
  preserved compile-time actor/key checks and Hotfix method-group completion,
  and migrated generated projects and samples to the smaller API.

## 2026-07-14 — Lakona Hub desktop foundation

**Key releases:** `Lakona.ProjectSystem 0.1.0` and `Lakona.Tool 0.25.20`.

- Added the first Avalonia-based Lakona Hub desktop slice, a reusable read-only
  ProjectSystem inspector, and a non-invasive project contract that keeps Hub
  metadata outside project directories.
- Added the black-and-gold project workspace, fully expanded guided creation
  form, local editor discovery, per-project IDE selection, and a unified
  Settings page with immediate Simplified Chinese and English switching plus
  manual, verified application updates.
- Moved the canonical project creation pipeline behind
  `LakonaProjectCreator`, so the CLI and Hub share defaults, validation,
  rendering, transactional writes, and future generator changes; added a
  GitHub Releases pipeline for self-contained Windows, Linux, and macOS
  packages with a bundled .NET 10 SDK and file-level incremental updates.

## 2026-07-14 — Complete Unity-family project generation

**Key release:** `Lakona.Tool 0.25.19`.

- Matched generated Unity and Tuanjie projects to each editor's complete default
  package baseline, with Lakona's Input System and networking dependencies added
  on top; Unity generation now selects `2022`, `6.0`, or `6.3` through
  `--client-engine-version`, while Tuanjie and Godot remain pinned to their
  current supported versions.

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
