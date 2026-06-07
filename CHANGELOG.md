# Changelog

This file preserves the release history imported from the former Lakona.Game,
Lakona.Actor, and Lakona.Rpc repositories during the monorepo consolidation.
Package and command names have been updated to the Lakona naming scheme where
applicable.

## Lakona.Game History

## 2026-06-06

### Released

- `Lakona.Game.Server` `0.2.0`
- `Lakona.Game.Server` `0.3.0`
- `Lakona.Game.Server` `0.3.1`
- `Lakona.Game.Server` `0.3.2`
- `Lakona.Tool` `0.5.0`
- `Lakona.Tool` `0.5.1`
- `Lakona.Tool` `0.6.0`
- `Lakona.Tool` `0.6.1`
- `Lakona.Tool` `0.6.2`
- `Lakona.Tool` `0.6.4`
- `Lakona.Tool` `0.6.3`
- `Lakona.Tool` `0.6.5`

### Changed

- `Lakona.Tool` `0.6.5`: fixed generated Chat server startup failure where Lakona.Rpc notification service binding could not construct `ChatServiceImpl(IChatCallback, IActorRuntime)`. Generated `ServiceBindingConfigurator` now binds `ChatServiceImpl` through `ChatServiceBinder` and `ActivatorUtilities` so the callback still comes from Lakona.Rpc while `IActorRuntime` comes from DI.
- `Lakona.Game.Server` `0.3.2`: added `IServiceProvider`-aware `BindServices` and `AddRpcEndpoint` overloads so generated service binders can create RPC services through DI.
- `Lakona.Tool` `0.6.4`: fixed `CS0267` build error where `ChatRuleState` template used `partial sealed class` but the Hotfix source generator produced a conflicting `partial class` declaration without `sealed`.
- `Lakona.Tool` `0.6.3`: replaced generated static mutable Chat room with `ChatRoomActor` through `IActorRuntime`. Added stable `ChatRules` Hotfix wrapper and valid `[HotfixSystemOf]` `ChatRulesSystem`. Generated Chat send path now filters messages through Hotfix before broadcasting.
- `Lakona.Game.Server` `0.3.1`: failed initial Hotfix load now throws and fails server startup instead of logging a warning and continuing.
- `Lakona.Tool` `0.6.2`: also remove the server-side `PingService.cs` implementation when cleaning up starter Ping samples, so the generated server project compiles cleanly after Ping contracts are removed.
- `Lakona.Tool` `0.6.1`: removed starter Ping sample files from generated Lakona.Game projects. Moved generated Chat contracts to `Shared/Contracts/Chat/` with namespace `Shared.Contracts.Chat`. Replaced inline Chat RPC IDs with named constants in `Shared.Contracts.RpcContractIds`. Updated generated server, hotfix, Unity client, and Godot client code to use the new namespace and contract ID layout.
- Added the canonical `Lakona.Game` runtime configuration model with `Lakona.Game:Node:Id`, `Lakona.Game:Endpoints[]`, compact `Lakona.Game:Feature`, and minimal `Lakona.Game:Cluster` binding.
- Added Feature Catalog startup APIs with ordered `LakonaGameFeature` registration, feature dependencies, transport requirements, and fail-fast validation.
- Updated guardrails, generated project templates, and Agar Unity Gateway sample to use `Lakona.Game:Endpoints[]` and Feature Catalog startup instead of singular endpoint or role-shaped configuration.
- Updated configuration/startup documentation and package READMEs to make the new schema the current guidance.
- `Lakona.Tool` `0.5.1`: relaxed `lakona-starter` version check from exact match to `>=` minimum; auto-update now installs latest available version instead of pinning to a specific version.
- `Lakona.Game.Server` `0.3.0`: moved `ClusterOptions`, `ServerRpcServerOptions`, and `Lakona.GameRuntimeOptions` configuration types from Tool-generated layer into the framework package. Added `LakonaGameServer.RunAsync()` unified hosting entry point with `LakonaGameServerBuilder` for delegate-based RPC wiring (`UseSerializer`, `UseAcceptor`, `BindServices`, `ConfigureFeatures`, `AddRpcEndpoint`). Added layered health probes: `Lakona.GameLivenessProbe` (`--health-check`, profile-aware cluster/standalone) and `Lakona.GameReadinessProbe` (`--readiness-check`, with Guardrails integration and `--json` output). Added automatic feature discovery from entry assembly, referenced assemblies, and hotfix directory when `ConfigureFeatures` is not called.
- `Lakona.Tool` `0.6.0`: replaced generated `ClusterHealthCheck.cs`, `ClusterOptions.cs`, `ServerRpcServerOptions.cs`, `DefaultRpcServerConfigurator.cs`, and `LakonaGameGeneratedApplication.cs` with thin `Program.cs` wiring (`LakonaGameServer.RunAsync`) and `ServiceBindingConfigurator.cs` (`AllServicesBinder.BindAll`). Generated projects now consume hosting lifecycle, health checks, and configuration from the `Lakona.Game.Server` `0.3.0` framework package instead of owning generated copies.

## 2026-06-05

### Released

- `Lakona.Game.Cluster.Rpc` `0.1.3`
- `Lakona.Game.Server` `0.1.26`
- `Lakona.Tool` `0.4.17`
- `Lakona.Tool` `0.4.18`
- `Lakona.Tool` `0.4.19`

### Changed

- Updated `Lakona.Game.Cluster.Rpc` and `Lakona.Game.Server` to consume `Lakona.Rpc.Client` `0.12.0`, `Lakona.Rpc.Server` `0.12.1`, and `Lakona.Rpc.Transport.Tcp` `0.11.4`.
- Updated `Lakona.Tool` to consume `Lakona.Rpc.Starter` `0.4.1` and generate contracts with the `Lakona.Rpc` `0.12` notification naming.
- Updated `Lakona.Tool` generated server projects to keep `Program.cs` as a thin entrypoint and move generated hosting, check, cluster, RPC, and hotfix setup into `Hosting/Advanced/LakonaGameGeneratedApplication.cs`.
- Removed the fourth post-create next step from `lakona new` output.
- Updated Agar samples and tests to use `Lakona.Rpc.Core` `0.12.0`, `Lakona.Rpc.Client` `0.12.0`, and `Lakona.Rpc.Analyzers` `0.2.0`, replacing callback/push contract attributes with notification contract attributes.

## 2026-06-04

### Released

- `Lakona.Game.Abstractions` `0.1.3`
- `Lakona.Game.Client` `0.1.6`
- `Lakona.Game.Server` `0.1.24`
- `Lakona.Game.Server` `0.1.25`
- `Lakona.Tool` `0.4.14`
- `Lakona.Tool` `0.4.15`
- `Lakona.Tool` `0.4.16`
- `Lakona.Game.Server` `0.1.22`
- `Lakona.Game.Server` `0.1.23`
- `Lakona.Tool` `0.4.8`
- `Lakona.Tool` `0.4.9`
- `Lakona.Tool` `0.4.10`
- `Lakona.Tool` `0.4.11`
- `Lakona.Tool` `0.4.12`
- `Lakona.Tool` `0.4.13`

### Fixed

- Fixed generated Unity projects to rescan existing NuGet analyzer DLLs and disable them as Unity plugins after the import guard compiles.
- Fixed generated Unity chat UI rendering by linking the chat stylesheet, generating the default runtime theme, and writing transport-specific scene path values.
- Fixed generated Unity chat UI send interactions by surfacing join/send status, marshalling RPC callbacks onto the Unity main thread, and making dark-theme form controls readable.
- Updated generated Godot projects to use the distributed chat app template with a Godot main chat scene.
- Fixed actor stop timeouts so queued deactivation hooks are cancelled when timed stop drains fail.

### Changed

- Added fixed session termination notice contracts, server-side notify-before-close orchestration, and client terminal-session helpers.
- Updated `Lakona.Game.Server` to consume `Lakona.Actor` `0.4.0`, adapting actor handles, call options, lifecycle diagnostics, mailbox metrics, and actor state lookups to the new runtime API.
- Removed the `ActorRuntimeOptions.ExecutionTimeout` configuration surface because `Lakona.Actor` `0.4.0` no longer supports preemptive actor turn execution timeouts; use `SlowMessageThreshold` for long handler diagnostics and `CallTimeout` for queue/response timeout bounds.
- Updated `Lakona.Game.Server` to consume `Lakona.Actor` `0.5.16`, adapting actor spawn and disposal integration to the asynchronous actor system API.
- Updated `Lakona.Tool` to ship generated templates with `Lakona.Game.Server` `0.1.25`.
- Updated `Lakona.Tool` to consume `Lakona.Rpc.Starter` `0.3.5`.

## 2026-06-03

### Released

- `Lakona.Tool` `0.4.2`
- `Lakona.Tool` `0.4.3`
- `Lakona.Tool` `0.4.4`
- `Lakona.Tool` `0.4.5`
- `Lakona.Tool` `0.4.6`
- `Lakona.Tool` `0.4.7`

### Fixed

