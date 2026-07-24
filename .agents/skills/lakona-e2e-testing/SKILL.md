---
name: lakona-e2e-testing
description: >
  E2E-validate Lakona.Tool scaffolded projects (scaffold → build → start → RPC verify).
  The package source determines the validation target —
  --feed project : local source via ProjectReference, fastest dev feedback.
  --feed local   : locally-packed NuGet packages, pre-publish validation without waiting for nuget.org.
  --feed nuget   : published packages from nuget.org, post-publish user-experience verification.
  Trigger mapping: user says "local packages" / "pre-publish" / "before nuget" / "local feed" → local;
  user says "nuget packages" / "published" / "real packages" / "after publish" → nuget;
  user says "quick test" / "dev feedback" / "project reference" / no package source mentioned → project (default).
  When the user's intent is unclear, ask which package source to use.
  Do NOT use for: template-level checks (unit tests), RPC layer unit tests (Loopback transport), or Godot client UI testing.
---

# Lakona E2E Testing

Verify scaffolded Lakona projects end-to-end with real network round-trips.

**Core question:** "Does a Lakona.Tool generated project scaffold, restore, build, and respond to RPC calls correctly?"

## Required Context

Always read `CONTRIBUTING.md` first. It is the repository authority for package boundaries, version bump rules, Unity constraints, and validation expectations.

When the failure involves project generation architecture, read `docs/tool/lakona-tool-generation-architecture.md` before proposing fixes.

## Quick Reference: Feed Modes

This skill has ONE script with ONE parameter that changes everything — the package source:

| Mode | Flag | Package source | Use case | Speed |
|------|------|---------------|----------|-------|
| **ProjectReference** | `-Feed ProjectReference` (default) | Local source via `<ProjectReference>` | Dev feedback after code changes | Fastest |
| **LocalFeed** | `-Feed LocalFeed` | Locally packed `.nupkg` files | Pre-publish validation | Medium |
| **NuGetOrg** | `-Feed NuGetOrg` | Published packages on nuget.org | Post-publish verification | Slower (restore) |

All three modes scaffold, build, start the server, and run an RPC verification client. Only the dependency resolution differs.

## Commands

The unified script is at `.agents/skills/lakona-e2e-testing/scripts/run-e2e.ps1`.

### Default smoke (ProjectReference)

```powershell
# Fastest feedback: godot + websocket + memorypack, ProjectReference mode
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1
```

### LocalFeed (pre-publish)

```powershell
# Default smoke with local NuGet packages
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed

# Unity-facing build with local feed
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed -Engine unity -Transport kcp -Serializer memorypack

# Build-only smoke when investigating scaffold/build failures
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed -SkipRuntime

# Full matrix for release-grade confidence
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed -Engine all -Transport all -Serializer all

# Keep generated scaffolds for inspection
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed -KeepScaffolds
```

### NuGetOrg (post-publish)

```powershell
# Verify published packages work for end users
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed NuGetOrg

# Full matrix against nuget.org
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed NuGetOrg -Engine all -Transport all -Serializer all
```

### ProjectReference (dev feedback)

```powershell
# Single combination with ProjectReference (fastest)
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed ProjectReference

# Full matrix with source references
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed ProjectReference -Engine all -Transport all -Serializer all
```

### Parameter Reference

| Parameter | Values | Default | Description |
|-----------|--------|---------|-------------|
| `-Feed` | `ProjectReference`, `LocalFeed`, `NuGetOrg` | `ProjectReference` | Package source for generated project and E2E client |
| `-Engine` | `all`, `unity`, `tuanjie`, `godot` | `godot` | Client engine to scaffold |
| `-Transport` | `all`, `tcp`, `kcp`, `websocket` | `websocket` | RPC transport |
| `-Serializer` | `all`, `json`, `memorypack` | `memorypack` | RPC serializer |
| `-SkipRuntime` | switch | off | Skip runtime E2E verification (scaffold + build only) |
| `-Port` | integer | `20000` | Server port |
| `-WorkDir` | path | `.tmp/lakona-e2e` | Output directory for scaffolds, logs, and reports |
| `-KeepScaffolds` | switch | off | Keep generated projects after test (default: clean up passing ones) |

## What the Script Does

