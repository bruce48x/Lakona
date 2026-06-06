# Lakona.Rpc.Starter

`Lakona.Rpc.Starter` creates a runnable Lakona.Rpc workspace with shared contracts, a .NET server, and a selected client skeleton.

Use it when you want to start a new project without manually wiring package references, transport setup, serializer setup, and source generation.

## Requirements

- .NET SDK 10.0 or later
- A client runtime/editor matching the selected client type:
  - Unity 2022 LTS for `unity`
  - Unity 2022 LTS with China-friendly defaults for `unity-cn`
  - Tuanjie for `tuanjie`
  - Godot 4.6 C# for `godot`
  - .NET 10 for `console`

## Install

```bash
dotnet tool install -g Lakona.Rpc.Starter
```

## Create A Project

Interactive mode:

```bash
lakona-starter new --name MyGame
```

Non-interactive mode:

```bash
lakona-starter new --name MyGame --output ./samples --client-engine unity --transport websocket --serializer json
```

Console client example:

```bash
lakona-starter new --name MyConsoleApp --client-engine console --transport tcp --serializer memorypack
```

## Command Reference

```bash
lakona-starter [--help|-h|--version]
lakona-starter new [--name MyGame] [--output ./out] [--client-engine unity|unity-cn|tuanjie|godot|console] [--transport tcp|websocket|kcp] [--serializer json|memorypack] [--nugetforunity-source embedded|openupm] [--no-next-steps]
```

`new` options:

- `--name`: Project root folder name. Default: `ULinkApp`.
- `--output`: Parent directory for the generated project. Default: current directory.
- `--client-engine`: Client type to scaffold: `unity`, `unity-cn`, `tuanjie`, `godot`, or `console`.
- `--transport`: Transport package: `tcp`, `websocket`, or `kcp`.
- `--serializer`: Serializer package: `json` or `memorypack`.
- `--nugetforunity-source`: Unity-compatible clients only. Choose `embedded` or `openupm`.
- `--no-next-steps`: Do not print post-create guidance.

If `--client-engine`, `--transport`, or `--serializer` is omitted, the tool asks for it in the terminal.

CLI prompts, validation errors, usage text, and next steps follow the system UI language. English is the default; Simplified Chinese is used for `zh`, `zh-CN`, `zh-Hans`, and other non-traditional Chinese cultures; Traditional Chinese is used for `zh-TW`, `zh-HK`, `zh-MO`, and `zh-Hant`.

## Client Types

| `--client-engine` | Generated client |
| --- | --- |
| `unity` | Unity 2022 LTS project using OpenUPM `NuGetForUnity` by default |
| `unity-cn` | Unity 2022 LTS project using embedded `NuGetForUnity` by default |
| `tuanjie` | Tuanjie-compatible Unity project using embedded `NuGetForUnity` by default |
| `godot` | Godot 4.6 C# project with a runnable test scene |
| `console` | .NET 10 console client with a generated-client ping call |

Default `NuGetForUnity` source:

- `unity`: `openupm`
- `unity-cn`: `embedded`
- `tuanjie`: `embedded`

You can override the Unity default with `--nugetforunity-source`.

## Generated Layout

```text
MyGame/
  .gitignore
  Shared/
    Shared.csproj
    Interfaces/
      IPingService.cs
      RpcContractIds.cs
      SharedDtos.cs
  Server/
    Server.slnx
    Server/
      Server.csproj
      Program.cs
      Services/
        PingService.cs
  Client/
```

`Shared/` contains DTOs and RPC contracts. Unity-compatible clients also use it as a local UPM package.

`Server/Server/` is a .NET 10 server app configured with the selected transport and serializer.

`Client/` depends on the selected client type. The generated client includes a minimal connection test that calls `IPingService.PingAsync`.

The tool also initializes a git repository at the project root.

## Run The Generated Project

Start the server:

```bash
cd MyGame
dotnet run --project Server/Server/Server.csproj
```

Then run or open the client:

- Unity / Unity CN / Tuanjie: open `Client/` in the matching editor and press Play in the generated test scene.
- Godot: open `Client/` in Godot 4.6, build the C# project, then run `Main.tscn`.
- Console: run `dotnet run --project Client/Client.csproj`.

## Changing Contracts

Edit service interfaces and DTOs under `Shared/Interfaces/`.

Generated RPC glue is compiler output. New starter projects do not create `Generated/` source folders, MSBuild codegen targets, Unity codegen editor scripts, or a local generator tool manifest.

After changing shared contracts, use the normal build/editor flow. `Lakona.Rpc.Analyzers` runs automatically:

- Server, Godot, and console builds run source generation during compilation.
- Unity, Unity CN, and Tuanjie run source generation through the analyzer package restored into the Unity project.

When `memorypack` is selected, shared DTOs are generated with MemoryPack attributes and the required MemoryPack package references.

## More Documentation

- Getting started: https://bruce48x.github.io/Lakona.Rpc/posts/lakona-rpc-getting-started/
- Godot guide: https://bruce48x.github.io/Lakona.Rpc/guides/godot-guide/
- Generated RpcClient reference: https://bruce48x.github.io/Lakona.Rpc/reference/generated-client/
- Design boundary: https://bruce48x.github.io/Lakona.Rpc/concepts/design-boundary/
- Source generation notes: [`design/starter/source-generation.md`](../../design/starter/source-generation.md)
