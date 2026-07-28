# Package Version Graph Guard

Status: active policy
Date: 2026-07-03
Audience: maintainers and release automation contributors

## Purpose

Lakona packages are published from a monorepo, but NuGet packages are immutable
once a `PackageId` + `Version` pair exists. If a package dependency changes and
the consuming package keeps the same version, a later publish with
`--skip-duplicate` cannot repair the already-published consumer package. Users
then restore stale dependency metadata even though the source tree looks
correct.

This document defines a local, graph-based version guard. It detects when a
package or generated-template dependency edge changed and requires every
affected published package to receive a new version. It intentionally does not
query nuget.org or compare against already-published remote packages.

## Non-Goals

- Detecting or repairing already-published bad packages.
- Deciding semantic version numbers or major/minor/patch policy.
- Replacing focused API or runtime compatibility tests.
- Requiring every downstream application to upgrade all packages together.
- Inferring external package compatibility from NuGet ranges.

## Problem Model

`dotnet pack` converts most `ProjectReference` edges between packable projects
into package dependencies in the generated `.nuspec`.

For example:

```txt
Lakona.Game.Server        -> Lakona.Rpc.Server
Lakona.ProjectSystem      -> generated starter package versions
Lakona.Tool               -> Lakona.ProjectSystem
```

If `Lakona.Rpc.Server` changes from version `X` to `Y`, then
`Lakona.Game.Server` must publish a new version so its `.nuspec` depends on
`Y`. If generated projects embed `Game.Server`, `Lakona.ProjectSystem` and its
Tool consumer must also publish new versions.

Some package assets come from internal, non-packable projects. Their owning
package declares each such project with `PackageInputProject`; a source change
under that project requires a new owner package version even though there is no
NuGet dependency edge. `Lakona.Game.Server.Hotfix.Abstractions` and
`Lakona.Game.Server.Hotfix.Generators` use this path because their assemblies
ship inside `Lakona.Game.Server`. `Lakona.Rpc.Analyzers` uses the same path
because its compiler extension ships inside `Lakona.Rpc.Core`.

The required behavior is not specific to Hotfix packages. It applies to every
packable package dependency chain in `src/**`.

## Definitions

- **Package node**: a `src/**.csproj` with a `PackageId` and a package version,
  unless explicitly marked non-packable with `IsPackable=false`.
- **Artifact dependency edge**: a dependency from package node `A` to package
  node `B` that can appear in `A`'s generated package metadata.
- **Version-source edge**: a dependency from package node `A` to package node
  `B` where `A` embeds `B`'s package version into generated code, templates, or
  other packed content.
- **Bundled project input**: a non-packable project named by a package node's
  `PackageInputProject` item whose output is embedded in the owner's package.
- **Changed package artifact**: a package node whose packed source/content or
  package version changed between the selected Git base and head.
- **Required bump**: a package node that must change its `<Version>` because
  its own packed artifact changed or because an upstream dependency node
  changed.

## Graph Construction

The guard builds a directed graph from current source files:

```txt
consumer package -> dependency package
```

### Package Nodes

Scan `src/**/*.csproj`. A project is a package node when all are true:

- It has `PackageId`.
- It has `Version`.
- `IsPackable` is not `false`.

`PackAsTool=true` projects are normal package nodes.

### Artifact Dependency Edges

For each package node, parse `ProjectReference` items. If the referenced project
is also a package node, add an artifact dependency edge unless the reference is
explicitly configured as a non-package implementation detail.

Initial suppression rule:

- Ignore a `ProjectReference` only when it has both
  `ReferenceOutputAssembly="false"` and `PrivateAssets="all"`.

This keeps the first implementation simple and conservative. If future projects
need a non-packaging reference that does not match this rule, add an explicit
metadata marker instead of special-casing package names.

### Bundled Project Inputs

For each `PackageInputProject`, treat the referenced project file and every
source file under its directory as packed inputs of the declaring package.
This is an ownership marker, not a NuGet dependency edge, so the internal
project does not become a package node or appear in a generated `.nuspec`.

### Version-Source Edges

Some package metadata changes are not `ProjectReference` edges. The current
important case is `Lakona.ProjectSystem`, whose build target reads runtime package
versions through `XmlPeek` and writes `GeneratedProjectPackageVersions.g.cs`.

The guard should discover these edges by parsing package project files for
`XmlPeek` tasks whose `Query` is `/Project/PropertyGroup/Version/text()` and
whose `XmlInputPath` resolves to another package node. Every matching task in a
packable project is a version-source edge unless it is explicitly suppressed by
future metadata. Do not attempt general MSBuild property dataflow analysis.

For `Lakona.ProjectSystem`, each such input project is a version-source edge:

```txt
Lakona.ProjectSystem -> package read by GenerateProjectPackageVersions
```

The rule is intentionally structural: the edge comes from the project file, not
from a hard-coded package allowlist.

## Change Detection

The guard compares two Git trees:

- `base`: the selected comparison base.
- `head`: the commit or working tree being validated.

Default base/head resolution:

