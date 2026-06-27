# Lakona Tool Server Pack Command

Status: implemented maintainer reference
Date: 2026-06-23
Audience: AI implementation agent, Lakona.Tool maintainers

## Purpose

Document the production-oriented `lakona-tool server pack` command that creates a
single deployable server zip for generated Lakona.Game projects.

The command exists because a Lakona server package is not only a normal
`dotnet publish` output. A valid first deployment must also include an initial
hotfix version installed in the production hotfix root structure. Manual
publish/build/copy/zip steps are too easy to get wrong and should be owned by
`Lakona.Tool`.

## Product Decision

`server pack` is the standard first-deployment package command.

`hotfix pack` remains the standard follow-up patch package command.

The two commands have distinct responsibilities:

- `lakona-tool server pack`: packages a complete, bootable server application
  zip with stable `Server.App` publish output and an installed initial hotfix
  version.
- `lakona-tool hotfix pack`: packages a standalone hotfix zip for later
  install/activate/rollback operations.

V1 deliberately excludes Docker packaging and trimming.

## Command Contract

Default command:

```powershell
lakona-tool server pack --runtime linux-x64
```

Options:

| Option | Required | Default | Meaning |
| --- | --- | --- | --- |
| `--runtime <rid>` | Yes | none | .NET Runtime Identifier such as `linux-x64`, `linux-arm64`, `win-x64`, or `win-arm64`. |
| `--configuration <name>` | No | `Release` | Build configuration passed to both stable publish and hotfix build. Must support at least `Release` and `Debug`; do not hard-code an enum if custom configurations can work naturally. |
| `--project <path>` | No | `Server/App/Server.App.csproj` | Stable executable server project to publish. |
| `--hotfix-project <path>` | No | `Server/Hotfix/Server.Hotfix.csproj` | Initial hotfix project to build/package/install into the server zip. |
| `--output <dir>` | No | `artifacts/server` | Directory where the final zip is written. |
| `--version <version>` | No | UTC timestamp such as `v20260623-153045Z` | Server package version and initial hotfix version unless a future option separates them. |

Self-contained publish is always enabled in v1:

```powershell
dotnet publish Server/App/Server.App.csproj `
  -c Release `
  -r linux-x64 `
  --self-contained true `
  -o <staging>/app
```

Do not expose `--self-contained` in v1. The production package is always
self-contained.

Publish trimming is not supported in v1. Do not expose `--trim`; the package
must be untrimmed. Lakona's stable host plus dynamically loaded hotfix assembly
model is not a safe default fit for publish trimming.

Do not pass `PublishTrimmed`, `PublishSingleFile`, or NativeAOT publish
properties in v1. A single deployable artifact means one zip containing the
normal published app tree, not a .NET single-file executable.

Docker packaging is not supported in v1. Use `server pack` for the server zip
and handle image creation with external deployment tooling if needed.

## Output Naming

Default output:

```txt
artifacts/server/Server.App-v20260623-153045Z-linux-x64.zip
```

The file name should include:

- the stable app assembly name, usually `Server.App`
- the package version
- the runtime identifier

If a file already exists at the final zip path, replace it only after the new
package has been created successfully in a staging path.

## Zip Layout

The zip root is the application root. After extraction, the operator should be
able to run the server without moving nested directories.

Expected layout:

```txt
Server.App.dll
Server.App.exe              # present on Windows self-contained publish
appsettings.json
*.dll
*.json
*.deps.json
*.runtimeconfig.json
lakona-server.json
hotfix/
  current.txt
  versions/
    v20260623-153045Z/
      Server.Hotfix.dll
      Server.Hotfix.pdb          # optional, include when produced
      Server.Hotfix.deps.json    # optional, include when produced
      hotfix.json
      checksums.sha256
      READY
```

The package must use the production hotfix root structure, not the development
flat hotfix directory with `reload.signal`.

## Server Manifest

Write `lakona-server.json` at the application root.

Minimum fields:

