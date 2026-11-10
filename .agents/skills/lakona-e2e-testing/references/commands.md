# E2E Commands And Parameters

Run commands from the repository root with PowerShell 7 or later.

## Commands

The unified script is at `.agents/skills/lakona-e2e-testing/scripts/run-e2e.ps1`.
Run it with PowerShell 7 or later.

### Default smoke (ProjectReference)

```powershell
# Fastest feedback: godot + websocket + memorypack, ProjectReference mode
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1
```

Runtime verification defaults to in-memory Membership. External Membership
providers need real infrastructure, so this wrapper validates their generated
package/configuration shape in build-only mode while Daily Validation owns the
provider runtime contracts:

```powershell
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -MembershipProvider all -SkipRuntime
```

### LocalFeed (pre-publish)

```powershell
# Default smoke with local NuGet packages
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed

# Skip a preferred business/cluster/management port range when another local service uses it
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed -Port 30000 -FindAvailablePort

# Unity-facing build with local feed
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed -Engine unity -Transport kcp -Serializer memorypack

# Build-only smoke when investigating scaffold/build failures
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed -SkipRuntime

# Full matrix when requested or required by a release gate
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed -Engine all -Transport all -Serializer all

# Keep generated scaffolds for inspection
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed -KeepScaffolds
```

### Compose container verification

```powershell
pwsh -NoProfile -File .agents/skills/lakona-e2e-testing/scripts/run-e2e.ps1 -Feed LocalFeed -Compose -Engine godot -Transport all -Serializer memorypack -FindAvailablePort -KeepScaffolds
```

`-Compose` builds the generated Dockerfile from a clean scaffold and starts the
generated Compose service, then runs the same login/push client outside the
container. It requires a Linux Docker engine with Compose and `LocalFeed` or
`NuGetOrg`; source references outside the Docker context are not supported.
LocalFeed copies packages into the build context with a relative NuGet source.
The generated port variable changes both listener and published port for isolation;
all runtime settings come from the generated files. Each case owns a unique Compose project;
logs are retained and its containers, networks, volumes and local image are
removed after runtime verification. Cleanup failure fails the case.

### NuGetOrg (post-publish)

```powershell
# Verify published packages work for end users
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed NuGetOrg

# Full matrix against nuget.org
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed NuGetOrg -Engine all -Transport all -Serializer all
```

This mode still builds the checkout's Tool to scaffold projects. Use a checkout
whose generated package versions have been published. When validating a wrapper
fix while runtime releases remain local, run the fixed wrapper in an isolated
checkout of the matching published release and record that commit. Keep the
generated dependency versions intact; do not substitute latest versions to make
restore pass. Such a run verifies the wrapper against published packages, not
unpublished runtime changes.

### ProjectReference (dev feedback)

```powershell
# Single combination with ProjectReference (fastest)
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed ProjectReference

# Full matrix with source references
.\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed ProjectReference -Engine all -Transport all -Serializer all
```

### Matrix Coverage And Common Tasks

Choose the feed independently from the combination count. The default is one
Godot + WebSocket + MemoryPack case. Unity and Godot across three transports and
two serializers make **12 cases**. Run those explicitly, preserving both reports:

```powershell
$e2e = '.agents/skills/lakona-e2e-testing/scripts/run-e2e.ps1'
foreach ($engine in 'unity', 'godot') {
    pwsh -NoProfile -File $e2e -Feed ProjectReference -Engine $engine `
        -Transport all -Serializer all -MembershipProvider memory `
        -FindAvailablePort -WorkDir ".tmp/lakona-e2e-$engine"
    if ($LASTEXITCODE -ne 0) { throw "$engine E2E failed; inspect its report." }
}
```

`-Engine all -Transport all -Serializer all` also includes Tuanjie and therefore
runs **18 cases** with in-memory Membership. It does not include Console clients.
Record which combinations actually ran; one smoke pass does not establish a
complete matrix pass. Add `-KeepScaffolds` when retaining generated projects.

| Task | Unified entry point option or behavior |
| --- | --- |
| Validate current source | `-Feed ProjectReference` |
| Pack and validate local NuGet packages | `-Feed LocalFeed` |
| Validate published packages | `-Feed NuGetOrg`, using a published version baseline |
| Select combinations | `-Engine`, `-Transport`, `-Serializer`; select all dimensions explicitly for a full matrix |
| Scaffold and build only | `-SkipRuntime`; runtime is reported as skipped |
| Verify login and server push | Omit `-SkipRuntime`; the generated client checks both |
| Keep generated projects | `-KeepScaffolds`; failures retain their diagnostic artifacts |
| Preserve separate runs | Give each run a distinct `-WorkDir`; reports are `report.md` and `summary.json`, with per-phase logs |
| Handle occupied ports | `-Port` selects the preferred base; `-FindAvailablePort` finds a free business/cluster/management range without stopping unrelated processes |
| Propagate failure | A failed case produces a nonzero exit code; inspect the failed phase in its report |

When converting an existing command, replace `-DependencyMode NuGetFeed` with
`-Feed LocalFeed`, `-DependencyMode ProjectReference` with `-Feed ProjectReference`,
and `-Quick` with `-SkipRuntime`. Use explicit matrix dimensions rather than
assuming the default invocation covers every combination.

Daily Validation's `scripts/game/ci/verify-lakona-tool-godot.ps1` retains its
separate real Godot-engine and PostgreSQL Membership checks. The unified RPC
verification client does not replace those checks or exercise interactive UI.

### Parameter Reference

| Parameter | Values | Default | Description |
|-----------|--------|---------|-------------|
| `-Feed` | `ProjectReference`, `LocalFeed`, `NuGetOrg` | `ProjectReference` | Package source for generated project and E2E client |
| `-Engine` | `all`, `unity`, `tuanjie`, `godot` | `godot` | Client engine to scaffold |
| `-Transport` | `all`, `tcp`, `kcp`, `websocket` | `websocket` | RPC transport |
| `-Serializer` | `all`, `json`, `memorypack` | `memorypack` | RPC serializer |
| `-MembershipProvider` | `all`, `memory`, `postgres`, `redis`, `mysql` | `memory` | Generated Membership Adapter; external values require `-SkipRuntime` |
| `-SkipRuntime` | switch | off | Skip runtime E2E verification (scaffold + build only) |
| `-Compose` | switch | off | Build/run generated Compose instead of a host process; package feeds only |
| `-Port` | integer | `20000` | Base server port; matrix cases use consecutive ports |
| `-FindAvailablePort` | switch | off | Starting at `-Port`, select the first complete free business, cluster, and management port range |
| `-WorkDir` | path | `.tmp/lakona-e2e` | Output directory for scaffolds, logs, and reports |
| `-KeepScaffolds` | switch | off | Keep generated projects after test (default: clean up passing ones) |