- If explicit `LAKONA_VERSION_GUARD_BASE` and `LAKONA_VERSION_GUARD_HEAD`
  overrides are present, use them.
- Otherwise find the most recent commit where
  `src/Lakona.Tool/Lakona.Tool.csproj` changed its `<Version>` value.
- If package-relevant changes exist after that latest Tool version anchor, use
  the latest Tool version anchor commit as `base`.
- If no package-relevant changes exist after the latest Tool version anchor,
  use the previous Tool version anchor commit as `base`, falling back to the
  latest anchor's first parent when there is no previous anchor.
- Use `HEAD` as `head` for a clean working tree.
- Use the working tree as `head` when tracked or untracked local changes are
  present.

This keeps package-only changes after a completed Tool release anchored after
that historical Tool bump, so a missing new Tool bump is still reported. When
the latest Tool bump is the current release boundary, the guard falls back to
the previous Tool anchor so the current Tool bump and the package changes that
caused it stay in the same comparison range.

For each package node:

1. Read current and base `<Version>`.
2. Detect whether any file that can affect the packed artifact changed.
3. Mark `versionChanged` when the `<Version>` value differs.
4. Mark `artifactChanged` when packed inputs changed or `versionChanged` is
   true.

Packed input detection should start conservative:

- Include files under the package project directory.
- Include files linked into the project with `Compile Include`, `None Include`,
  `EmbeddedResource Include`, or equivalent packable item metadata.
- Include project directories declared through `PackageInputProject`.
- Include repository-level build inputs that can affect all package outputs:
  `Directory.Build.props`, `Directory.Build.targets`, `global.json`, and shared
  imported `.props` or `.targets` files under repository-owned build paths.
- Exclude `bin/**`, `obj/**`, editor caches, and generated build outputs.
- Exclude test projects and docs outside the package directory unless the
  package explicitly packs them.

## Required Bump Algorithm

The algorithm computes required version bumps from changed package artifacts and
the reverse dependency graph.

```txt
required = empty set
work = all package nodes where artifactChanged is true

for each node in work:
  add node to required

while work is not empty:
  changed = pop(work)
  for each consumer in reverseEdges[changed]:
    if consumer not in required:
      add consumer to required
      push consumer into work

failures = all nodes in required where versionChanged is false
```

The transitive walk is required. If `B` changes, `A -> B` must bump. Once `A`
bumps, `C -> A` must also bump so `C`'s `.nuspec` points at the new `A`
version.

`Lakona.Tool` is the repository release anchor. If any package in the required
set other than `Lakona.Tool` changes, `Lakona.Tool` must also change version.
This keeps generated project package constants aligned with each repository
release and gives the guard a stable local comparison anchor for the next run.

The guard should report all failures in one run. A failure message should show:

- the package that must bump,
- the changed dependency path that forced the bump,
- the current version,
- the file or dependency edge that changed.

Example shape:

```txt
Lakona.Game.Server must bump because:
  Lakona.Game.Server changed its packed inputs.
Current Lakona.Game.Server version is unchanged at <current>.
```

## Expected Behavior

### Runtime Package Change

If `Lakona.Rpc.Core` source changes and its version changes, every packable
package that directly or transitively depends on `Lakona.Rpc.Core` through
artifact dependency edges must also change version.

### Bundled Hotfix Asset Change

If the internal Hotfix abstractions or generator project changes, its
`PackageInputProject` owner must publish a new version. The normal generated
version-source edges then carry that change through:

```txt
Lakona.Game.Server
Lakona.ProjectSystem
Lakona.Tool
```

`Lakona.ProjectSystem` is reached through the generated `Lakona.Game.Server`
version-source edge, then `Lakona.Tool` through its package reference, not
through a hard-coded Hotfix rule.

### Bundled RPC Analyzer Change

If the internal `Lakona.Rpc.Analyzers` project changes, its
`PackageInputProject` owner `Lakona.Rpc.Core` must publish a new version. The
normal package and generated version-source edges then carry that change
through every RPC runtime consumer, `Lakona.Game.Server`, and generated project
tooling without an analyzer-specific guard rule.

### Tool Template Version Change

If a runtime package version changes and `Lakona.ProjectSystem` embeds that
version into generated starter projects, both `Lakona.ProjectSystem` and
`Lakona.Tool` must change version even if no Tool implementation file changed.

### Isolated Tool Code Change

If only `src/Lakona.Tool` implementation changes, only `Lakona.Tool` is
required to bump unless other graph edges or packed inputs changed.

## Execution Model

This guard should not be a standalone PowerShell implementation with ad hoc XML
parsing. It should be a non-packable .NET repository-guard test project that is
included in the normal test solution, with a thin script wrapper only for
explicit local or CI invocation.

Required shape:

```txt
tests/Lakona.RepositoryGuards.Tests/
  Lakona.RepositoryGuards.Tests.csproj
  PackageVersions/
    PackageVersionGraphFixtureTests.cs
    PackageVersionGraphRepositoryTests.cs
    PackageGraph.cs
    PackageProjectReader.cs
    GitChangeSetReader.cs
scripts/nuget/check-package-version-graph.ps1
```

