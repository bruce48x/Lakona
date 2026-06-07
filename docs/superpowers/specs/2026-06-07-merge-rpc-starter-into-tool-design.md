# Merge Lakona.Rpc.Starter Into Lakona.Tool Design

## Purpose

Lakona is now a single repository and product line. Keeping both
`Lakona.Rpc.Starter` and `Lakona.Tool` as separate .NET tools preserves the
old split and creates an avoidable runtime dependency: `Lakona.Tool new` shells
out to `lakona-starter`, installs or updates `Lakona.Rpc.Starter`, then
augments the generated project. The next implementation should remove that
split.

The final state is one tool package and one command:

- NuGet package: `Lakona.Tool`
- Tool command: `lakona-tool`
- Removed package/tool/project: `Lakona.Rpc.Starter`
- Removed command: `lakona-starter`

`Lakona.Tool` owns both the base RPC workspace generation and the game
framework augmentation in one in-process flow.

## Current State

The current tree has two tool projects:

- `src/Lakona.Rpc.Starter/Lakona.Rpc.Starter.csproj`
  - `PackageId`: `Lakona.Rpc.Starter`
  - `ToolCommandName`: `lakona-starter`
  - Generates the base RPC shared/server/client workspace.
  - Owns `ReleaseVersions.json`, embedded Unity/Godot templates, dependency
    planning, CLI prompts, localized text, output staging, and version
    resolution.
- `src/Lakona.Tool/Lakona.Tool.csproj`
  - `PackageId`: `Lakona.Tool`
  - Current `ToolCommandName`: `lakona`
  - Runs `lakona-starter new` through `ToolProcessRunner`.
  - Installs or updates `Lakona.Rpc.Starter` automatically when needed.
  - Augments the generated base RPC workspace with game server, client,
    hotfix, cluster, operations, and config scaffolding.

The two projects are tied together by:

- `src/Lakona.Tool/Infrastructure/ToolProcessRunner.cs`
- `src/Lakona.Tool/Scaffolding/ToolModels.cs`
  - `ToolPackageVersions.ULinkRpcStarter`
- `src/Lakona.Tool/Cli/ToolText.cs`
  - messages about installing/updating `lakona-starter`
- `src/Lakona.Tool/README.md`
  - states that `lakona new` installs `Lakona.Rpc.Starter`
- `.github/workflows/godot-daily.yml`
  - has separate starter and tool jobs
- `scripts/rpc/ci/verify-starter-godot.sh`
  - runs `src/Lakona.Rpc.Starter`
- `scripts/game/ci/verify-lakona-tool-godot.sh`
  - packs and installs `Lakona.Rpc.Starter`
- `Lakona.slnx`
  - includes `src/Lakona.Rpc.Starter`
- `tests/Lakona.Rpc.Starter.Tests`
  - tests the starter project directly

## Decision

Move the starter implementation into `src/Lakona.Tool` as an internal
`RpcStarter` module. Delete the standalone `src/Lakona.Rpc.Starter` project and
fold its tests into `tests/Lakona.Tool.Tests`.

The new `lakona-tool new` flow is:

1. Parse `lakona-tool new` options through `CliParser`.
2. Convert `NewCommandOptions` to a `RpcStarterNewOptions` value.
3. Generate the base RPC workspace directly in process.
4. Augment that workspace with Lakona.Game files.
5. Write `lakona.tool.json`.
6. Print the existing three next steps.

The new implementation must not:

- start `lakona-starter` as an external process
- install `Lakona.Rpc.Starter`
- reference `src/Lakona.Rpc.Starter`
- pack `Lakona.Rpc.Starter`
- publish `Lakona.Rpc.Starter`
- document `dotnet tool install -g Lakona.Rpc.Starter` as current usage

Historical docs may still mention the old package when explicitly describing
former repository history.

## Architecture

### Package And Command Identity

`src/Lakona.Tool/Lakona.Tool.csproj` becomes the only CLI project.

