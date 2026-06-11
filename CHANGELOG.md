# Changelog

Lakona was created on 2026-06-07 by merging the former ULinkGame, ULinkActor,
and ULinkRpc repositories into a single monorepo. This changelog starts from
that consolidation.

## 2026-06-11

### Fixed

- `Lakona.Tool` `0.9.3`: generated Unity clients now emit the NuGetForUnity restore settings and built-in Unity module dependencies required on first startup, preventing partial package restore plus missing `MemoryPack`, `System.Threading.Channels`, and UIElements compile errors.
- `Lakona.Tool` `0.9.2`: fixed generated Godot `.tscn` scenes to emit `theme_type_variation` as Godot `StringName` literals, so `Login.tscn` and `Chat.tscn` parse correctly on startup.
- `Lakona.Tool` `0.9.1`: restored the generated Unity, Godot, Server, and Hotfix chat starter slices after the single-pipeline refactor, including full client scripts, Unity UI assets, Godot static `.tscn` scenes with `LakonaTheme.tres`, server chat actor/lifecycle binding, and Godot MemoryPack client serializer references.
- `Lakona.Tool` `0.9.0`: restored Unity CN and Tuanjie scaffolding to use the Unity renderer, force embedded NuGetForUnity, preserve OpenUPM metadata for standard Unity, extract the embedded NuGetForUnity package, and generate compose files that use the published `Server.App.dll` entrypoint.
- `Lakona.Tool` `0.8.22`: generated Godot LoginScene and ChatScene buttons now have `StyleBoxFlat` backgrounds (accent green normal, dark panel disabled) matching the Unity cyber-green theme.
- `Lakona.Tool` `0.8.22`: generated Unity `.connect-button:disabled` and `.send-button:disabled` USS rules now use `--lakona-bg-input` instead of `--lakona-bg-panel` to prevent buttons from visually disappearing into the footer/panel when disabled at scene start.

## 2026-06-10

### Fixed

- `Lakona.Rpc.Transport.Kcp` `0.11.15`: `KcpConnectionAcceptor` and `KcpListener` now resolve DNS hostnames and support IPv6 listen addresses.
- `Lakona.Rpc.Transport.Tcp` `0.11.6`: `TcpConnectionAcceptor` now resolves DNS hostnames and supports IPv6 listen addresses.
- `Lakona.Rpc.Transport.WebSocket` `0.11.8`: `WsConnectionAcceptor` now resolves DNS hostnames and supports IPv6 listen addresses.
- `Lakona.Tool` `0.8.21`: updated scaffolded transport package references to `0.11.6`/`0.11.8`/`0.11.15` for DNS hostname resolution and IPv6 support.
- `Lakona.Tool` `0.8.20`: scaffolded server programs now pass `opts.Host` to transport acceptors. WebSocket, TCP, and KCP acceptors now default to `127.0.0.1` instead of `0.0.0.0`, eliminating Windows Firewall prompts during local development.
- `Lakona.Rpc.Transport.WebSocket` `0.11.7`: `WsConnectionAcceptor.CreateAsync` accepts optional `host` parameter (default `127.0.0.1`).
- `Lakona.Rpc.Transport.Tcp` `0.11.5`: `TcpConnectionAcceptor` constructor accepts optional `host` parameter (default `127.0.0.1`).
- `Lakona.Rpc.Transport.Kcp` `0.11.14`: `KcpConnectionAcceptor` and `KcpListener` constructors accept optional `host` parameter (default `127.0.0.1`).
- `Lakona.Tool` `0.8.19`: generated Godot login scenes no longer assign `reply.ConnectionId` to `ChatSession`, fixing `CS1061` in scaffolded Godot clients.
- `Lakona.Tool` `0.8.18`: generated chat clients now import the `Client.Login` namespace where `LoginClient` is defined.
- `Lakona.Tool` `0.8.18`: generated server programs now call `UseTransport(...)` for the selected single-endpoint transport, so TCP/KCP projects no longer fail `--lakona-game-check` by looking for a websocket endpoint.

### Released

- `Lakona.Tool` `0.8.15`

### Fix shared connection identity across Login and Chat services

Actor generates connection ID at login, returns it in `LoginReply.ConnectionId`,
client passes it back in `ChatSendRequest.ConnectionId`. This fixes a bug where
`LoginService` and `ChatService` each generated independent connection IDs,
causing `SendAsync` to fail to find the member in `ChatRoomActor`.

- `Lakona.Tool` `0.8.14`

### Fix circular ProjectReference in generated server project

Removed `EnsureProjectReferenceWithoutOutput` that added a circular `ProjectReference`
from `Server.App.csproj` to `Server.Hotfix.csproj`. The build ordering is already
handled by `Server.slnx`. This fixes `MSB4006` on `_GenerateRestoreProjectPathWalk`
in .NET 10 SDK.

- `Lakona.Tool` `0.8.13`

### Split RPC services into ILoginService + IChatService

The generated `Shared` project now defines two RPC services instead of one:
- `ILoginService` (service ID 1) — session establishment, auto-joins chat room
- `IChatService` (service ID 2) — messaging only (no Join/Leave)

Login implicitly joins the chat room; disconnect implicitly leaves. Generated
`Server.Hotfix` now contains `Login/LoginService.cs` and `Chat/ChatService.cs`.

### Merge RPC starter into Game scaffolding

`lakona new` now uses a single-phase project generation. RPC starter logic (git
infrastructure, Shared project, client project) is merged into the Game
scaffolder. The stale `Server/Server/` directory is no longer created — all
server code goes directly into `Server/App/`.

### Rename ChatServiceImpl → ChatService

The `Impl` suffix was an undocumented legacy artifact. The documented convention
`{Domain}Service` is now followed consistently.

## 2026-06-09

### Released

- `Lakona.Tool` `0.8.4`

### RPC package versions auto-detected at build time

The `GenerateToolPackageVersions` MSBuild target now XmlPeeks all RPC package
versions (Core, Server, Client, Transport.Tcp/WebSocket/Kcp,
Serializer.Json/MemoryPack, Analyzers) from their `.csproj` files, eliminating
the manually-maintained `ReleaseVersions.json`. The `StarterReleaseVersions`
class delegates directly to the generated `ToolPackageVersions` constants.

## 2026-06-07

### Released

- `Lakona.Tool` `0.7.0`
- `Lakona.Game.Server` `0.4.0`

### Merged Lakona.Rpc.Starter into Lakona.Tool

`Lakona.Tool` is now the single .NET CLI tool for Lakona. One command generates
the full project.

### Merged Lakona.Actor into Lakona.Game.Server

The standalone `Lakona.Actor` package and `Lakona.Actor.SourceGenerator` are
removed. The actor mailbox runtime now lives inside `Lakona.Game.Server` as an
internal kernel under `Lakona.Game.Server.Internal.ActorKernel`.
`Lakona.Game.Server.Actors` remains the only public game-facing actor API.