- Fixed `lakona new` Unity chat templates to use the generated `Rpc.Generated.RpcClient` API and emit the missing task namespace import.
- Fixed `lakona new` server templates to use the Lakona.Rpc callback-service constructor shape and copy the generated hotfix assembly into the server runtime output.
- Fixed `lakona new` Unity chat UI templates to avoid null-conditional event subscription syntax on `Button.clicked`.
- Fixed `lakona new` Unity chat projects to statically wire the UI Toolkit chat document and panel settings into the starter scene without emitting an editor installer script.
- Fixed `lakona new --serializer json` shared chat contracts to omit MemoryPack attributes and imports.

## 2026-06-02

### Released

- `Lakona.Game.Server.Hotfix.Abstractions` `0.1.1`
- `Lakona.Game.Server.Hotfix` `0.1.1`
- `Lakona.Game.Cluster` `0.1.4`
- `Lakona.Game.Server` `0.1.16`
- `Lakona.Game.Server` `0.1.17`
- `Lakona.Game.Server` `0.1.18`
- `Lakona.Game.Server` `0.1.19`
- `Lakona.Game.Server` `0.1.20`
- `Lakona.Game.Server` `0.1.21`
- `Lakona.Game.Server.Generators` `0.1.1`
- `Lakona.Game.Server.Generators` `0.1.2`
- `Lakona.Game.Server.Generators` `0.1.3`
- `Lakona.Game.Server.Generators` `0.1.4`
- `Lakona.Tool` `0.3.4`
- `Lakona.Tool` `0.4.1`

### Added

- Added actor call exception types and a remote actor call helper for generated actor APIs.
- Added cluster feature node discovery APIs for listing or selecting ready nodes by service feature without exposing endpoints.
- Added server-side actor directory contracts and an in-memory actor directory implementation.
- Added an in-memory actor directory cache for actor id to node id lookups.
- Added a feature-discovery based actor directory client abstraction that caches the directory host node and rediscovers once after host failure.
- Added generated distributed `Get(id)` actor accessors that resolve local-first, then actor directory cache/directory, before remote actor invocation.
- Added actor lifecycle hook attributes and a local actor node identity service for generated managed actor lifecycle.
- Added generated local-only `SpawnAsync` and `DestroyAsync` actor lifecycle APIs with actor directory registration, cache updates, and rollback on spawn failure.

### Changed

- Replaced hardcoded hotfix assembly path with `Path.Combine(AppContext.BaseDirectory, "hotfix")` in generated server code, and added an MSBuild target to copy `Server.Hotfix.dll` into the server output directory after each build. The path is now configuration-independent (works in Debug/Release, any target framework).
- Updated generated remote actor methods to throw actor call exceptions on remote failure instead of emitting status checks and constructing `RemoteActorException` inline.
- Updated generated actor lifecycle ordering to claim actor directory ownership before spawn hooks and unregister ownership before destroy hooks.

### Fixed

- Fixed lifecycle hook diagnostics so spawn hooks may take a request and destroy hooks may not.
- Fixed `lakona new` chat templates to emit C# 9-compatible block-scoped namespaces instead of file-scoped namespaces for Unity-created projects.

## 2026-06-01

### Released

- `Lakona.Game.Server` `0.1.12`
- `Lakona.Game.Server` `0.1.13`
- `Lakona.Game.Server` `0.1.14`
- `Lakona.Game.Server` `0.1.15`
- `Lakona.Game.Server.Generators` `0.1.0`

### Added

- Added typed actor metadata primitives for the server actor runtime API.
- Added server-side typed actor source generation for `Actor<TKey>` local/remote accessors, cluster handlers, and service registration.
- Added `Lakona.Game.Server.Generators` analyzer references to generated server projects.

### Fixed

- Fixed `RemoteActorInvoker` pending-reply cleanup on send failure and direct-node delivery for remote actor invocations.
- Fixed `RemoteActorInvoker.AskAsync` pending-reply cleanup when direct node send throws.
- Fixed `RemoteActorInvoker.AskAsync` cancellation mapping during direct node send.

## 2026-05-31

### Released

- `Lakona.Game.Server` `0.1.10`
- `Lakona.Tool` `0.2.13`
- `Lakona.Tool` `0.3.1`

### Added

- Added runtime guardrail diagnostics, a resolved runtime model, and initial validation rules for node id, endpoints, hotfix presence, and duplicate cluster services.
- Updated generated `--lakona-game-check` to reuse runtime guardrail diagnostics and support `--json` output.

### Fixed

- Updated Unity/Tuanjie scaffolding to pin `Lakona.Game.Abstractions` beside `Lakona.Game.Client` in `Assets/packages.config`.
- Added a generated Unity editor import guard that disables NuGet analyzer DLLs under `Assets/Packages/**/analyzers/` so Unity does not load Roslyn analyzers as runtime plugins.

## 2026-05-30

### Released

- `Lakona.Game.Server` `0.1.9`
- `Lakona.Tool` `0.2.12`

### Changed

- Replaced hardcoded `Lakona.Actor` project reference with NuGet package `Lakona.Actor` `0.3.0` in `Lakona.Game.Server`.

## 2026-05-29

### Released

- `Lakona.Tool` `0.2.10`
- `Lakona.Tool` `0.2.11`

### Added

- Added server hotfix design and initial runtime/generator packages for attribute-discovered hotfix systems.
- Added Agar sample gameplay-rule hotfix integration for arena tick and settlement behavior.
- Added default `Lakona.Tool` hotfix scaffolding with stable `Shared` state, a separate `Server.Hotfix` assembly, hotfix package references, runtime loading, and boundary examples.

## 2026-05-28

### Released

- `Lakona.Game.Cluster` `0.1.3`
- `Lakona.Game.Cluster.Sql` `0.1.0`
- `Lakona.Game.Cluster.Rpc` `0.1.2`
- `Lakona.Tool` `0.2.8`
- `Lakona.Tool` `0.2.9`

### Added

- Added node-directory contracts, in-memory and SQL-backed storage, Lakona.Rpc node-directory adapter, and node-local service configuration scaffolding for cluster deployments.

### Changed

- Updated `Lakona.Tool` to consume `Lakona.Rpc.Starter` `0.3.4`.

## 2026-05-27

### Released

- `Lakona.Game.Server` `0.1.8`
- `Lakona.Tool` `0.2.7`

### Added

- Added Lakona.Game-owned actor diagnostics for Lakona.Actor dead letters, slow messages, and call timeouts through `ActorRuntimeOptions`.
- Added explicit local actor backpressure with `IActorRuntime.TryTell(...)` and `ActorTellResult`.
- Added `ClusterActorTellDispatcher<TActor>` for one-way cluster actor dispatch that maps local mailbox pressure to `ClusterSendStatus.Backpressure`.
- Added explicit actor stop/drain APIs with `ActorStopOutcome`.
- Added Lakona.Game-owned mailbox metrics through `IActorRuntime.TryGetMailboxMetrics(...)`.
- Added mailbox-native timer registration through the actor runtime facade so timer ticks enter the actor mailbox.
- Added `Actor.OnDeactivateAsync(...)` for explicit cleanup during actor stop.

### Changed

- Documented the Lakona.Actor facade design principles in `CONTRIBUTING.md`.
- Updated `Lakona.Game.Server` actor documentation to show the Lakona.Game facade as the recommended API while keeping Lakona.Actor native APIs as an opt-in lower-level choice.
- Updated `Lakona.Tool` so generated project templates consume `Lakona.Game.Server` `0.1.8`.

## 2026-05-26

### Released

- `Lakona.Game.Abstractions` `0.1.2`
- `Lakona.Game.Cluster` `0.1.0`
- `Lakona.Game.Cluster` `0.1.1`
- `Lakona.Game.Cluster` `0.1.2`
- `Lakona.Game.Cluster.Rpc` `0.1.0`
- `Lakona.Game.Cluster.Rpc` `0.1.1`
- `Lakona.Game.Server` `0.1.6`
- `Lakona.Game.Server` `0.1.7`
- `Lakona.Tool` `0.2.2`
- `Lakona.Tool` `0.2.3`
- `Lakona.Tool` `0.2.4`
- `Lakona.Tool` `0.2.5`
- `Lakona.Tool` `0.2.6`

### Added

