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
8. **Run E2E client**: Calls `LoginAsync` and verifies the response
9. **Report**: Writes Markdown report and JSON summary to `$WorkDir`

The E2E client uses `LakonaGameClient` with an `IGameCallback` and source-generated `client.Api.Shared.Game.LoginAsync()` — this tests the full generated game client stack that end users experience.

The pre-push hook enables `-FindAvailablePort`, so unrelated local servers do
not block validation merely because they already use the preferred range.

### E2E Client Architecture

| Aspect | ProjectReference mode | LocalFeed / NuGetOrg mode |
|--------|----------------------|---------------------------|
| Dependency style | `<ProjectReference>` to local source | `<PackageReference>` with version from feed/csproj |
| RPC analyzer | Direct ProjectReference with `OutputItemType="Analyzer"` because MSBuild project analyzers are not transitive | Carried transitively inside the `Lakona.Rpc.Core` package |
| Hotfix assets | Direct references to the internal Abstractions assembly and Generators analyzer because Game.Server's package bundling does not apply to ProjectReference | Both assets are carried by the `Lakona.Game.Server` package |
| NuGet.config | None needed | Written to E2E client dir |
| Program.cs | LakonaGameClient (same for all modes) | LakonaGameClient (same for all modes) |