```json
{
  "version": "v20260623-153045Z",
  "builtAtUtc": "2026-06-23T15:30:45Z",
  "runtime": "linux-x64",
  "configuration": "Release",
  "selfContained": true,
  "trimmed": false,
  "entryAssembly": "Server.App.dll",
  "buildTag": "20260612.001",
  "initialHotfixVersion": "v20260623-153045Z",
  "toolVersion": "0.0.0-local"
}
```

`buildTag` must match the `LakonaHotfixBuildTag` used by `Server.App` and the
initial hotfix manifest.

`builtAtUtc` is canonical UTC text and must be written with a `Z` suffix, for
example `2026-06-23T15:30:45Z`.

The canonical timestamp precision is whole seconds. Implementations must
normalize `BuiltAtUtc` to UTC whole-second precision before writing
`lakona-server.json` and before comparing an in-memory manifest with the JSON
manifest read back from disk.

## Build And Packaging Flow

1. Resolve and validate paths:
   - stable project defaults to `Server/App/Server.App.csproj`
   - hotfix project defaults to `Server/Hotfix/Server.Hotfix.csproj`
   - output defaults to `artifacts/server`
   - staging directory lives under the output directory, for example
     `artifacts/server/.staging/<guid>/`

2. Publish the stable app:
   - run `dotnet publish`
   - pass `--self-contained true`
   - pass `-r <runtime>`
   - pass `-c <configuration>`
   - pass `-o <staging>/app`
   - do not pass trimming, single-file, or NativeAOT publish properties

3. Build and package the initial hotfix:
   - reuse the existing hotfix packaging logic where practical
   - pass the same `--configuration`
   - use the same version as the server package in v1
   - read the shared BuildTag from `BuildTag.props`
   - produce the same `hotfix.json` and `checksums.sha256` format used by
     `lakona-tool hotfix pack`

4. Install the initial hotfix into the staged app:
   - create `<staging>/app/hotfix/versions/<version>/`
   - copy `Server.Hotfix.dll`, optional PDB, optional deps file,
     `hotfix.json`, and `checksums.sha256`
   - write `READY` last
   - write `<staging>/app/hotfix/current.txt` containing the version
   - do not include `reload.signal`

5. Validate the staged package:
   - entry assembly exists
   - `lakona-server.json` exists and is valid JSON
   - `hotfix/current.txt` points to an existing version directory
   - version directory contains `READY`
   - hotfix manifest exists
   - hotfix checksums verify
   - server manifest BuildTag equals hotfix manifest BuildTag
   - publish output does not contain build `bin` or `obj` directories

6. Create the zip:
   - zip the contents of `<staging>/app`, not the staging directory itself
   - write to a temporary zip path first
   - atomically replace the final output path when possible
   - delete staging on success or failure

## Configuration Behavior

`--configuration` must be passed to both stable publish and hotfix build.

Examples:

```powershell
lakona-tool server pack --runtime linux-x64
lakona-tool server pack --runtime linux-x64 --configuration Debug
lakona-tool server pack --runtime win-x64 --configuration Release
```

`Release` is the default because this is a production packaging command.
`Debug` is still useful for staging, QA, symbol-rich troubleshooting, and local
package inspection.

Runtime configuration follows the default .NET host provider order. Operators
select a node-specific package configuration file with `DOTNET_ENVIRONMENT`:

```bash
DOTNET_ENVIRONMENT=battle-1 ./Server.App
```

or:

```bash
DOTNET_ENVIRONMENT=battle-1 dotnet Server.App.dll
```

The extracted package directory is the application content root. When
`DOTNET_ENVIRONMENT=battle-1`, the server reads `appsettings.json`, then
`appsettings.battle-1.json` from that directory, then environment variables,
then command-line arguments. Environment variables remain the right place for
secrets and host-specific overrides.

`server pack` does not require node-specific appsettings files. Deployment
automation may add or replace `appsettings.{Environment}.json` beside
`Server.App.dll` after extraction, or the project may include those files in
publish output when that is an explicit deployment choice.