Required project metadata:

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>lakona-tool</ToolCommandName>
<PackageId>Lakona.Tool</PackageId>
```

The implementation should not create a `lakona` compatibility command. The user
explicitly chose `lakona-tool`.

The help text must show:

```txt
lakona-tool help
lakona-tool new [--name MyGame] ...
```

Usage errors should say `Run lakona-tool help for usage.`

### Internal Starter Module

Create an internal module under:

```txt
src/Lakona.Tool/RpcStarter/
  Cli/
  Generation/
  Generation/ClientTemplates/
  Infrastructure/
  Templates/
  TemplateAssets/
```

Move starter code from `src/Lakona.Rpc.Starter` into that module and rename the
namespace from `Lakona.Rpc.Starter` to `Lakona.Tool.RpcStarter`.

Do not keep a source namespace named `Lakona.Rpc.Starter` inside `Lakona.Tool`.
That would make the source tree look merged while preserving the old product
boundary in code.

The old `src/Lakona.Rpc.Starter/Cli/Program.cs` should not move. There is no
separate starter executable after this change.

The old `StarterCli` parser/prompt code should be treated as internal helper
logic only. `lakona-tool` already owns top-level command parsing. Keep only the
pieces still needed by the generator, such as enum parsing helpers if the
implementation chooses to reuse them. Prefer making the in-process API explicit
instead of pretending there is still a nested CLI.

### In-Process Generation API

Introduce a small in-process API in `src/Lakona.Tool/RpcStarter`, for example:

```csharp
namespace Lakona.Tool.RpcStarter;

internal sealed record RpcStarterNewOptions(
    string ProjectName,
    string OutputDirectory,
    ClientEngineKind ClientEngine,
    TransportKind Transport,
    SerializerKind Serializer,
    NuGetForUnitySourceKind NuGetForUnitySource);

internal sealed class RpcStarterGenerator
{
    public int Generate(RpcStarterNewOptions options);
}
```

The exact class names may differ, but the API must make these facts obvious:

- it is not a command-line wrapper
- it is called directly by `CliApplication`
- it receives normalized options, not raw strings
- it returns or throws in a way `CliApplication` can report as a normal tool
  failure

The generation path should keep the existing staging semantics from
`StarterOutputManager.GenerateIntoTargetDirectory`. The generated directory
must either appear complete or not replace an existing directory.

### Option Conversion

`CliParser` should continue to own user-facing `lakona-tool new` validation.

`CliApplication.NewAsync` should convert string options into starter enums:

| `NewCommandOptions` value | Starter enum |
| --- | --- |
| `ClientEngine` `unity` | `ClientEngineKind.Unity` |
| `ClientEngine` `unity-cn` | `ClientEngineKind.UnityCn` |
| `ClientEngine` `tuanjie` | `ClientEngineKind.Tuanjie` |
| `ClientEngine` `godot` | `ClientEngineKind.Godot` |
| `Transport` `tcp` | `TransportKind.Tcp` |
| `Transport` `websocket` | `TransportKind.WebSocket` |
| `Transport` `kcp` | `TransportKind.Kcp` |
| `Serializer` `json` | `SerializerKind.Json` |
| `Serializer` `memorypack` | `SerializerKind.MemoryPack` |
| `NuGetForUnitySource` `embedded` | `NuGetForUnitySourceKind.Embedded` |
| `NuGetForUnitySource` `openupm` | `NuGetForUnitySourceKind.OpenUpm` |

Because `CliParser` already validates these strings, conversion failures should
be impossible during normal execution. If a failure still occurs, throw
`InvalidOperationException` with a message naming the unsupported value and
option.

### Version Resolution

Delete `ToolPackageVersions.ULinkRpcStarter`.

Keep starter release versions as an internal part of `Lakona.Tool`. The
existing generated-project package version behavior must remain:

- generated shared projects use current `Lakona.Rpc.Core`
- generated servers use current `Lakona.Rpc.Server`
- generated clients use current `Lakona.Rpc.Client`
- selected transports and serializers use the same versions previously
  resolved by `ReleaseVersions.json`
- analyzers remain private analyzer references

The implementation may keep `ReleaseVersions.json` embedded in `Lakona.Tool`,
or it may replace it with generated constants in the `GenerateToolPackageVersions`
MSBuild target. The safer first implementation is to keep the JSON resource and
move it into `src/Lakona.Tool/RpcStarter/ReleaseVersions.json`; tests already
verify it matches source package versions.

If the JSON resource is kept, update its embedded resource logical name from:

```txt
Lakona.Rpc.Starter.ReleaseVersions.json
```

to:

```txt
Lakona.Tool.RpcStarter.ReleaseVersions.json
```

and update `StarterReleaseVersions` accordingly.

### Embedded Templates

Move starter embedded assets into `Lakona.Tool`:

```txt
src/Lakona.Tool/RpcStarter/TemplateAssets/NuGetForUnity.4.5.0.zip
src/Lakona.Tool/RpcStarter/Templates/Godot/project.godot.template
src/Lakona.Tool/RpcStarter/Templates/Godot/Main.tscn.template
src/Lakona.Tool/RpcStarter/Templates/Unity/EditorBuildSettings.asset.template
src/Lakona.Tool/RpcStarter/Templates/Unity/AutoOpenConnectionScene.template
```

Update `src/Lakona.Tool/Lakona.Tool.csproj` so these files are embedded
resources. The resource prefix used by `StarterTemplateRenderer` must be updated
to match the new logical names.

### Tests

Fold all useful `tests/Lakona.Rpc.Starter.Tests` coverage into
`tests/Lakona.Tool.Tests`.

Preferred structure:

```txt
tests/Lakona.Tool.Tests/
  RpcStarter/
    Golden/
    ProcessRunnerTests.cs
    StarterDependencyPlannerTests.cs
    StarterLocalizationTests.cs
    StarterTemplateGeneratorTests.cs
    UnitySamplePackageTests.cs
