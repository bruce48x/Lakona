# Changelog

Lakona was created on 2026-06-07 by merging the former ULinkGame, ULinkActor,
and ULinkRpc repositories into a single monorepo. This changelog starts from
that consolidation.

## 2026-06-10

### Fixed

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