Why this shape:

- It runs automatically during `dotnet test Lakona.slnx` and
  `dotnet test tests/Tests.slnx`.
- It stays cross-platform and uses normal .NET XML/path handling.
- The graph algorithm can have fast fixture tests without shelling out to Git.
- The repository integration test can shell out to Git once, then run the
  in-memory graph algorithm.
- The PowerShell script is a convenience entry point, not the source of truth.

### Local Commit Enforcement

Each clone should configure the tracked hooks with:

```powershell
pwsh -NoProfile -File scripts/git/install-hooks.ps1
```

When NuGet or Hub release inputs are staged, `.githooks/pre-commit` delegates
to `scripts/check-release-version-guards.ps1`. The hook only selects whether
the guards need to run; the .NET repository tests remain the single authority
for graph construction, base selection, and required version bumps. This moves
the same failure from post-push CI to the local commit boundary without
duplicating release policy.

The guard must be fast enough to run on every development finish pass:

- It must not restore, build, pack, or query NuGet.
- It should read only project files, relevant repository metadata, and `git
  diff --name-only` output.
- Its expected cost is proportional to package projects plus dependency edges
  plus changed paths.
- For the current repository size, the target runtime is under one second after
  the test assembly is loaded.

The guard must be precise enough that maintainers do not learn to ignore it:

- It should report all missing version bumps in one failure.
- It should show the dependency path that forced each bump.
- It should fail when the comparison base cannot be resolved instead of
  silently skipping.
- It should use explicit suppressions for known structural exceptions rather
  than package-name special cases.

## Local Base Resolution

The test project should resolve base/head the same way whether invoked through
`dotnet test` or the wrapper script.

Input overrides:

- `LAKONA_VERSION_GUARD_BASE`
- `LAKONA_VERSION_GUARD_HEAD`

Default behavior:

1. If both overrides are present, use them.
2. Resolve the latest committed `Lakona.Tool.csproj` `<Version>` change.
3. If package-relevant changes exist after that latest Tool anchor, use the
   latest Tool anchor commit as `base`; otherwise use the previous Tool anchor,
   falling back to the latest anchor's first parent when needed.
4. If the working tree has tracked or untracked changes, use the working tree
   as head; otherwise use `HEAD`.
5. If no reliable Tool version anchor can be resolved, fail with an actionable
   message telling the developer to set `LAKONA_VERSION_GUARD_BASE`.

The guard should print the resolved base/head at the start of the test output.
It must not skip silently.

## Implementation Plan Shape

Prefer a small repository-guard test project plus a thin script wrapper:

```txt
scripts/nuget/check-package-version-graph.ps1
tests/Lakona.RepositoryGuards.Tests/PackageVersions/PackageVersionGraphRepositoryTests.cs
```

The test project owns the algorithm and repository integration:

- parse current and base project files,
- run Git diff commands,
- build graph edges,
- print actionable failures,
- fail the test on missing bumps.

The tests own parser and algorithm behavior with small fixture graphs:

- direct dependency bump,
- transitive dependency bump,
- version-source edge bump,
- unchanged dependency does not force bump,
- non-packable project is ignored,
- suppressed project reference is ignored.

Keep tests fixture-based. Do not hard-code real package versions such as
`0.2.9`, `0.3.13`, or `0.15.2` into algorithm tests.

The script wrapper should only set environment variables and run:

```powershell
dotnet test tests/Lakona.RepositoryGuards.Tests/Lakona.RepositoryGuards.Tests.csproj --filter PackageVersionGraph
```

## CI Integration

Add a dedicated step before packing NuGet packages:

```yaml
- name: Checkout
  uses: actions/checkout@v5
  with:
    fetch-depth: 0

- name: Check package version graph
  run: dotnet test tests/Lakona.RepositoryGuards.Tests/Lakona.RepositoryGuards.Tests.csproj --no-build -c Release --filter PackageVersionGraph
```

The publish workflow should fail before `dotnet pack` if a required package
version bump is missing. This preserves `--skip-duplicate` behavior while
preventing stale dependency metadata from being generated in the first place.

The checkout step must fetch enough history for the latest `Lakona.Tool`
version change and its parent to exist locally. A shallow checkout is not
sufficient for the default anchor-based analysis.

CI should normally rely on the default Tool anchor. Explicit base/head
overrides are reserved for maintainer diagnostics and unusual repository
history repairs.

The test should print the resolved base and head before analysis. The wrapper
script may be used locally, but CI should call the test project directly after
the repository build/test restore step has already made test dependencies
available.

## Maintenance Rules

- Do not encode accident-specific package IDs or version thresholds.
- Treat new packable projects as graph nodes automatically.
- Treat new `ProjectReference` edges between package nodes as dependency edges
  automatically.
- Add explicit edge metadata only for structural exceptions.
- Keep failure output concrete enough that a maintainer knows exactly which
  package version must change and why.
- Keep online NuGet metadata comparison as a separate future guard if needed.
