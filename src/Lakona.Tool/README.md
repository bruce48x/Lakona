# Lakona.Tool

`Lakona.Tool` is the command-line project tool for Lakona. It generates
a complete Lakona.Game workspace from one project specification: shared
contracts, server app, hotfix project, selected client, operations files, and
generated project docs.

## Install

```bash
dotnet tool install -g Lakona.Tool
```

## Create A Project

```bash
lakona-tool new
```

For scripts and CI, provide the required options explicitly (--output, --persistence, --nugetforunity-source, and --deploy-profile default to `.`, `none`, `openupm`, and `none` respectively):

```bash
lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack
```

For a lightweight headless client for smoke and load checks:

```bash
lakona-tool new --name MyGame --client-engine console --transport kcp --serializer memorypack
```

After generation, build the server and hotfix project, start the server, then
query the printed readiness endpoint from another terminal:

```bash
cd MyGame
dotnet build "Server/Server.slnx"
dotnet build "Server/Hotfix/Server.Hotfix.csproj"
dotnet run --project "Server/App/Server.App.csproj" --no-build
curl http://127.0.0.1:20080/_lakona/health/ready
```

Supported values:

- `--client-engine`: `unity`, `tuanjie`, `godot`, `console`
- `--transport`: `websocket`, `tcp`, `kcp`
- `--serializer`: `json`, `memorypack`
- `--persistence`: `none`, `postgres`, `mysql`
- `--nugetforunity-source`: `embedded`, `openupm`
- `--deploy-profile`: `none`, `compose`

## Defaults

By default, the generated project includes:

- a server project
- a Unity, Tuanjie, Godot, or Console client project
- a shared contract project
- Lakona.Game server and client dependencies
- Cluster infrastructure
- Hotfix infrastructure
- Reliable Push infrastructure

Generated server projects reference `Lakona.Game.Server.Hotfix.Generators` as an analyzer so public `[HotfixBehaviorOf]` extension methods define actor APIs, and Hotfix-owned generated behavior-derived selectors/refs are available at build time.

For Unity and Tuanjie clients, the tool installs Unity's new Input System with a file-backed movement action and scene-owned UI input module. It also pins `Lakona.Game.Client` and `Lakona.Game.Abstractions` in `Assets/packages.config` and generates an editor import guard that prevents NuGet analyzer/generator DLLs and incompatible multi-TFM plugins (for example `lib/net10.0/`) from being loaded as Unity runtime plugins, while explicitly enabling `netstandard2.1` runtime DLLs under `Assets/Packages`.

The generated `appsettings.json` intentionally stays small. It contains only the local node identity, session cleanup retention, health endpoint binding, and endpoint-local serializer/RPC service exposure under `Lakona`; cluster discovery, hotfix defaults, reliable push defaults, and readiness diagnostics are derived by generated server helper code.

Generated server apps use build-time hotfix service discovery. RPC contracts marked with `[RpcService]` in referenced user contract assemblies produce stable server proxies automatically; new projects no longer need hand-written service marker files.

For a local Docker Compose rehearsal:

```bash
lakona-tool new --name MyGame --deploy-profile compose
```

To include database dependencies:

```bash
lakona-tool new --name MyGame --persistence postgres
lakona-tool new --name MyGame --persistence mysql
```

## Generated Configuration

The default development appsettings file has this shape:

```json
{
  "Lakona": {
    "Node": {
      "Id": "dev-1"
    },
    "Sessions": {
      "ResumeWindowSeconds": 60
    },
    "Hotfix": {
      "DebugWatcher": "On"
    },
    "Management": {
      "Http": {
        "Host": "127.0.0.1",
        "Port": 20080
      }
    },
    "Health": {
      "Enabled": true,
      "RequireLoopback": true
    },
    "Observability": {
      "LocalAdmin": {
        "Enabled": true,
        "RequireLoopback": true
      }
    },
    "Endpoints": [
      {
        "Transport": "kcp",
        "Serializer": "memorypack",
        "Host": "127.0.0.1",
        "Port": 20000,
        "ReliablePush": true,
        "RpcServices": [ "game" ]
      }
    ]
  }
}
```

For WebSocket projects, the endpoint entry also includes `"Path": "/ws"`.

After the server starts, validate the derived project state with:

```bash
curl http://127.0.0.1:20080/_lakona/health/ready
```

The generated `DebugWatcher` setting makes local hotfix rebuilds reload through
`reload.signal`. The readiness endpoint returns JSON guardrail diagnostics so
the default `appsettings.json` does not need to expose every derived setting.

## Server Package

Create the initial deployable server zip:

```bash
lakona-tool server pack --runtime linux-x64
```

The server package is self-contained, RID-specific, untrimmed, and includes an
installed initial hotfix version under `hotfix/versions/<version>/`.

Use `--configuration Debug` for symbol-rich staging packages:

```bash
lakona-tool server pack --runtime linux-x64 --configuration Debug
```

## Hotfix Operations

Package future hotfix zips after the initial server package has shipped:

```bash
lakona-tool hotfix pack
```

Install a package into the node-local hotfix root:

```bash
lakona-tool hotfix install artifacts/hotfix/Server.Hotfix-v20260612-153045Z.zip --root hotfix
```

Activate, inspect, or roll back the loopback-only admin endpoint:

```bash
lakona-tool hotfix activate v20260612-153045Z --server http://127.0.0.1:20080
lakona-tool hotfix status --server http://127.0.0.1:20080
lakona-tool hotfix rollback --server http://127.0.0.1:20080
```

## Distributed Configuration

The generated server derives a node-local runtime model. A node is one .NET server process; generated defaults include gateway, node-directory, and route-directory infrastructure inside that node.

The default `appsettings.json` does not expose that full derived topology. Use the readiness endpoint to inspect whether the resolved runtime is valid. When a generated project is intentionally split across processes, use `Lakona:ActorHosts`, `Lakona:Endpoints[]`, endpoint `RpcServices`, and the minimal `Lakona:Cluster` shape described in `../../docs/cluster.md`; Startup service groups are declared in `HotfixStartup.ConfigureActors`. Include `Lakona:Cluster:Directory` only when a split project owns shared cluster directory infrastructure. Do not add `Services`, endpoint `Name`, or deployment-shaped sections to appsettings until the framework owns and validates those settings.