- Added the initial `Lakona.Game.Cluster` package with explicit node/route/message contracts, actor route envelopes, in-memory route directory, loopback messenger, router diagnostics, and unit tests.
- Added route generation, node epoch, stale route registration rejection, conditional lease refresh, node-epoch clearing, and explicit stale route status values to `Lakona.Game.Cluster`.
- Added the initial `Lakona.Game.Cluster.Rpc` adapter package with a Lakona.Rpc cluster send method descriptor, node messenger, client factory, transport factory boundary, TCP transport factory, server binder, and unit tests.
- Added a TCP smoke test proving that `Lakona.Game.Cluster.Rpc` can send a `ClusterMessage` through a Lakona.Rpc server binder.
- Added `LakonaRpcRouteDirectory` and `LakonaRpcRouteDirectoryBinder` so route register, resolve, expiration, lease refresh, clear-by-node, and clear-by-node-epoch can run through a Lakona.Rpc-managed route directory service.
- Added a TCP smoke test proving the Lakona.Rpc route directory adapter preserves route generation and node epoch semantics across the transport.
- Added `samples/Game.Cluster.TwoNode`, a cross-process Lakona.Rpc cluster smoke sample that starts separate route-directory and worker processes and verifies local dispatch, remote dispatch, route-not-found, expiration, timeout, handler-unavailable, backpressure, stale registration rejection, node-epoch clearing, and node restart.
- Added explicit `lakona new --network-profile cluster` scaffolding for cluster package references, environment-variable-friendly cluster node, endpoint, lease, send-timeout settings, and a local `--health-check` configuration probe.
- Added explicit `lakona new --deploy-profile compose` scaffolding for local cluster deployment rehearsal with Dockerfile, compose healthcheck, `.env.cluster.example`, and an operations note that avoids production secrets.
- Added `LakonaRpcClusterDependencyProbe` so hosts can check Lakona.Rpc route-directory dependency health with bounded timeout and explicit healthy/timeout/unhealthy results.
- Added `ClusterActorDispatcher<TActor>` in `Lakona.Game.Server` to adapt explicit cluster actor envelopes into the local `IActorRuntime` mailbox without exposing transparent remote actor references.
- Added a minimal `samples/Cluster.Loopback` sample that demonstrates in-memory local dispatch, remote loopback dispatch, route-not-found, expiration, timeout, and backpressure.

### Changed

- Updated `LakonaRpcClusterClientFactory` so client cache reuse is scoped by node epoch and endpoint address, preventing a restarted node with the same `NodeId` from inheriting a stale connection.
- Updated `Lakona.Game.Server` to consume `Lakona.Actor` `0.2.0` while preserving the existing process-local `IActorRuntime` facade.
- Updated `Lakona.Tool` so generated project templates consume `Lakona.Game.Server` `0.1.7`.
- Updated `lakona new` to automatically install the pinned `Lakona.Rpc.Starter` tool when `lakona-starter` is not already available.
- Updated `lakona new` so cluster scaffolding is generated by default and the `--network-profile` argument is no longer required.
- Updated generated `lakona` output to preserve the `lakona-starter` server project naming under `Server/Server/Server.csproj`, including namespace, Docker, compose, and health-check commands.
- Updated the cluster loopback sample to register generation-aware route locations.
- Reorganized `src/Lakona.Game.Abstractions`, `src/Lakona.Game.Cluster`, `src/Lakona.Game.Cluster.Rpc`, and `src/Lakona.Tool` source files into responsibility-focused directories without changing public namespaces or APIs.
- Reorganized `CONTRIBUTING.md` around repository workflow, package boundaries, runtime architecture, cluster architecture, and the current development plan.
- Documented the production cluster adapter decision: Lakona.Rpc is the first adapter direction, implemented as a separate transport package only after a real cross-process consumer exists.
- Removed completed or external cluster planning tasks from `CONTRIBUTING.md`; the next implementation should start only when the production adapter gates are met.

## 2026-05-25

### Released

- `Lakona.Tool` `0.2.1`

### Changed

- Updated `Lakona.Tool` to consume `Lakona.Rpc.Starter` `0.3.1`, remove the manual `codegen` command path, and keep generated projects on the `Lakona.Rpc.Analyzers` source-generator workflow.
- Added Simplified Chinese and Traditional Chinese CLI text for `Lakona.Tool`, matching the culture detection used by `Lakona.Rpc.Starter`.
- Migrated the Unity and Godot samples away from committed RPC `Generated/` sources; server and client RPC glue is now compiler output.

## 2026-05-21

### Changed

- Updated the Lakona.Actor integration to consume `Lakona.Actor` `0.1.9`.

## 2026-05-20

### Changed

- Documented the package boundary after publishing `Lakona.Actor` and `Lakona.Actor.SourceGenerator` as standalone NuGet packages.
- Clarified that `Lakona.Actor` is the actor/mailbox runtime foundation for Lakona.Game; `Lakona.Game.Server` builds on it for game-session infrastructure, Lakona.Rpc hosting, endpoint binding, reconnect, and reliable push integration.

## 2026-05-13

### Changed

- Clarified cluster routing documentation so `realtime` remains an optional template/sample profile instead of a framework-wide concept.
- Added cluster node-to-node communication design notes for route lookup, local dispatch, remote dispatch, and pluggable node messenger adapters.
- Added Skynet-derived cluster design principles for explicit remote boundaries, overload results, trace propagation, large-message boundaries, and draining shutdown.
- Merged the standalone architecture draft into `CONTRIBUTING.md` and removed duplicate repository design notes.

## 2026-05-12

### Released

- `Lakona.Game.Abstractions` `0.1.1`
- `Lakona.Game.Client` `0.1.5`
- `Lakona.Game.Server` `0.1.5`
- `Lakona.Tool` `0.1.15`
- `Lakona.Tool` `0.1.16`
- `Lakona.Tool` `0.1.17`

### Changed

- Added `Lakona.Game.Abstractions` for cross-side framework-owned session, endpoint, reconnect, and reliable push primitives.
- Added `ILakonaGameServer` / `AddLakonaGameServer()` and `Lakona.GameClient` as the recommended single-entry APIs for server and client code.
- Added typed reliable push overloads on `ILakonaGameServer` so recommended server code can deliver through endpoint callbacks without handling `ReliablePushRecord`.
- Moved shared `GameSessionKey`, `GameEndpointName`, `ReliablePushSequence`, reliable push acknowledgement outcomes, and session resume outcomes out of server/client-only namespaces.
- Changed `Lakona.Tool` to generate its Lakona.Game runtime package version constants from the Server and Client project versions during build.
- Changed `Lakona.Tool` project templates to default to one RPC endpoint and require `--network-profile realtime` for separate control and realtime endpoints.
- Changed `Lakona.Tool` project initialization to add `Lakona.Game.Client` to generated Unity and Godot client projects.
- Added `Lakona.Tool new --persistence none|mysql|postgres`; MySQL/PostgreSQL profiles add Dapper plus the selected database provider package to generated server projects.

## 2026-05-11

### Released

- `Lakona.Game.Client` `0.1.4`
- `Lakona.Game.Server` `0.1.4`
- `Lakona.Tool` `0.1.14`

### Changed

- Added framework session lifecycle primitives, reconnect/state-lost outcomes, session-scoped reliable push acknowledgement helpers, and engine-neutral client session state helpers.
- Migrated Unity and Godot samples to `ReliablePushInbox`.
- Updated `Lakona.Tool` package version constants for generated projects.

## 2026-05-09

### Released

- `Lakona.Tool` `0.1.11`
- `Lakona.Tool` `0.1.12`
- `Lakona.Tool` `0.1.13`

### Changed

- Updated generated local `lakona-rpc.starter` tool manifests and Godot verification to use `0.2.57`.
- Updated generated local `lakona-rpc.starter` tool manifests and Godot verification to use `0.2.58`.
- Preserved `Lakona.Rpc.*` package references from starter-generated projects instead of rewriting their versions in Lakona.Game templates.
- Documented the `lakona-starter` ownership boundary for Lakona.Game contributors.

## 2026-05-08

### Released

- `Lakona.Tool` `0.1.10`

### Changed

- Updated generated local `lakona-rpc.starter` tool manifests to use `0.2.53`.
- Suppressed delegated `lakona-starter` next-step output during `lakona new` so the command only prints the final Lakona.Game next steps.

## 2026-05-07

### Released

- `Lakona.Game.Client` `0.1.3`
- `Lakona.Tool` `0.1.7`
- `Lakona.Tool` `0.1.8`
- `Lakona.Tool` `0.1.9`

### Changed

- Removed Unity package metadata from `Lakona.Game.Client`; it is now consumed as a NuGet package only, matching the `Lakona.Rpc.Client` layout.
- Updated Unity and Godot samples to consume `Lakona.Game.Client` through NuGet.
- Updated Godot sample projects and generated tool templates to avoid MSBuild multi-target project races during default restore/build.
- Limited Godot server logging to console output to avoid Windows EventLog permission failures in non-elevated runs.
- Updated Godot client generation in `Lakona.Tool` to preserve generated RPC clients and create a real networked Ping example.
- Updated `Lakona.Tool` project scaffolding to expose the generated client-facing server as `Server/Server/Server.csproj`, while keeping the then-current `Server/Silo/Silo.csproj` state-process layout.

## Lakona.Actor History

## 0.5.0 - 0.5.16

### Changed

- **Actor system lifecycle API** (breaking): `ActorSystem` is now async-only. Use `await using ActorSystem system = new();` and `await system.SpawnAsync<TMessage>(...)`.
- **Generated typed spawn extensions** (breaking): generated spawn helpers now use the `SpawnXxxAsync(...)` naming shape and return `ValueTask<ActorHandle<TMessage>>`.
- **ActorSystem internals**: diagnostics publishing, actor registration, message delivery, spawning, stopping, lookup, and call handling were separated into focused internal components.
- **ActorCell internals**: per-message dispatch, stop sequencing, timer ownership, timer creation, and response handling were separated into focused internal components.
- **Actor call pipeline**: call queueing, queue-timeout diagnostics, response waiting, response-timeout diagnostics, and response type conversion were consolidated into dedicated internal call components.
- **Runtime internal layout**: actor runtime internals were organized into responsibility-based directories for dispatch, lifecycle, registry, diagnostics, and timer ownership.

