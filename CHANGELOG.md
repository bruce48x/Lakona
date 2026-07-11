# Changelog

This changelog records significant product and architecture milestones. Routine
maintenance and individual patch details are intentionally omitted, while the
date and package versions of important releases are retained.

## 2026-07-11 — Resilient game runtime and game-first scaffolding

**Key releases:** `Lakona.Game.Abstractions 0.2.7`,
`Lakona.Game.Client 0.3.10`, `Lakona.Game.Server 0.18.1`,
`Lakona.Game.Server.Hotfix 0.8.0`, `Lakona.Rpc.Analyzers 0.3.9`,
`Lakona.Tool 0.25.1`,
`Lakona.Cluster 0.5.0`, `Lakona.Cluster.Rpc 0.4.0`,
`Lakona.Cluster.Rpc.MemoryPack 0.3.0`, and `Lakona.Cluster.Sql 0.4.0`.

- Added keyed Startup Actor service groups with application-defined selection,
  replica failover, and transactional publication during hotfix reload.
- Added persistent and remote node and actor directories, unified local health,
  diagnostics, and administration endpoints, and strengthened startup readiness.
- Replaced the default Chat scaffold with a server-authoritative top-down arena
  for Unity, Tuanjie, Godot, and Console, using engine primitives with no art
  assets; added endpoint-scoped reliable push and negotiated 60-second Game
  Session resume for ordered recovery after short network transitions.

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