## Error Handling

Fail with actionable CLI errors when:

- `--runtime` is missing.
- the stable project does not exist.
- the hotfix project does not exist.
- `dotnet publish` fails.
- hotfix build/package fails.
- BuildTag cannot be read from the shared BuildTag file.
- the hotfix package BuildTag differs from the server BuildTag.
- the staged app cannot be validated.
- the final zip cannot be written.

When custom `--project` and `--hotfix-project` paths resolve different
BuildTag files, `server pack` must fail on the mismatch. Do not rewrite or
override the hotfix manifest BuildTag to make the package appear compatible.

Do not leave partial final zips behind. Staging cleanup failures can be reported
as warnings after the primary failure.

## Implementation Notes

Implemented source layout:

```txt
src/Lakona.Tool/Cli/Commands/Server/ServerCommand.cs
src/Lakona.Tool/Cli/Commands/Server/ServerPackCommand.cs
src/Lakona.Tool/Hotfix/HotfixPackageVerifier.cs
src/Lakona.Tool/Server/DotNetCommandRunner.cs
src/Lakona.Tool/Server/HotfixPackageBuilder.cs
src/Lakona.Tool/Server/ServerJson.cs
src/Lakona.Tool/Server/ServerPackageWriter.cs
src/Lakona.Tool/Server/ServerPackageManifest.cs
src/Lakona.Tool/Server/ServerPackageValidator.cs
src/Lakona.Tool/Server/UtcDateTimeOffsetJsonConverter.cs
```

`CliApplication` should route:

```txt
lakona-tool server pack
```

Do not fold this into `HotfixPackCommand`. The stable server package has a
different artifact boundary and manifest.

Reuse `HotfixPackageWriter` or extract shared helpers where that keeps checksum
and manifest behavior consistent. Avoid duplicating checksum format logic.

## Tests

Keep focused tests under `tests/Lakona.Tool.Tests`.

Recommended coverage:

- CLI routes `server pack` and rejects unknown `server` subcommands.
- `server pack` requires `--runtime`.
- option parsing supports `--configuration Release` and
  `--configuration Debug`.
- default paths are `Server/App/Server.App.csproj`,
  `Server/Hotfix/Server.Hotfix.csproj`, and `artifacts/server`.
- manifest serializes `runtime`, `configuration`, `selfContained: true`, and
  `trimmed: false`.
- staged hotfix layout uses `hotfix/current.txt` plus
  `hotfix/versions/<version>/READY`.
- package validation rejects missing `READY`.
- package validation rejects mismatched BuildTag.
- zip root contains `Server.App.dll` directly rather than an extra top-level
  staging folder.
- generated docs or tool README mention `lakona-tool server pack --runtime`.
- manifest tests cover sub-second `BuiltAtUtc` input and verify that server
  package validation uses the same whole-second UTC value that is written to
  `lakona-server.json`.
- writer tests assert that `dotnet publish` receives no `PublishTrimmed`,
  `PublishSingleFile`, or NativeAOT arguments.

For integration tests that need external `dotnet publish`, keep them narrow and
skip or isolate them if the local SDK/runtime cannot support the requested RID.
Most behavior should be tested through writer methods using staged fake publish
and hotfix outputs.

## Documentation Updates

Update:

- `src/Lakona.Tool/README.md`
- `docs/tool/generation-architecture.md`
- generated project README text in
  `src/Lakona.Tool/Rendering/Docs/GeneratedProjectGuideRenderer.cs`

The generated project guide should distinguish:

```powershell
lakona-tool server pack --runtime linux-x64
lakona-tool hotfix pack
```

The first command creates the initial deployable server zip. The second creates
future hotfix zips.

## Non-Goals For V1

- Docker image build or compose deployment.
- Publish trimming.
- Single-file publish.
- NativeAOT.
- Multiple server entry projects in one zip.
- Separate server version and initial hotfix version.
- Remote deployment, upload, node rollout, or public management endpoints.
- Changing runtime hotfix activation semantics.
