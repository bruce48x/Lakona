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

After generation, run the printed check command before starting the server:

```bash
cd MyGame
dotnet run --project "Server/App/Server.App.csproj" -- --lakona-game-check
```

Supported values:

- `--client-engine`: `unity`, `unity-cn`, `tuanjie`, `godot`, `console`
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

Generated server projects reference `Lakona.Game.Server.Generators` as an analyzer so server-side `Actor<TKey>` classes get typed `Local(id)` and `Remote(nodeId, id)` accessors at build time.

For Unity and Tuanjie clients, the tool pins `Lakona.Game.Client` and `Lakona.Game.Abstractions` in `Assets/packages.config` and generates an editor import guard that prevents NuGet analyzer DLLs from being loaded as Unity runtime plugins.

The generated `appsettings.json` intentionally stays small. It contains only the local node identity and endpoint-local RPC service exposure under `Lakona`; cluster discovery, hotfix defaults, reliable push defaults, and RPC check output are derived by generated server helper code.

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
    "Endpoints": [
      {
        "Transport": "kcp",
        "Host": "127.0.0.1",
        "Port": 20000,
        "RpcServices": [ "login", "chat" ]
      }
    ]
  }
}
```

For WebSocket projects, the endpoint entry also includes `"Path": "/ws"`.

Validate the derived project state with:

```bash
dotnet run --project "Server/App/Server.App.csproj" -- --lakona-game-check
```

The check prints the generated Cluster, Hotfix, Reliable Push, and RPC state so the default `appsettings.json` does not need to expose every derived setting.

Use JSON output when CI or deployment scripts need machine-readable validation results:

```bash
dotnet run --project "Server/App/Server.App.csproj" -- --lakona-game-check --json
```

## Hotfix Operations

Package the current hotfix project:

```bash
lakona-tool hotfix pack
```

Install a package into the node-local hotfix root:

```bash
lakona-tool hotfix install artifacts/hotfix/Server.Hotfix-v20260612-153045Z.zip --root hotfix
```

Activate, inspect, or roll back the loopback-only admin endpoint:

```bash
lakona-tool hotfix activate v20260612-153045Z --server http://127.0.0.1:20090
lakona-tool hotfix status --server http://127.0.0.1:20090
lakona-tool hotfix rollback --server http://127.0.0.1:20090
```

## Distributed Configuration

The generated server derives a node-local feature model. A node is one .NET server process; generated defaults include gateway, node-directory, and route-directory infrastructure inside that node.

The default `appsettings.json` does not expose that full derived topology. Use `--lakona-game-check` to inspect it. When a generated project is intentionally split across processes, use the canonical `Lakona:Feature`, `Lakona:Endpoints[]`, endpoint `RpcServices`, and minimal `Lakona:Cluster` shape described in `../../docs/cluster.md`; do not add `Cluster.Directory`, `Services`, endpoint `Name`, or deployment-shaped sections to appsettings until the framework owns and validates those settings.