```

Update namespaces to `Lakona.Tool.Tests`.

Update `tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj`:

- keep the existing reference to `src/Lakona.Tool`
- add `Golden\**\*` copy rules from the moved starter tests
- use one test framework line. Prefer the current `xunit.v3` package style used
  by `Lakona.Tool.Tests`, not the older `xunit` v2 style from
  `Lakona.Rpc.Starter.Tests`.

After tests are moved, delete `tests/Lakona.Rpc.Starter.Tests`.

### Solutions

Update `Lakona.slnx`:

- remove `src/Lakona.Rpc.Starter/Lakona.Rpc.Starter.csproj`
- remove `tests/Lakona.Rpc.Starter.Tests/Lakona.Rpc.Starter.Tests.csproj`
- keep `src/Lakona.Tool/Lakona.Tool.csproj`
- keep `tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj`

Update `tests/Tests.slnx`:

- remove stale `Lakona.Game.Tool.Tests` if present
- include `Lakona.Tool.Tests/Lakona.Tool.Tests.csproj`
- include all existing test projects that should be part of normal test runs

### Workflows And Scripts

Update `.github/workflows/godot-daily.yml`:

- remove the separate `verify-rpc-godot-starter` job
- keep one `verify-lakona-godot-tool` job
- the remaining job should verify `lakona-tool new` for the same transport and
  serializer matrix

Update `scripts/game/ci/verify-lakona-tool-godot.sh`:

- stop packing `src/Lakona.Rpc.Starter`
- stop installing `Lakona.Rpc.Starter`
- run `src/Lakona.Tool/Lakona.Tool.csproj` directly
- generated command semantics should be equivalent to:

```bash
dotnet run --project "$ROOT_DIR/src/Lakona.Tool/Lakona.Tool.csproj" -- \
  new \
  --name "$PROJECT_NAME" \
  --output "$WORK_DIR" \
  --client-engine godot \
  --transport "$TRANSPORT" \
  --serializer "$SERIALIZER"