### Removed

- **Sync-over-async lifecycle paths** (breaking): removed synchronous `ActorSystem.Spawn(...)` and `ActorSystem.Dispose()` so startup hooks, mailbox drain, and disposal remain explicitly asynchronous.

### Fixed

- Prevented timers scheduled during actor stopping from surviving stop cleanup and publishing post-stop dead letters.
- Ensured actor call failures are observed after after-interceptor callbacks have completed.

## 0.4.0

### Added

- **Actor handle**: `ActorHandle<TMessage>` separates owner/admin operations from message-only `ActorRef<TMessage>`.
- **Actor state machine**: `ActorState` enum (`Active`, `Draining`, `Dead`) exposed via `ActorHandle.GetState()` and `ActorSystem.GetActorState()`. Actors now have an explicit, queryable lifecycle.
- **Message interceptor hooks**: `IActorMessageInterceptor` with `OnBeforeMessage` and `OnAfterMessage` callbacks, configured per-`ActorSystem` via `ActorSystemOptions.MessageInterceptor`. Enables message recording, replay, and custom diagnostics without modifying the runtime.
- **Observer error diagnostics**: `ActorSystem.ObserverErrorPublished` reports failures from diagnostic event handlers and message interceptors without changing actor message execution.
- **Actor call options**: `ActorCallOptions` gives `Call<T>` separate queue and response timeout budgets.
- **Design philosophy documentation**: `docs/design-philosophy.md` documents the Skynet-influenced principles behind the runtime.

### Changed

- **Spawn API** (breaking): `ActorSystem.Spawn(...)` and generated typed spawn extensions now return `ActorHandle<TMessage>`. Use `handle.Ref` when passing a message-only actor reference to other code.
- **Diagnostic events** (breaking): `DeadLetter`, `SlowMessage`, and `ActorCallTimeout` now expose message/request type names instead of the original message or request payload.
- **Message interceptor errors**: `IActorMessageInterceptor` exceptions are now reported through `ObserverErrorPublished` and no longer fail actor message dispatch.
- **Stop flow**: Actor removal from the registry now happens *after* the mailbox drain completes, so `GetActorState()` correctly reports `Draining` during the drain window.
- **Graceful stopping**: `IActorStopping<TMessage>` now runs as the final mailbox turn during explicit stop. The hook no longer runs concurrently with an in-flight message, and drain timeouts leave the actor in `Draining` until the stop sequence actually completes.
- **Call timeout API** (breaking): `ActorRef<TMessage>.Call<TResponse>` now accepts `ActorCallOptions` instead of a single `TimeSpan`. Queue backpressure timeout and response timeout are handled independently.
- **Call timeout diagnostics** (breaking): `ActorCallTimeout` now exposes `QueueTimeout`, `ResponseTimeout`, and `Elapsed` instead of a single `Timeout`.

### Removed

- **ActorRef management APIs** (breaking): Removed `Stop(...)`, `GetState()`, and `GetMailboxMetrics()` from `ActorRef<TMessage>`. Keep the `ActorHandle<TMessage>` returned by spawn for lifecycle and diagnostics.
- **ActorHandle implicit conversion** (breaking): Removed implicit conversion from `ActorHandle<TMessage>` to `ActorRef<TMessage>`. Use `handle.Ref` explicitly when passing a message-only actor reference.
- **`ActorContext<TMessage>.System`** (breaking): Removed direct `ActorSystem` access from actor handlers. Pass dependencies explicitly through constructor parameters, actor refs, or messages instead of using actor context as a service locator.
- **`ActorSystemOptions.ExecutionTimeout`** (breaking): Removed preemptive message execution timeout because timing out a handler with `WaitAsync` allowed the mailbox to advance while the original actor turn could still be running. Slow or stuck actor turns should be diagnosed through slow-message telemetry and handled by application-level shutdown or process supervision.
- **`ActorCallTimeoutReason.CircularWait`** (breaking): Circular actor call chains now throw `InvalidOperationException` synchronously before any message is queued, rather than waiting for a timeout. Circular calls are a design error, not a runtime condition.

## 0.2.3 - 2026-05-26

### Changed

- Aligned internal namespaces with runtime and source generator directory structure while preserving the public runtime API namespace.
- Added a focused test solution under `tests/test.slnx`.

## 0.2.2 - 2026-05-26

### Changed

- Organized runtime and source generator code files into responsibility-based directories.

## 0.2.1 - 2026-05-26

### Fixed

- Fixed a lifecycle stop test assertion that depended on mailbox scheduling timing.

## 0.2.0 - 2026-05-26

### Added

- Added explicit mailbox backpressure results through `ActorRef<TMessage>.TrySend(...)` and `ActorSendResult`.
- Added rejected mailbox metrics and dead-letter publication for failed immediate sends.
- Added structured call-timeout diagnostics through `ActorSystem.CallTimedOut`, including caller, target, timeout reason, request, and actor call chain.
- Added bounded stop/drain overloads that return `ActorStopResult`.
- Added optional lifecycle hooks with `IActorStarted<TMessage>` and `IActorStopping<TMessage>`.
- Added runtime observability through .NET `Meter` counters/gauges for message delivery, calls, timeouts, dead letters, and mailbox queue length.
- Added slow-message trace events and activity context propagation through `Send`, `Call<T>`, timers, and generated actor clients.
- Added guidance and tests for safe long-running work that resumes actors by posting completion messages back through the mailbox.
- Extended analyzer coverage for common blocking APIs inside actor types.

### Changed

- Reworked README and contributor documentation around design principles, runtime boundaries, and maintenance rules.
- Clarified that Lakona.Actor remains a process-local actor/mailbox runtime and does not provide distributed actor, cluster, RPC, transport, persistence, or gameplay concepts.

### Removed

- Removed `ActorGroup<TMessage>` and `ActorSystem.CreateGroup(...)` from the core runtime. Batch grouping and broadcast semantics should live in Lakona.Game or application code.

## Lakona.Rpc History

## 0.12.0 / 0.2.0 / 0.4.0

- Release packages:
	- `Lakona.Rpc.Core` `0.12.0`
	- `Lakona.Rpc.Client` `0.12.0`
	- `Lakona.Rpc.Server` `0.12.0`
	- `Lakona.Rpc.Analyzers` `0.2.0`
	- `Lakona.Rpc.Starter` `0.4.0`
- Raised the package line after the API stability, notification naming, lifecycle, error model, generated facade naming, and server API boundary changes.
- Updated the starter release manifest so newly scaffolded projects use the `0.12.0` runtime packages and `0.2.0` analyzer package.

## 0.11.13 / 0.3.7

- Release packages:
	- `Lakona.Rpc.Server` `0.11.13`
	- `Lakona.Rpc.Starter` `0.3.7`
- Marked runtime-internal server APIs such as `RpcSession`, low-level handler delegates, and registry mutation entry points with `EditorBrowsable(Never)`.
- Added tests that keep runtime-internal server APIs hidden from normal IntelliSense.
- Updated the starter release manifest to reference `Lakona.Rpc.Server` `0.11.13`.

## 0.11.12 / 0.3.6

- Release packages:
	- `Lakona.Rpc.Server` `0.11.12`
	- `Lakona.Rpc.Starter` `0.3.6`
- Updated Server package README guidance so regular applications use `RpcServerHostBuilder` instead of direct `RpcSession` construction.
- Added public API layer documentation that separates stable user APIs, stable extension APIs, generated-support APIs, runtime internals, and protocol infrastructure.
- Updated the starter release manifest to reference `Lakona.Rpc.Server` `0.11.12`.

## 0.11.12 / 0.11.11

- Release packages:
	- `Lakona.Rpc.Core` `0.11.12`
	- `Lakona.Rpc.Server` `0.11.11`
- Replaced the ambiguous `RpcStatus.Exception` with framework-specific statuses: `HandlerError`, `Overloaded`, `BadRequest`, and `ProtocolError`.
- Updated server request dispatch so handler failures return `HandlerError` and request queue saturation returns `Overloaded`.
- Documented that `RpcStatus` is framework-only; business failures belong in business DTOs.

## 0.11.11 / 0.1.8

- Release packages:
	- `Lakona.Rpc.Core` `0.11.11`
	- `Lakona.Rpc.Server` `0.11.10`
	- `Lakona.Rpc.Analyzers` `0.1.8`
- Added `RpcServiceAttribute.ApiGroup` and `RpcServiceAttribute.ApiName` so long-lived projects can explicitly lock generated `client.Api.<group>.<service>` names.
- Changed source generation to fail fast on duplicate generated API service names instead of producing ambiguous facade properties.
- Clarified lifecycle semantics: runtime/client and session objects are single-use, while cleanup methods remain idempotent.
- Documented explicit generated API naming guidance for projects that want stable facade names across namespace or interface refactors.

## 0.11.10 / 0.11.6 / 0.1.7

