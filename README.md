# Lakona

[![Tests and Publish NuGet](https://github.com/bruce48x/Lakona/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/bruce48x/Lakona/actions/workflows/publish-nuget.yml)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/Lakona.Game.Server.svg?label=NuGet)](https://www.nuget.org/packages/Lakona.Game.Server)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com)
[![Unity](https://img.shields.io/badge/Unity-2022-000000.svg?logo=unity)](https://unity.com)
[![Godot](https://img.shields.io/badge/Godot-4.x-478CBF.svg?logo=godot-engine)](https://godotengine.org)

Build realtime game servers in C#, share contracts with Unity or Godot, and
reload replaceable game logic while runtime-owned state stays in place.

Lakona gives game teams a complete starting workspace: shared C# contracts,
stateful server logic, hotfixable behavior, sessions, transports, diagnostics,
and a path from one local process to a multi-node deployment. Your gameplay,
database, and data model remain yours.

[Download Lakona Hub](https://github.com/bruce48x/Lakona/releases) ·
[Get started with Lakona Hub](https://bruce48x.github.io/Lakona/posts/getting-started/) ·
[See the samples](#see-it-in-action) ·
[Browse the docs](#learn-more)

## Start Here 🖥️

For the easiest desktop workflow, [download Lakona Hub](https://github.com/bruce48x/Lakona/releases).
Hub guides project creation, detects compatible .NET SDKs and client editors,
imports existing Lakona projects, packages server and Hotfix releases, and
opens your development tools. Every generated project remains ordinary files
that you can build and use without Hub. See the [Hub documentation](docs/tool/lakona-hub.md).

For terminal workflows and CI, use `Lakona.Tool`; the CLI path is below.

## Why Lakona ✨

- **🧩 Generate a complete project.** One command creates the shared contracts,
  server host, hotfix project, and Unity or Godot client in one workspace.
- **🔗 Define the contract once.** Shared C# interfaces, DTOs, callbacks, and
  protocol types are compiled for both client and server, reducing protocol
  drift during development. See [RPC architecture](docs/rpc/architecture.md).
- **🔥 Keep gameplay state alive while behavior changes.** Actors own mutable
  state, while replaceable C# behavior can be rebuilt and reloaded without
  moving that state into the hotfix assembly. See [Hotfix architecture](docs/hotfix/architecture.md)
  and the [Actor Model](docs/actor.md).
- **🗄️ Your game, your database.** Integrate PostgreSQL, MySQL, MongoDB, or
  another database through your own .NET clients and data access layer. Keep
  the schema, queries, and tools that fit your game and your team's expertise;
  Lakona does not impose a database or ORM.
- **🚀 Start small and grow deliberately.** Run a complete game server locally,
  choose the transports and serializers your game needs, then add sessions,
  reliable push, routing, and cluster deployment as the product requires them.
  See [Cluster](docs/cluster.md) and [Packaging and Deployment](docs/deployment.md).

Lakona is infrastructure, not a full game business framework. Your game owns
accounts, matchmaking policy, room rules, gameplay simulation, persistence
schema, rewards, and UI architecture.

## Quick Start ⚡

Prerequisites:

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Unity 2022 LTS](https://unity.com/releases/editor/archive) or
  [Godot 4.x .NET](https://godotengine.org/download/archive/)

Install the project tool and create a Unity starter project:

```bash
dotnet tool install -g Lakona.Tool
lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack
```

Build the generated server and hotfix project:

```bash
cd MyGame
dotnet build "Server/Server.slnx"
```

Start the server:

```bash
dotnet run --project "Server/App/Server.App.csproj" --no-build
```

In another terminal, check that the generated runtime is ready:

```bash
curl http://127.0.0.1:20080/_lakona/health/ready
```

Then open the generated `Client/` project in Unity or Godot. For the complete
first-run walkthrough, including the Godot command and client setup, read
[Create and Run a Lakona Project](https://bruce48x.github.io/Lakona/posts/getting-started/).

## See It In Action 🎮

> **Agar in action:** [From a local Unity game to a nine-node cluster](https://bruce48x.github.io/Lakona/posts/agar/) — including deployment and OpenTelemetry observability.

- [Game.Unity.Agar](samples/Game.Unity.Agar) — a small multiplayer game with
  shared gameplay code, sessions, reliable push, dual transports, and cluster
  deployment examples.
- [Game.Unity.MMO](samples/Game.Unity.MMO) — a server-authoritative Unity
  sample using shared contracts and realtime state synchronization.
- [Game.Godot.Chat](samples/Game.Godot.Chat) — a compact Godot .NET sample for
  the shortest client/server path.

Focused RPC examples for JSON, WebSocket, TCP, KCP, MemoryPack, and mixed
transports are available under [`samples/Rpc.*`](samples).

## What You Can Add Later 🚀

Lakona keeps the first project small while leaving room for production needs:

- **Sessions and reliable push** for login, reconnect, matchmaking, and
  server-initiated notifications. See [Session Lifecycle](docs/session.md).
- **Actors and timers** for rooms, players, matches, lobbies, and other
  stateful workflows. See [Actor Model](docs/actor.md).
- **Readiness and diagnostics** for startup validation, health probes, logs,
  and opt-in local runtime inspection. See [Guardrails](docs/guardrails.md)
  and [Use Lakona Observability](https://bruce48x.github.io/Lakona/posts/observability/).
- **Multi-node routing and deployment** when a single process is no longer
  enough. See [Cluster](docs/cluster.md) and [Packaging and Deployment](docs/deployment.md).
- **Pluggable transports and serializers** so gameplay contracts do not have
  to change when networking requirements change.

## AI-Assisted Development 🤖

Lakona ships a public, project-local [Agent Skill Pack](skills) for coding
agents. Generated projects include the compatible skills so agents can inspect
the installed framework, follow project conventions, and work from current
contracts without a second installation step. See [Lakona Project Agent Skills](docs/tool/agent-skills.md).

## Packages 📦

Most users should start with [Lakona Hub](https://github.com/bruce48x/Lakona/releases).
Use `Lakona.Tool` for terminal workflows and CI. Advanced integrations can use
`Lakona.Game.Server`, `Lakona.Game.Client`, `Lakona.Game.Abstractions`, or the
`Lakona.Rpc.*` packages. Package-specific usage is documented in the READMEs
under [`src/`](src).

Supported targets include .NET 10 server projects, .NET Standard 2.1 shared and
client packages, Unity 2022 LTS, Godot 4.x .NET, and Windows, Linux, and macOS.

## Learn More 📚

- [Create and Run a Lakona Project](https://bruce48x.github.io/Lakona/posts/getting-started/)
- [Design Philosophy](docs/design-philosophy.md)
- [Lakona Hub](docs/tool/lakona-hub.md)
- [RPC architecture](docs/rpc/architecture.md)
- [Actor Model](docs/actor.md)
- [Hotfix architecture](docs/hotfix/architecture.md)
- [Session Lifecycle](docs/session.md)
- [Cluster](docs/cluster.md)
- [Logging](docs/logging.md)
- [Packaging and Deployment](docs/deployment.md)
- [Runtime Guardrails](docs/guardrails.md)
- [Changelog](CHANGELOG.md)

## Contributing 🤝

Contributor rules, package boundaries, testing expectations, and release policy
live in [CONTRIBUTING.md](CONTRIBUTING.md).