1. **Pack** (LocalFeed only): Packs all packable `src/Lakona.*.csproj` projects into a local NuGet feed
2. **Build Lakona.Tool**: Ensures the scaffolding tool is built
3. **Scaffold**: Runs `dotnet run --project src/Lakona.Tool -- new` for each combination
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

### E2E Client Architecture

| Aspect | ProjectReference mode | LocalFeed / NuGetOrg mode |
|--------|----------------------|---------------------------|
| Dependency style | `<ProjectReference>` to local source | `<PackageReference>` with version from feed/csproj |
| Analyzers | ProjectReference with `OutputItemType="Analyzer"` | PackageReference with PrivateAssets |
| NuGet.config | None needed | Written to E2E client dir |
| Program.cs | LakonaGameClient (same for all modes) | LakonaGameClient (same for all modes) |

## Validation Strategy

Choose the smallest run that can answer the question:

- **Default smoke**: `godot + websocket + memorypack` with runtime verification (all modes).
- **Tool template or generated layout change**: Run the affected engine plus the affected transport/serializer.
- **Transport change**: Run the changed transport with both serializers.
- **Serializer change**: Run the changed serializer across at least websocket and one socket transport.
- **Source generator or shared contract shape change**: Run default runtime verification first, then expand if it fails or passes but risk remains.
- **Pre-publish confidence** (LocalFeed): Run the full matrix; runtime verification remains enabled unless `-SkipRuntime` is explicitly requested.
- **Post-publish verification** (NuGetOrg): Run at least default smoke; full matrix for release announcements.
- **Fast dev iteration** (ProjectReference): Default smoke covers the most common code path.

Do not claim package-level confidence from repository tests alone. The point of the LocalFeed and NuGetOrg modes is to validate the package restore surface that generated users experience.

## Failure Triage

Classify failures before proposing code changes.

1. **Pack failure** (LocalFeed only)
   - Check the failing `src/<Package>/<Package>.csproj`.
   - Check version metadata and missing packed files.
   - If package source changed under `src/**`, verify the relevant `<Version>` was bumped according to `CONTRIBUTING.md`.

2. **Scaffold failure**
   - Inspect `src/Lakona.Tool/Cli`, option parser behavior, and `docs/tool/lakona-tool-generation-architecture.md`.
   - Treat deprecated CLI options in older scripts as script drift, not product regressions.
   - Current `new` options are `--name`, `--output`, `--client-engine`, `--client-engine-version`, `--transport`, `--serializer`, `--nugetforunity-source`, and `--deploy-profile`.

3. **Restore or build failure in generated project**
   - Inspect the generated `NuGet.config`, `Server/App/Server.App.csproj`, `Shared/Shared.csproj`, and local feed contents.
   - Check whether the generated package versions match the locally packed package versions.
   - Check analyzer and generator packages first when generated types are missing.
   - For ProjectReference mode: verify csproj patching replaced the correct PackageReference elements.

4. **Runtime verification failure**
   - Inspect generated server stdout/stderr (`server-out.txt`, `server-err.txt`) and the E2E client log.
   - Classify by transport connection, serializer payload, RPC dispatch, DI/hotfix loading, or contract mismatch.
   - Check if the server actually started (prefer "Lakona server started successfully"; WebSocket hosts may also emit "Application started").
   - Prefer a narrow framework fix over committing generated RPC glue or broad template rewrites.

5. **E2E client build failure**
   - Check that the E2E client can resolve all Lakona types.
   - For ProjectReference mode: verify all ProjectReference paths exist.
   - For LocalFeed mode: verify the local feed contains all needed packages.
   - For NuGetOrg mode: verify the published package versions match what the scaffold expects.
   - Source generator failures: check `CompilerVisibleProperty` items and analyzer references.

6. **Wrapper/script failure**
   - If the wrapper assumptions diverge from current generator behavior, update the wrapper or skill first.
   - Do not hide real product failures by weakening assertions.

## Output Contract

After running this skill, report:

- Exact command run (including `-Feed` mode).
- Combination matrix covered.
- Feed mode used and what it means for the results.
- Report path under `$WorkDir`.
- Pass/fail count.
- Most likely root cause for each failure.
- Whether the problem appears to be the framework, generated template, package metadata, or test wrapper.
- Concrete improvement options, with a recommended option.

Stop after analysis and proposed improvements unless the user explicitly approves implementation.