- Release packages:
	- `Lakona.Rpc.Core` `0.11.10`
	- `Lakona.Rpc.Client` `0.11.6`
	- `Lakona.Rpc.Analyzers` `0.1.7`
- Renamed the user-facing server-to-client API from push/callback terminology to notification terminology:
	- `[RpcNotificationContract]`
	- `[RpcNotification]`
	- `RpcNotificationMethod<T>`
	- `RegisterNotificationHandler(...)`
	- `SendNotificationAsync(...)`
- Changed notification handler registration to use `Func<T, ValueTask>` as the core handler shape, while keeping a synchronous convenience overload on `RpcClientRuntime`.
- Made duplicate notification handler registration fail fast instead of replacing the existing handler.
- Added observable notification failure events for unhandled notification frames and notification handler exceptions.
- Updated source generation to support `[RpcNotification]` methods returning either `void` or `ValueTask`.

## 0.11.9 / 0.11.5

- Release packages:
	- `Lakona.Rpc.Core` `0.11.9`
	- `Lakona.Rpc.Client` `0.11.5`
- Documented the Lakona.Rpc wire protocol v1 envelope format and added golden byte tests for request, response, push, and keepalive frames.
- Added `RpcException` as the dedicated client-side exception for non-OK remote RPC responses, carrying status, error message, request id, service id, and method id.
- Updated `RpcClientRuntime` to throw `RpcException` for non-OK remote responses.

## 0.3.3

- Release packages:
	- `Lakona.Rpc.Starter` `0.3.3`
- Removed starter support for the deleted client engine path from CLI parsing, interactive prompts, template generation, dependency planning, CI verification, tests, and documentation.

## 0.11.12

- Release packages:
	- `Lakona.Rpc.Transport.Kcp` `0.11.12`
- Fixed a KCP server transport dispose race where `AcceptAsync` could observe a queued connection as connected while it was already being disposed.

## 0.3.1

- Release packages:
	- `Lakona.Rpc.Starter` `0.3.1`
- Added automatic CLI language detection for `Lakona.Rpc.Starter`.
- Added Simplified Chinese and Traditional Chinese output for usage text, interactive prompts, validation errors, and post-create next steps.

## 0.11.8 / 0.1.6 / 0.3.0

- Release packages:
	- `Lakona.Rpc.Core` `0.11.8`
	- `Lakona.Rpc.Analyzers` `0.1.6`
	- `Lakona.Rpc.Starter` `0.3.0`
- Added an assembly-level `Lakona.RpcGenerateClient` marker for selecting the one client assembly that should receive source-generated RPC client glue.
- Updated Unity-compatible starters to write the marker into `Assembly-CSharp`, preventing duplicate `Rpc.Generated` output across Unity script assemblies.
- Suppressed client auto-detection for Unity compilations without the marker so Unity asmdefs that reference `Lakona.Rpc.Client` do not generate duplicate facades.
- Removed the legacy starter codegen command surface and the deleted `Lakona.Rpc.CodeGen` package route from starter workflow documentation.
- Added source-generator focused tests for generated client/server glue, referenced contract assemblies, Unity client markers, and contract id diagnostics.

## 0.1.4 / 0.2.65

- Release packages:
	- `Lakona.Rpc.Analyzers` `0.1.4`
	- `Lakona.Rpc.Starter` `0.2.65`
	- `Lakona.Rpc.Transport.Kcp` `0.11.11`
- Changed `Lakona.Rpc.Analyzers` to use the Unity-compatible Roslyn 3.8 source generator API surface with a single `ISourceGenerator` implementation.
- Documented that new starter projects must use the source-generator route and must not fall back to starter-scaffolded generated source or `Lakona.Rpc.CodeGen`.
- Added the planned `Lakona.Rpc.CodeGen` removal roadmap, gated by source-generator coverage and Unity/Tuanjie validation.
- Updated the starter release manifest to reference `Lakona.Rpc.Analyzers` `0.1.4` and `Lakona.Rpc.Transport.Kcp` `0.11.11`.

## 0.1.2 / 0.2.63

- Release packages:
	- `Lakona.Rpc.Analyzers` `0.1.2`
	- `Lakona.Rpc.Starter` `0.2.63`
- Switched new starter projects from starter-scaffolded CLI codegen hooks to Roslyn source generation through `Lakona.Rpc.Analyzers`.
- Added source-generated client facade, service client, callback binder, server binder, callback proxy, and `AllServicesBinder` output with explicit client/server generation properties plus runtime-based mode detection.
- Removed new-project reliance on local `Lakona.Rpc.CodeGen` tool manifests, generated source directories, MSBuild `Lakona.RpcGenerateCode` targets, and Unity `Lakona.RpcCodeGenEditor` scripts.
- Kept `lakona-starter codegen` and direct `Lakona.Rpc.CodeGen` as legacy repair and migration paths for existing hook-based projects.
- Updated starter and public docs so source generation is the primary workflow and generated files are no longer part of new-project guidance.

## 0.2.61

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.61`

- Centralized starter dependency ownership in `StarterDependencyPlanner` and moved Shared / Server / Unity / Godot package selection through that planner.
- Added dependency matrix tests for JSON and MemoryPack starter projects across Server, Godot, Unity, and Tuanjie generation paths.

## 0.16.8 / 0.2.57

- Release packages:
	- `Lakona.Rpc.CodeGen` `0.16.8`
	- `Lakona.Rpc.Starter` `0.2.57`

- Changed generated client facade boundaries so `RpcClient`, `RpcCallbackBindings`, and `RpcApi` now live in the configured generated namespace instead of occupying `Lakona.Rpc.Client` by default.
- Removed the temporary legacy generated `Lakona.Rpc.Client.RpcClient` facade path; generated clients now live only in the configured generated namespace.
- Moved stable starter template files into embedded template resources and added golden file coverage for generated Godot project files.
- Reworked `ProcessRunner` to read stdout/stderr concurrently, use async process waiting, and support timeout/cancellation failure paths.

## 0.11.10 / 0.16.6 / 0.2.55

- Release packages:
	- `Lakona.Rpc.Client` `0.11.4`
	- `Lakona.Rpc.CodeGen` `0.16.6`
	- `Lakona.Rpc.Core` `0.11.5`
	- `Lakona.Rpc.Starter` `0.2.55`
	- `Lakona.Rpc.Transport.Kcp` `0.11.10`
	- `Lakona.Rpc.Transport.Tcp` `0.11.4`
	- `Lakona.Rpc.Transport.WebSocket` `0.11.6`
- Introduced shared `RpcProtocolLimits` defaults so RPC payload, transport frame, and security decompression limits all resolve from one source.
- Added first-class client-side security configuration via `RpcClientOptions.UseSecurity(...)`, matching the existing server builder security entry point while preserving direct `TransformingTransport` usage.
- Updated generated facades and starter templates so client/server security configuration is represented through symmetric public APIs.
- Documented `ITransport.ConnectAsync` semantics for client transports, accepted server transports, and no-op/in-memory transports, with contract tests covering idempotent initialization.

## 0.2.53

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.53`
- Added `--no-next-steps` to `lakona-starter new` so automation scripts can create starter projects without printing the post-create "Next steps" guidance.

## Repository metadata refresh

- Release packages:
	- `Lakona.Rpc.Client` `0.11.2`
	- `Lakona.Rpc.CodeGen` `0.16.5`
	- `Lakona.Rpc.Core` `0.11.3`
	- `Lakona.Rpc.Serializer.Json` `0.11.1`
	- `Lakona.Rpc.Serializer.MemoryPack` `0.11.1`
	- `Lakona.Rpc.Server` `0.11.8`
	- `Lakona.Rpc.Starter` `0.2.52`
	- `Lakona.Rpc.Transport.Kcp` `0.11.9`
	- `Lakona.Rpc.Transport.Loopback` `0.11.1`
	- `Lakona.Rpc.Transport.Tcp` `0.11.3`
	- `Lakona.Rpc.Transport.WebSocket` `0.11.4`
- Added NuGet repository and project URL metadata pointing to `https://github.com/bruce48x/Lakona.Rpc` so package pages expose the source repository.

## 0.2.51

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.51`
- Added `--help` / `-h` handling to `lakona-starter` so usage text prints successfully without starting interactive project creation.

## 0.2.50

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.50`
- Repositioned `Lakona.Rpc.Starter` as a project management tool instead of only a one-time initializer.
- Added `lakona-starter new` and `lakona-starter codegen` subcommands so starter-managed workspaces can regenerate both server and client code from one CLI entry point.
- Starter-generated projects no longer emit root `codegen.ps1` / `codegen.sh`; shared contract regeneration now goes through `lakona-starter codegen`.

## 0.2.49

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.49`
- Improved the generated Godot starter `RpcConnectionTester` so it logs scene-entry and ready-state milestones before attempting the default connection.
- The generated Godot starter test node now exits explicitly after success or failure, which makes headless CI validation deterministic for flows like `websocket + memorypack`.
- Added a dedicated daily GitHub Actions workflow that generates a fresh Godot starter project, launches the server and client, and verifies real network communication.

## 0.2.48

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.48`
- Added a dedicated `unity-cn` starter client option.
- Changed the default `NuGetForUnity` source selection so `unity` uses `openupm`, while `unity-cn` and `tuanjie` use the embedded local package by default.
- Kept `--nugetforunity-source embedded|openupm` as an explicit override on top of those client-specific defaults.

