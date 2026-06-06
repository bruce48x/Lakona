# Lakona.Tool

`Lakona.Tool` creates Lakona.Game projects.

## Install

Install the tool:

```bash
dotnet tool install --global Lakona.Tool
```

The first time you run `lakona new`, the tool automatically installs the matching `Lakona.Rpc.Starter` version if `lakona-starter` is not already available, then continues project generation.

## Create A Project

Start with the minimal command:

```bash
lakona new --name MyGame
```

After generation, run the printed check command before starting the server:

```bash
cd MyGame
dotnet run --project "Server/Server/Server.csproj" -- --lakona-game-check
```

Common options:

```bash
lakona new --name MyGame --client-engine unity --transport websocket --serializer json
```

Supported values:

- `--client-engine`: `unity`, `unity-cn`, `tuanjie`, `godot`
- `--transport`: `websocket`, `tcp`, `kcp`
- `--serializer`: `json`, `memorypack`
- `--persistence`: `none`, `postgres`, `mysql`
- `--nugetforunity-source`: `embedded`, `openupm`

## Defaults

By default, the generated project includes:

- a server project
- a Unity, Tuanjie, or Godot client project
- a shared contract project
- Lakona.Game server and client dependencies
- Cluster infrastructure
- Hotfix infrastructure
- Reliable Push infrastructure
- `lakona-game.tool.json`

Generated server projects reference `Lakona.Game.Server.Generators` as an analyzer so server-side `Actor<TKey>` classes get typed `Local(id)` and `Remote(nodeId, id)` accessors at build time.

For Unity and Tuanjie clients, the tool pins `Lakona.Game.Client` and `Lakona.Game.Abstractions` in `Assets/packages.config` and generates an editor import guard that prevents NuGet analyzer DLLs from being loaded as Unity runtime plugins.

The generated `appsettings.json` intentionally stays small. It contains only the local node identity and client endpoint binding under `Lakona.Game`; cluster services, hotfix defaults, reliable push defaults, and RPC check output are derived by generated server helper code.

For a local Docker Compose rehearsal:

```bash
lakona new --name MyGame --deploy-profile compose
```

To include database dependencies:

```bash
lakona new --name MyGame --persistence postgres
lakona new --name MyGame --persistence mysql
```

## Generated Configuration

The default development appsettings file has this shape:

```json
{
  "Lakona.Game": {
    "Node": {
      "Id": "dev-1"
    },
    "Endpoints": [
      {
        "Transport": "kcp",
        "Host": "127.0.0.1",
        "Port": 20000
      }
    ]
  }
}
```

For WebSocket projects, the endpoint entry also includes `"Path": "/ws"`.

Validate the derived project state with:

```bash
dotnet run --project "Server/Server/Server.csproj" -- --lakona-game-check
```

The check prints the generated Cluster, Hotfix, Reliable Push, and RPC state so the default `appsettings.json` does not need to expose every derived setting.

Use JSON output when CI or deployment scripts need machine-readable validation results:

```bash
dotnet run --project "Server/Server/Server.csproj" -- --lakona-game-check --json
```

## Cluster Configuration

The generated server derives a node-local service model. A node is one .NET server process; generated defaults include gateway, node-directory, and route-directory services inside that node.

The default `appsettings.json` does not expose that full derived topology. Use `--lakona-game-check` to inspect it. When a generated project is intentionally split across processes, use the canonical `Lakona.Game:Feature`, `Lakona.Game:Endpoints[]`, and minimal `Lakona.Game:Cluster` shape described in `../../docs/lakona-game-configuration-startup.md`; do not add `Cluster.Directory`, `Services`, or deployment-shaped sections to appsettings until the framework owns and validates those settings.