```

Update or delete `scripts/rpc/ci/verify-starter-godot.sh`:

- If no workflow or local docs use it after the merge, delete it.
- If kept as a lower-level RPC-only smoke script, it must invoke
  `src/Lakona.Tool` and its name should no longer contain `starter`.

Update `scripts/rpc/check-generated-code.ps1` and `scripts/rpc/sample.ps1` only
if they reference `Lakona.Rpc.Starter`, `lakona-starter`, or old paths.

### Documentation

Update current docs and package READMEs so users install and run one tool:

```bash
dotnet tool install -g Lakona.Tool
lakona-tool new --name MyGame --client-engine unity --transport websocket --serializer json
```

Required current-doc updates:

- `README.md`
- `CONTRIBUTING.md`
- `src/Lakona.Tool/README.md`
- `docs/rpc/overview.md`
- `docs/rpc/starter/*.md`
- `docs/lakona-monorepo.md`
- `.github/workflows/*.yml` names and step labels if they mention starter

Remove `src/Lakona.Rpc.Starter/README.md` with the project. If useful content
is not already present in `src/Lakona.Tool/README.md` or `docs/rpc/overview.md`,
move it before deletion.

Historical files may retain old names when they are explicitly archival:

- `CHANGELOG.md`
- `docs/maintenance/imported-contributing-notes.md`
- `docs/rpc/archive/**`

### NuGet Publishing

The generic publish workflow packs every `src/*/*.csproj`. Removing
`src/Lakona.Rpc.Starter` is enough to stop publishing `Lakona.Rpc.Starter`.

The implementation should verify the pack list locally by running:

```powershell
Get-ChildItem -Path src -Depth 1 -Filter *.csproj | Select-Object FullName
```

The output must include `src/Lakona.Tool/Lakona.Tool.csproj` and must not
include `src/Lakona.Rpc.Starter/Lakona.Rpc.Starter.csproj`.

### Compatibility Policy

Do not preserve `lakona-starter` as an alias. Do not add a `starter` subcommand
that behaves like the old tool. This is a deliberate cleanup at the first major
post-merge change.

It is acceptable to keep internal type names such as `StarterTemplateGenerator`
for one implementation pass if the namespace and product surface are clearly
`Lakona.Tool.RpcStarter`. A later cleanup can rename internal types if desired.

## Error Handling

`CliApplication.RunAsync` already catches `CliUsageException` and prints
localized usage help. Keep that behavior.

Starter generation failures should surface as normal tool failures:

- `InvalidOperationException` from template generation should print the message
  and return exit code `1`
- attempts to generate into an existing non-empty target should preserve the
  current staging behavior
- failed `git init` should keep the existing behavior from the starter generator
  unless tests show it is unsafe

Do not introduce package installation errors for starter, because starter is no
longer a separate package.

## Validation

The implementation is complete when all of these are true:

- `src/Lakona.Rpc.Starter` no longer exists
- `tests/Lakona.Rpc.Starter.Tests` no longer exists
- `Lakona.slnx` no longer references `Lakona.Rpc.Starter`
- `tests/Tests.slnx` references `tests/Lakona.Tool.Tests`
- `src/Lakona.Tool/Lakona.Tool.csproj` has
  `<ToolCommandName>lakona-tool</ToolCommandName>`
- `Lakona.Tool.Tests` contains the former starter generator tests
- `dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj` passes
- `dotnet build Lakona.slnx --no-restore` passes
- `pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1` passes
- a text scan shows no current references to `lakona-starter` or
  `Lakona.Rpc.Starter` outside explicitly historical docs
- the Godot daily script verifies the integrated `lakona-tool new` path

## Non-Goals

This change should not redesign the generated project layout.

This change should not rename generated RPC contracts, transports, serializers,
or sample project names.

This change should not remove the phrase "starter" from every internal helper
name. The product boundary must disappear; internal implementation vocabulary
can be cleaned incrementally.

This change should not introduce a new package besides `Lakona.Tool`.

## Notes For The Implementing Agent

Work in small commits. Do not delete `src/Lakona.Rpc.Starter` at the beginning.
First move code and make tests pass from `Lakona.Tool`; then remove the old
project once nothing references it.

Use `rg` constantly. A good final scan is:

```powershell
rg -n "Lakona\\.Rpc\\.Starter|lakona-starter|ULinkRpcStarter|src/Lakona\\.Rpc\\.Starter|tests/Lakona\\.Rpc\\.Starter" . -g '!**/bin/**' -g '!**/obj/**'
```

Every match must be either removed, updated, or explicitly justified as
historical documentation.