## 0.2.47

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.47`
- Tuanjie starter projects now write `Client/ProjectSettings/ProjectVersion.txt` using the real Tuanjie format: Unity base editor version `2022.3.61t11`, matching revision `122146d53e32`, plus `m_TuanjieEditorVersion: 1.6.10`.

## 0.2.46

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.46`
- Tuanjie starter projects now generate `Client/Assets/NuGet.config` with the China NuGet V3 endpoint `https://nuget.cdn.azure.cn/v3/index.json` to reduce package restore failures from mainland China networks.

## 0.2.45

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.45`
- Tuanjie starter projects now write `Client/ProjectSettings/ProjectVersion.txt` with Tuanjie version `1.6.10` instead of reusing the Unity 2022 LTS editor version string.

## 0.2.44

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.44`
- Starter-generated projects now include root `codegen.ps1` and `codegen.sh` helpers so DTO or service-contract changes under `Shared/` can regenerate both server and client code in one command.
- The generated regeneration scripts choose the correct client codegen target automatically for Unity / Tuanjie versus Godot.

## 0.2.43

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.43`
- Updated the starter's bundled `Lakona.Rpc.Transport.Kcp` dependency to `0.11.8`.
- Refreshed the starter's pinned Unity/NuGet dependency baseline to the latest stable package versions used by the generated `packages.config` files, including current `System.Text.Json`, `System.IO.Pipelines`, Roslyn, and BCL support packages.

## 0.11.8

- Release packages:
	- `Lakona.Rpc.Transport.Kcp` `0.11.8`
- Added an explicit `KcpTransport(string host, int port, uint conversationId)` overload so clients can connect with a server-assigned `conv` instead of always generating one locally.
- Added optional KCP handshake admission hooks via `KcpHandshakeAdmission` and new `KcpConnectionAcceptor` / `KcpListener` overloads so servers can validate or reserve incoming `conv` values before establishing a session.

## 0.2.41

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.41`
- Added Tuanjie client support to `Lakona.Rpc.Starter`.
- `Lakona.Rpc.Starter` now supports `--client-engine tuanjie` plus `unity-china` / `unitycn` aliases, reusing the existing Unity-compatible client template and codegen flow.
- Updated starter help and readme text so Unity-compatible client scaffolding explicitly covers Tuanjie in addition to Unity.

- Fixed Godot starter generation so scaffolded projects now include a root `.gitattributes` file with LF normalization for source assets and LFS/binary rules for common Godot project binaries.

## 0.16.3 / 0.2.39

- Release packages:
	- `Lakona.Rpc.CodeGen` `0.16.3`
	- `Lakona.Rpc.Starter` `0.2.39`
- Added `--version` to `lakona-rpc-codegen` so the tool can print its package version and exit without requiring any other arguments.
- Added `--version` to `lakona-starter` so the tool can print its package version and exit before any interactive prompts or template generation.

## 0.2.38

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.38`
- Updated the generated Godot client baseline from Godot `4.4` to Godot `4.6`.
- The generated `project.godot` now writes `config/features=PackedStringArray("4.6", "C#")`.
- The generated `Client.csproj` now falls back to `Godot.NET.Sdk/4.6.1` when no local SDK package source is detected.
- Updated starter help/readme text so Godot scaffolding consistently refers to Godot `4.6`.

## 0.2.37

- Reverted the unreleased source-side Unity `memorypack` formatter-registration experiment that had been added after `0.2.36`.
- The explicit `SharedMemoryPackRegistration` approach did not reliably fix fresh Unity starter projects in real user validation, so it is not being kept in source as the current direction.
- The Unity fresh-start `memorypack` issue is now documented as known-but-deferred and should be revisited later with a different approach.

## 0.2.36

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.36`
- Fixed Unity starter `memorypack` runtime startup so newly generated clients explicitly register generated `MemoryPack` formatters before constructing the RPC client.
- The generated shared project now emits a `SharedMemoryPackRegistration` helper, and the generated Unity tester calls it before opening the default connection.
- This removes the first-run Unity failure where starter-generated `PingRequest` / `PingReply` types could still throw `MemoryPackSerializationException: ... is not registered in this provider` even though package restore and source generation had succeeded.

## 0.2.35

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.35`
- Fixed Unity starter `memorypack` package restore again so generated `Assets/packages.config` now references the correct Roslyn package set for `MemoryPack.Generator`.
- The generated Unity package list now uses `Microsoft.CodeAnalysis.Common` instead of the wrong umbrella `Microsoft.CodeAnalysis` package, and also includes the missing `System.Reflection.Metadata` plus related runtime dependencies required for Unity's first import.
- This removes the first-import Unity errors where `MemoryPack.Generator.dll` and `Microsoft.CodeAnalysis.CSharp.dll` still failed to load even after the previous `0.2.34` fix.

## 0.2.34

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.34`
- Fixed Unity starter `memorypack` package restore so generated `Assets/packages.config` now includes the Roslyn dependencies required by `MemoryPack.Generator`.
- This removes the first-import Unity error where `MemoryPack.Generator.dll` could not resolve `Microsoft.CodeAnalysis` / `Microsoft.CodeAnalysis.CSharp` and the project entered Safe Mode on initial open.

## 0.11.2 / 0.2.33

- Release packages:
	- `Lakona.Rpc.Transport.Tcp` `0.11.2`
	- `Lakona.Rpc.Starter` `0.2.33`
- Fixed TCP server-side accepted-connection handling so freshly accepted `TcpServerTransport` instances report a connected state before `RpcSession.StartAsync()` calls `ConnectAsync()`.
- This fixes a runtime bug where `BoundedConnectionAcceptor` could treat a newly accepted TCP connection as stale and dispose it before the server session started.
- In practice, this restores starter-generated `tcp + memorypack` and `tcp + json` flows where the client could connect successfully but then hang on the first RPC call.
- Updated `Lakona.Rpc.Starter`'s bundled release manifest so newly scaffolded TCP projects reference `Lakona.Rpc.Transport.Tcp` `0.11.2`.

## 0.2.32

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.32`
- Fixed Godot starter `memorypack` projects so the generated `Shared.csproj` now targets `net8.0;net10.0` instead of `netstandard2.1;net10.0`.
- Fixed MemoryPack starter contracts so the generated shared project explicitly references `MemoryPack` and `MemoryPack.Generator`.
- This removes the Godot runtime failure where generated `MemoryPack` DTOs could throw `System.TypeLoadException: Virtual static method 'Serialize' is not implemented` while loading `Shared.Interfaces.PingReply`.

## 0.2.31

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.31`
- Fixed Godot starter C# project binding so newly generated `project.godot` files now set `[dotnet] project/assembly_name="Client"` to match the generated `Client.csproj` / `Client.dll`.
- Fixed Godot starter client output so runtime package dependencies are copied into Godot's load directory and local restores are not blocked by NuGet audit checks in restricted environments.
- This removes the runtime error where Godot reported that the associated C# class could not be found for `res://Scripts/Rpc/Testing/RpcConnectionTester.cs` even though the script file and class existed.

## 0.2.30

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.30`
- Fixed Godot starter script binding so the generated `RpcConnectionTester.cs` now declares a top-level `RpcConnectionTester` class that Godot can instantiate from `Main.tscn`.
- This removes the runtime error where Godot reported that the associated C# class could not be found for `res://Scripts/Rpc/Testing/RpcConnectionTester.cs`.

## 0.2.29

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.29`
- Fixed Godot starter runtime behavior so the default connection example now defers auto-connect until the scene is ready, matching the expected Unity starter flow more closely.
- This restores the generated Godot project's default behavior of creating a client connection and issuing the starter `Ping` request automatically on Play.

## 0.2.28

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.28`
- Fixed Godot starter generation so `project.godot` no longer writes the selected transport and serializer into `config/features`.
- This removes false unsupported-feature warnings when opening newly scaffolded Godot projects with combinations like `websocket + memorypack` or `kcp + json`.

## 0.16.2 / 0.2.27

- Release packages:
	- `Lakona.Rpc.CodeGen` `0.16.2`
	- `Lakona.Rpc.Starter` `0.2.27`
- Added Godot 4.x client support to `Lakona.Rpc.CodeGen`.
- `Lakona.Rpc.CodeGen` now supports `--mode godot`, detects Godot projects via `project.godot`, defaults generated output to `Scripts/Rpc/Generated`, and keeps Unity-only `.asmdef` emission scoped to Unity projects.
- Added Godot 4.x client scaffolding to `Lakona.Rpc.Starter`.
- `Lakona.Rpc.Starter` now supports `--client-engine unity|godot`, prompts for the client engine interactively when omitted, and generates either the existing Unity skeleton or a Godot 4.x C# client skeleton.
- Updated `Lakona.Rpc.Starter`'s bundled `Lakona.Rpc.CodeGen` version so new starter projects install the Godot-capable generator by default.

