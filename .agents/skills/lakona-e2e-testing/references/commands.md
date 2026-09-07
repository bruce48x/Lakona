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
| `-MembershipProvider` | `all`, `memory`, `postgres`, `redis`, `mysql` | `memory` | Generated Membership Adapter; external values require `-SkipRuntime` |
| `-SkipRuntime` | switch | off | Skip runtime E2E verification (scaffold + build only) |
| `-Port` | integer | `20000` | Base server port; matrix cases use consecutive ports |
| `-FindAvailablePort` | switch | off | Starting at `-Port`, select the first complete free business, cluster, and management port range |
| `-WorkDir` | path | `.tmp/lakona-e2e` | Output directory for scaffolds, logs, and reports |
| `-KeepScaffolds` | switch | off | Keep generated projects after test (default: clean up passing ones) |
