# Changelog

This changelog records significant product and architecture milestones. Routine
maintenance and individual patch details are intentionally omitted, while the
date and package versions of important releases are retained.

## 2026-07-16 — Persistent Hub workspace settings

**Key release:** `Lakona Hub 0.2.17`.

- Persisted imported project paths and the selected display language in Hub user
  settings, then restored both when a new Hub process starts.
- Re-inspected saved projects during startup so restored rows reflect their
  current project metadata and health.

## 2026-07-16 — Multi-installation Hub tool management

**Key release:** `Lakona Hub 0.2.16`.

- Added Unity Hub discovery and retained every detected Unity or Godot editor
  installation instead of collapsing each tool kind to one row.
- Added persistent manual tool registration for multiple engine versions and
  arbitrary server IDEs, including legacy path migration and independent
  removal.

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

## 2026-07-16 — Reliable Hub update discovery

**Key release:** `Lakona Hub 0.2.13`.

- Made update checks select the highest available Hub semantic version instead
  of depending on the GitHub Releases API response order, restoring upgrades
  from 0.2.8 to newer releases.

## 2026-07-15 — NativeAOT Hub releases

**Key releases:** `Lakona Hub 0.2.12`, `Lakona.ProjectSystem 0.1.7`, and
`Lakona.Tool 0.25.27`.

- Moved every Hub target to warning-clean NativeAOT publishing on its native
  operating system, with final-executable startup and bundled-SDK smoke gates.
- Replaced runtime bitmap branding with borderless Lakona cat character art and
  kept Linux packages independent by using the desktop environment's standard
  development-application icon.
- Replaced Windows and macOS ZIP distribution with MSI and DMG installers, and
  made Linux DEB/RPM asset names explicitly include `linux-x64`.
- Added Lakona branding to the Windows executable, Start menu shortcut, and
  installed-app entry.
- Added native macOS and Linux application icons, ensured release checkouts
  resolve their LFS-backed bitmap assets, and kept NativeAOT restricted to
  release builds so Avalonia design-time previews retain dynamic XAML loading.
- Standardized button content alignment across navigation, project actions,
  settings, tool browsing, and dialogs.

## 2026-07-15 — Native Linux installation and updates

**Key release:** `Lakona Hub 0.2.1`.

- Replaced the portable Linux archive with DEB and RPM packages that install
  desktop integration and the bundled .NET SDK, and routed verified Linux
  updates through the distribution's system installer instead of modifying
  package-managed files.

## 2026-07-15 — Generation-pinned Hotfix actor dispatch

**Key releases:** `Lakona.Game.Server 0.18.13`,
`Lakona.Game.Server.Hotfix 0.8.4`,
`Lakona.Game.Server.Hotfix.Generators 0.5.7`,
`Lakona.ProjectSystem 0.1.6`, `Lakona.Tool 0.25.26`, and `Lakona Hub 0.2.2`.

- Pinned local and cross-node Hotfix actor invocations to one runtime
  generation while their mailbox handlers execute, restoring the active
  execution scope required by generation-aware services and Lakona timers.
- Restored Agar battle ticks and realtime `WorldState` publication after a
  room starts on a remote battle node.

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
