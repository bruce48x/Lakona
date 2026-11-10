## What the Script Does

1. **Pack** (LocalFeed only): Clears the isolated feed and package cache, builds all packable `src/Lakona.*.csproj` projects and their internal package inputs in one Release graph, then packs that completed graph without rebuilding into a local NuGet feed
2. **Build Lakona.Tool**: Ensures the scaffolding tool is built
3. **Scaffold**: Runs `dotnet run --project src/Lakona.Tool -- new` for each engine, transport, serializer, and Membership provider combination
4. **Resolve dependencies**:
   - ProjectReference: Patches scaffolded csproj to use `<ProjectReference>` to local source
   - LocalFeed: Writes `NuGet.config` pointing to the local feed
   - NuGetOrg: Uses default nuget.org source (no config changes)
5. **Build Server**: Builds the generated server solution
6. **Generate E2E client**: Creates a temporary `.csproj` and `Program.cs` that uses `LakonaGameClient` with source-generated RPC stubs
7. **Start server**, wait for readiness
8. **Run E2E client**: Calls `LoginAsync`, verifies success/player identity in the
   returned world, then verifies a server-pushed world with a later tick
9. **Report**: Writes Markdown report and JSON summary to `$WorkDir`

The E2E client uses `LakonaGameClient` with an `IGameCallback` and source-generated `client.Api.Shared.Game.LoginAsync()` — this tests the full generated game client stack that end users experience.

Generic scaffold/build/RPC matrix runs use this unified entry point. It owns
dependency selection, the verification-client contract, readiness checks, report
generation and cleanup of the server process it started. The current game
contract lives in `Shared.Contracts.Game`.

The pre-push hook enables `-FindAvailablePort`, so unrelated local servers do
not block validation merely because they already use the preferred range.

### E2E Client Architecture

| Aspect | ProjectReference mode | LocalFeed / NuGetOrg mode |
|--------|----------------------|---------------------------|
| Dependency style | `<ProjectReference>` to local source | `<PackageReference>` with exact version from the local feed or generated client manifest |
| RPC analyzer | Direct ProjectReference with `OutputItemType="Analyzer"` because MSBuild project analyzers are not transitive | Carried transitively inside the `Lakona.Rpc.Core` package |
| Hotfix assets | Direct reference to the internal Generators analyzer because package analyzer bundling does not apply to ProjectReference; authoring abstractions belong to Game.Server | The `Lakona.Game.Server` package contains the authoring API and bundled compiler analyzer |
| NuGet.config | None needed | Written to E2E client dir |
| Program.cs | LakonaGameClient (same for all modes) | LakonaGameClient (same for all modes) |

`Lakona.Game.Server.Hotfix.Abstractions` is a namespace in Game.Server, not a
separate project, assembly, or package.

For NuGetOrg, the verification client takes Game.Client, transport and serializer
versions from `Client/Client.csproj` for Godot or `Client/Assets/packages.config`
for Unity/Tuanjie. Server.App is not a source for client dependency versions.
SDK `Version` attributes and child elements are supported, with or without XML
namespaces. Missing packages, missing/empty versions and conflicting declarations
fail with the package ID and manifest path. Identical repeated versions are
accepted; MSBuild expressions, floating versions and ranges are not resolved.
There is no fallback to repository versions, server dependencies or latest packages.