## 0.2.26

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.26`
- Changed the Unity generated client namespace used by `Lakona.Rpc.Starter` from `Client.Generated` to `Rpc.Generated`.
- This keeps the scaffolded tester script, the explicit `--namespace` passed to `Lakona.Rpc.CodeGen`, and the generated output path `Assets/Scripts/Rpc/Generated` on the same naming convention.

## 0.2.25

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.25`
- Changed the Unity client codegen output path used by `Lakona.Rpc.Starter` from `Assets/Scripts/Rpc/RpcGenerated` to `Assets/Scripts/Rpc/Generated`.
- This aligns the scaffolded folder layout with `Lakona.Rpc.CodeGen`'s own default Unity output path and avoids carrying two naming conventions for the same generated client code.

## 0.2.24

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.24`
- Fixed Unity starter generation so it no longer writes a duplicate `Lakona.Rpc.Generated.asmdef` alongside the asmdef already emitted by `Lakona.Rpc.CodeGen`.
- Before this fix, newly scaffolded Unity projects could fail on first open with `Assembly with name 'Lakona.Rpc.Generated' already exists` because both `Assets/Scripts/Rpc/Generated` and `Assets/Scripts/Rpc/RpcGenerated` contained the same assembly name.

## 0.11.7 / 0.11.3 / 0.11.2 / 0.11.1 / 0.16.1 / 0.2.23

- Release packages:
	- `Lakona.Rpc.Server` `0.11.7`
	- `Lakona.Rpc.Transport.Kcp` `0.11.7`
	- `Lakona.Rpc.Transport.WebSocket` `0.11.3`
	- `Lakona.Rpc.Transport.Tcp` `0.11.1`
	- `Lakona.Rpc.Core` `0.11.2`
	- `Lakona.Rpc.CodeGen` `0.16.1`
	- `Lakona.Rpc.Starter` `0.2.23`
- Updated `Lakona.Rpc.Starter`'s bundled release manifest so newly scaffolded projects pin the current in-repo package versions instead of older package revisions.

## 0.11.3

- Release packages:
	- `Lakona.Rpc.Transport.WebSocket` `0.11.3`
- Fixed WebSocket accept queue hygiene so `AcceptAsync()` no longer returns queued transports that already disconnected before the server drained the queue.
- Hardened WebSocket transport disposal so server-side teardown does not hang forever when the remote peer disappears without completing the close handshake.
- Before this fix, a client could complete the WebSocket upgrade, get queued for acceptance, disconnect, and still be handed to the runtime on the next `AcceptAsync()` call. That surfaced as immediate receive failures against what should have been a fresh accepted connection.
- Before this fix, `WsTransportFraming.DisposeAsync(...)` also used an unbounded `CloseAsync(...)` wait, so disposing a half-dead WebSocket could stall shutdown and prevent the acceptor from draining stale queued connections.
- `WsConnectionAcceptor.AcceptAsync()` now skips stale queued WebSocket transports, disposes them, and keeps reading until it finds a live connection or the caller cancels.
- `WsTransportFraming.DisposeAsync(...)` now bounds the close-handshake wait and aborts the socket if the peer is no longer cooperating.
- Tests:
	- Added a regression test proving a disconnected queued WebSocket transport is skipped instead of being returned from `AcceptAsync()`.
	- Added a regression test proving disposing a server-side WebSocket transport still completes promptly after the remote peer aborts.
- Compatibility:
	- The public API and wire protocol are unchanged.
	- The change only tightens server-side WebSocket acceptance semantics.

## 0.11.7

- Release packages:
	- `Lakona.Rpc.Server` `0.11.7`
- Fixed bounded accept queue hygiene so the server no longer hands disconnected queued connections to the runtime.
- Before this fix, `BoundedConnectionAcceptor` could return a connection that had already died while waiting in the server's pending-accept queue. That let the host spin up session state for a transport that was already disconnected.
- `BoundedConnectionAcceptor.AcceptAsync()` now skips stale queued connections and disposes them before continuing to the next live connection.
- Tests:
	- Added a regression test proving a disconnected queued connection is skipped instead of being returned from `AcceptAsync()`.
- Compatibility:
	- The public API and wire protocol are unchanged.
	- The change only tightens server-side acceptance semantics.

## 0.11.7

- Release packages:
	- `Lakona.Rpc.Transport.Kcp` `0.11.7`
- Fixed KCP accept queue hygiene so `AcceptAsync()` no longer returns connections that already died while they were still waiting in the pending-accept queue.
- Before this fix, a connection could finish the KCP handshake, get queued for acceptance, then fail before the server drained the queue. The next `AcceptAsync()` call could receive that already-disposed transport and hand a dead connection to the server runtime.
- `KcpListener.AcceptAsync()` now skips stale queued transports and keeps reading until it finds a live connection or the caller cancels.
- Tests:
	- Added a regression test proving that disposed queued KCP connections are not returned from `AcceptAsync()`.
- Compatibility:
	- The wire protocol and public API are unchanged.
	- The change only tightens runtime correctness for queued KCP accepts.

## 0.11.6

- Release packages:
	- `Lakona.Rpc.Transport.Kcp` `0.11.6`
- Fixed KCP listener fault isolation so a single broken session no longer takes down the entire listener loop.
- Before this fix, if `KcpServerTransport.ProcessDatagram(...)` failed for one accepted connection, the exception escaped from `KcpListener.ReceiveLoopAsync()`. That stopped all future accepts and even caused `KcpListener.DisposeAsync()` to rethrow the background failure during shutdown.
- `KcpListener` now isolates per-session datagram processing failures, disposes only the offending session, and keeps listening for new connections.
- Hardened `KcpServerTransport.DisposeAsync()` to be idempotent so listener-driven session disposal and upper-layer connection teardown can safely converge on the same transport instance.
- Tests:
	- Added a regression test proving one session-processing failure does not prevent later KCP connections from being accepted.
	- Added regression coverage that `KcpServerTransport.DisposeAsync()` is safe to call multiple times.
- Compatibility:
	- The wire protocol is unchanged.
	- The fix applies to the KCP package runtime without altering the public API surface.

## 0.11.5

- Release packages:
	- `Lakona.Rpc.Transport.Kcp` `0.11.5`
- Fixed the Unity-compatible `netstandard2.1` KCP client receive path so cancellation now stops blocked reads promptly instead of waiting for another UDP packet to arrive.
- Before this fix, `KcpTransport.ReceiveFrameAsync(...)` used uncancellable `Socket.ReceiveFromAsync(...)` calls outside the `NET8+` branch. In practice that meant Unity 2022 clients could stay hung in KCP receive during shutdown, disconnect, or timeout handling.
- Reworked the non-`NET8+` KCP receive paths to use a cancellation-aware polling receive loop for both the main data path and the handshake acknowledgement wait, so Unity-side teardown no longer depends on disposing the socket from another path just to break a blocked receive.
- Tests:
	- Added a regression test that guards the `netstandard2.1` source path against reintroducing uncancellable `ReceiveFromAsync(...)` calls.
- Compatibility:
	- The wire protocol is unchanged.
	- Server-side `net10.0` behavior is unchanged; the fix is specifically for Unity-compatible `netstandard2.1` KCP runtime behavior.

## 0.11.4 / 0.11.2 / 0.11.1

- Release packages:
	- `Lakona.Rpc.Transport.Kcp` `0.11.4`
	- `Lakona.Rpc.Transport.WebSocket` `0.11.2`
	- `Lakona.Rpc.Core` `0.11.2`
	- `Lakona.Rpc.Transport.Tcp` `0.11.1`
- Fixed outbound frame-size validation so transports now reject oversized frames before putting them on the wire.
- Before this fix, `LengthPrefix.Pack(...)` and the TCP framing sender accepted payloads larger than the runtime's 64 MB frame limit, which meant the local sender appeared to succeed and the failure only surfaced later on the receiving side.
- That delayed failure path could turn a local API misuse into cross-peer runtime errors, including remote disconnects and avoidable transport churn under KCP, WebSocket, and TCP.
- Added shared frame-length validation in `Lakona.Rpc.Core.LengthPrefix` and enforced it in `TcpPipeFraming.SendFrameAsync(...)`, so Unity 2022 `netstandard2.1` and server-side `net10.0` builds now fail fast on the sending side with the same limit.
- Tests:
	- Added regression coverage proving `LengthPrefix.Pack(...)` now rejects oversized payloads locally.
	- Added regression coverage proving the TCP sender rejects oversized frames before writing them to the stream.
- Compatibility:
	- The wire format is unchanged.
	- The behavioral change is intentional: oversized frames are now rejected locally instead of being allowed onto the network and failing remotely.

## 0.11.6

- Release packages:
	- `Lakona.Rpc.Server` `0.11.6`
- Fixed a session-lifecycle bug where `RpcSession` kept waiting for in-flight handlers after the client connection had already closed.
- Before this fix, a client could disconnect in the middle of a slow or blocking RPC and leave that session stuck in shutdown, keeping request-budget slots occupied until the handler completed on its own.
- `RpcSession` now cancels its internal session token as soon as the transport closes or the receive loop faults, so in-flight handlers and keepalive work are asked to stop immediately before session teardown waits for them.
- Tests:
	- Added a regression test proving that a remote disconnect cancels an in-flight request and allows the server session to complete promptly.
- Compatibility:
	- The change is server-only and keeps the Unity 2022 `netstandard2.1` client/runtime surface unchanged while remaining compatible with the server-side `net10.0` target.

## 0.11.5 / 0.11.3 / 0.11.1 / 0.2.22

- Release packages:
	- `Lakona.Rpc.Server` `0.11.5`
	- `Lakona.Rpc.Transport.Kcp` `0.11.3`
	- `Lakona.Rpc.Transport.WebSocket` `0.11.1`
	- `Lakona.Rpc.Core` `0.11.1`
	- `Lakona.Rpc.Starter` `0.2.22`
- Security:
	- Fixed a pre-session connection admission bug that let `WebSocket` and `KCP` acceptors buffer unbounded pending connections before `RpcServerHost` could apply `MaxPendingAcceptedConnections`.
	- An attacker could exploit this to exhaust server memory, sockets, and per-connection runtime state with unauthenticated connection floods, especially against `KCP`, where each spoofable handshake could materialize a server transport immediately.
- Runtime changes:
	- Added `RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections` and moved the default pending-connection budget to a shared runtime constant used by the server and transport acceptors.
	- Hardened `WsConnectionAcceptor` so it rejects overflow before queuing another accepted connection and cleans up queued transports during shutdown.
	- Hardened `KcpListener` / `KcpConnectionAcceptor` so new sessions are only created when a pending-admission slot is available, and all failure paths release that slot correctly.
	- Added explicit `maxPendingAcceptedConnections` overloads to `WsConnectionAcceptor.CreateAsync(...)`, `KcpConnectionAcceptor`, and `KcpListener`, then updated the starter templates and checked-in server samples to wire them to `builder.Limits.MaxPendingAcceptedConnections`.
- Tests:
	- Added regression coverage proving `KcpListener` can no longer exceed the default pending-connection limit under a burst of handshake requests.
	- Added a regression guard preventing `WsConnectionAcceptor` from regressing back to an unbounded pending connection queue implementation.
- Compatibility:
	- The fix is validated against Unity 2022 compatible `netstandard2.1` builds and the server-side `net10.0` test solution.

## 0.11.4 / 0.11.2

- Release packages:
	- `Lakona.Rpc.Server` `0.11.4`
	- `Lakona.Rpc.Transport.Kcp` `0.11.2`
- This release focuses on removing small but persistent allocations from the real runtime hot paths instead of papering over them with broader caches or compatibility shims.
- Reworked KCP server session lookup so inbound datagrams are keyed by `(IPAddress, Port)` value semantics instead of `IPEndPoint.ToString()`, eliminating per-packet string allocation in the listener loop.
- Reworked KCP server receive waiting so `ReceiveFrameAsync` no longer allocates a linked `CancellationTokenSource` on every empty-queue wait cycle; shutdown still wakes waiters via the existing frame signal.
- Reworked server inflight-request draining so `TrackedTaskCollection.WaitAsync` now waits on a drained signal driven by the final completing task, instead of repeatedly snapshotting the tracked task set with `ToArray()`.
- Reworked the Unity-compatible KCP server send path so the `netstandard2.1` build no longer materializes outbound buffers with `mem.ToArray()`; it now prefers direct array segments and falls back to pooled copies only when required by the underlying memory owner.
- Design note:
	- The guiding rule for these fixes was to remove allocation sources at the point they occur in the transport/session hot path, while keeping the public runtime contract unchanged for Unity 2022 (`netstandard2.1`) and the server runtime (`net10.0`).
	- The KCP fixes deliberately mirror the more efficient client-side patterns that already existed in the repository, so the server and client transports now follow the same zero-extra-allocation strategy where the underlying APIs allow it.
	- The server shutdown fix avoids introducing another concurrent collection or background sweeper; instead it models the actual lifecycle directly: first tracked task creates a pending drain signal, last completing task resolves it.

## 0.11.3 / 0.2.21

- Release packages:
	- `Lakona.Rpc.Server` `0.11.3`
	- `Lakona.Rpc.Starter` `0.2.21`
- Fixed `RpcSession.RunAsync` so that cancelling the external `CancellationToken` now terminates active sessions promptly. Previously, `StartAsync` created an internal `CancellationTokenSource` unlinked from the caller's token, causing `RunAsync` to hang indefinitely when the transport had no EOF signal (e.g. KCP/UDP with connected clients).
- Fixed `RpcServerHost.RunAsync` to transfer ownership of the inner acceptor to `BoundedConnectionAcceptor` instead of holding a separate `await using` reference, which caused a double-`Dispose` and `ObjectDisposedException` on shutdown when no clients were connected.

## 0.2.20

- Release packages:
	- `Lakona.Rpc.Starter` `0.2.20`
- Fixed Unity starter `packages.config` generation to always include `System.Threading.Channels`, matching the direct runtime dependency now required by `Lakona.Rpc.Client` and WebSocket/KCP transports.
- Fixed the checked-in Unity samples that were still missing `System.Threading.Channels`, so restored sample clients load `Lakona.Rpc.Client.dll` cleanly under Unity again.

## 0.16.1 / 0.2.19

- Release packages:
	- `Lakona.Rpc.CodeGen` `0.16.1`
	- `Lakona.Rpc.Starter` `0.2.19`
- Fixed Unity client generation so `Lakona.Rpc.CodeGen` now emits a default `Lakona.Rpc.Generated.asmdef` when the Unity output folder does not already define its own assembly.
- The generated Unity asmdef now infers the nearest contracts assembly reference from `--contracts`, reducing manual Unity assembly wiring for generated client code.
- Fixed `Lakona.Rpc.Starter` so newly scaffolded Unity clients always include the generated runtime asmdef expected by the sample testing assembly layout.
- Refreshed the checked-in Unity samples so existing sample projects compile again after the recent generated-client API changes.

## 0.15.0 / 0.11.0

- Release packages:
	- `Lakona.Rpc.CodeGen` `0.15.0`
	- Runtime packages (`Lakona.Rpc.Core`, `Lakona.Rpc.Client`, `Lakona.Rpc.Server`, transports, serializers) `0.11.0`
- This release fixes a published package mismatch where `Lakona.Rpc.CodeGen 0.14.0` could still emit server binders that referenced the removed `IRpcSerializer.Serialize(...)` API.
- Fixed the published code generator so server binders now emit `SerializeFrame(...)` and `RpcEnvelopeCodec.EncodeResponse(...)` instead of referencing the removed `IRpcSerializer.Serialize(...)` API.
- Removed `IRpcSerializer.Serialize(...)` from the runtime surface and standardized serializers on pooled `TransportFrame` output to eliminate extra response allocations and copies.
- Changed generated server registry handlers to operate on `RpcRequestFrame` and return encoded `TransportFrame` responses directly, matching the new runtime contract.
- Removed the redundant inner send semaphore from TCP framing so each TCP send is serialized exactly once.
- Refreshed the checked-in sample generated code so repository samples compile against the new runtime and code generator.
- Upgrade guidance:
	- Regenerate all generated RPC code after upgrading to these packages.
	- Do not mix `Lakona.Rpc.CodeGen 0.14.0` with runtime `0.11.0`; use `Lakona.Rpc.CodeGen 0.15.0` together with runtime `0.11.0`.
	- If you previously consumed `IRpcSerializer.Serialize(...)` directly, migrate to `SerializeFrame(...)`.

## 0.2.14 / 0.13.6 / 0.8.2 / 0.6.4 / 0.6.2

- Refactored runtime internals by extracting shared keepalive state and request/task tracking helpers, reducing complexity in `RpcClientRuntime`, `RpcSession`, and `RpcServerHost`.
- Refactored `ContractParser` so source-loading/callback binding orchestration is separated from contract validation rules.
- Refactored code generation emitters so all-services binder generation, callback proxy generation, and facade callback generation are isolated into focused files.
- Refactored starter generation internals by centralizing Unity client template values and separating server command-line option parsing from the builder itself.
- Kept generated output and runtime behavior stable while improving local reasoning, reuse, and testability.

## 0.8.1 / 0.6.3 / 0.6.1

- Fixed RPC keepalive semantics so peer liveness is proven by inbound traffic and ping/pong responses rather than local outbound activity.
- Fixed client shutdown to fail pending RPC calls deterministically instead of leaving them hanging.
- Isolated client push-handler failures so callback exceptions no longer tear down the entire connection.
- Added a decompression size limit to transport security decoding to reduce compressed-frame denial-of-service risk.
- Added regression coverage for keepalive, shutdown, push isolation, and compressed-frame limits.

## 0.13.5

- Fixed generated client overload forwarding so parameterless client calls correctly delegate to the `CancellationToken` overload instead of recursing.
- Tightened contract validation to fail fast when RPC services or callback contracts are declared without valid RPC methods.
- Added behavior-level generator tests that compile and execute generated client code, not just compile it.
