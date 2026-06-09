# Changelog

Lakona was created on 2026-06-07 by merging the former ULinkGame, ULinkActor,
and ULinkRpc repositories into a single monorepo. This changelog starts from
that consolidation.

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
