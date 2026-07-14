# Changelog

This changelog records significant product and architecture milestones. Routine
maintenance and individual patch details are intentionally omitted, while the
date and package versions of important releases are retained.

## 2026-07-14 — Lakona Hub desktop foundation

**Key releases:** `Lakona.ProjectSystem 0.1.0` and `Lakona.Tool 0.25.20`.

- Added the first Avalonia-based Lakona Hub desktop slice, a reusable read-only
  ProjectSystem inspector, and a non-invasive project contract that keeps Hub
  metadata outside project directories.
- Added the black-and-gold project workspace, fully expanded guided creation
  form, local editor discovery, per-project IDE selection, and a unified
  Settings page with immediate Simplified Chinese and English switching.
- Moved the canonical project creation pipeline behind
  `LakonaProjectCreator`, so the CLI and Hub share defaults, validation,
  rendering, transactional writes, and future generator changes.

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
