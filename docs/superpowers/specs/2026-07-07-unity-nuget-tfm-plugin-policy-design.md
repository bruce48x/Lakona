# Unity NuGet TFM Plugin Policy Design

Date: 2026-07-07
Status: accepted design

## Problem

Unity and Tuanjie clients consume Lakona RPC packages through NuGetForUnity.
Those packages are multi-targeted (`netstandard2.1` and `net10.0`, with room for
future `net8.0` and other TFMs). NuGetForUnity extracts package assets under
`Client/Assets/Packages/**/lib/<tfm>/`.

Unity 2022.3-compatible projects compile against **.NET Standard 2.1**. Godot,
Console, and other SDK-style Lakona clients compile against **.NET 10** (and may
use **.NET 8** in the future). The multi-TFM package layout is therefore correct
and must remain.

The failure mode is Unity-specific:

1. NuGetForUnity installs more than one TFM folder for the same package.
2. `packages.config` `targetFramework="netstandard2.1"` affects NuGet dependency
   resolution, but does not reliably control which plugin DLL Unity enables or
   references at compile time.
3. `NuGet.config` `PreferNetStandardOverNetFramework=true` only prefers
   `netstandard` over **.NET Framework** (`net472`, etc.). It does **not**
   prefer `netstandard2.1` over `net10.0`.
4. When both `lib/netstandard2.1/*.dll` and `lib/net10.0/*.dll` are present and
   enabled in `PluginImporter`, Unity may reference the wrong assembly. Observed
   symptoms in `samples/Game.Unity.Agar/Client` include:
   - `CS7069` for `IAsyncDisposable` / `System.Runtime` 10.0.0.0
   - plugin load failure for `Lakona.Rpc.Transport.WebSocket` net10.0 because
     it references `Microsoft.AspNetCore.*`

This is not a business-code bug. It is a **Unity plugin import policy** gap.

`Assets/Packages/` is gitignored for sample and generated projects, so the bug
can appear after a local NuGet restore or package version bump even when
`packages.config` has not changed its declared TFM.

## Product Decision

Lakona must **not** solve this by splitting client NuGet packages or removing
`net10.0` (or future `net8.0`) assets from shared RPC packages. Godot, Console,
and future SDK clients depend on modern TFMs through normal `PackageReference`
resolution.

The fix belongs on the **Unity consumption boundary only**:

- keep multi-TFM Lakona RPC packages unchanged;
- enforce a deterministic Unity plugin policy after NuGet install/import;
- make `lakona-tool new` projects safe by default;
- backport the same policy to all maintained Unity samples that use NuGetForUnity.

## Goals

1. Generated Unity/Tuanjie/Unity-CN projects compile after NuGet restore without
   manual PluginImporter edits.
2. Unity clients always use the DLL set that matches the project's API
   compatibility level (today: `netstandard2.1`).
3. Godot/Console/SDK clients remain unaffected and continue using `net10.0`
   (or future `net8.0`) through `dotnet` + `PackageReference`.
4. The policy survives package upgrades and repeated NuGet restores.
5. Regression coverage prevents re-enabling incompatible TFMs in Unity plans.
6. A cold-opened, already-restored Unity project passes its first script compile
   without requiring a second domain reload.

## Non-Goals

- Splitting `Lakona.Rpc.Transport.*` into Unity-only or server-only package IDs.
- Removing `net10.0` (or `net8.0`) from published Lakona RPC NuGet packages.
- Forking or replacing NuGetForUnity.
- Changing Godot/Console client project TFMs or dependency planning.
- Teaching users to manually disable `net10.0` plugins in the Inspector.
- Handling NuGet assets outside `Assets/Packages/**/lib/<tfm>/` (for example
  `runtimes/**`, root-level `lib/*.dll`, or `tools/**`).

## Root Cause Summary

| Layer | What it does | Why it is insufficient alone |
| --- | --- | --- |
| `packages.config` `targetFramework` | NuGet dependency groups for install/restore | Does not disable extra extracted TFM folders |
| `NuGet.config` `PreferNetStandardOverNetFramework` | Prefer netstandard over net4x | Does not compare netstandard vs net10.0 |
| NuGetForUnity install | Extracts package lib assets | May leave multiple TFMs on disk |
| `PluginImporter` | Unity plugin enablement per DLL | Both TFMs can be enabled simultaneously |
| Unity/asmdef compile | Picks reference paths for `-r:` | May select incompatible net10.0 DLL |

## Recommended Design

### 1. Extend the generated Unity Import Guard

`Lakona.Tool` already generates
`Client/Assets/Editor/LakonaGameNuGetPackageImportGuard.cs` for Unity-compatible
clients. Today it only disables analyzer plugins under `Assets/Packages/**/analyzers/`.

Extend this guard into a **Unity NuGet plugin policy** with two responsibilities:

**A. Analyzer / generator policy (existing + clarified)**

- Disable any `Assets/Packages/**/analyzers/**/*.dll` with the same full disable
  routine used for forbidden TFMs: Any Platform, Editor, and explicit
  per-platform compatibility must all be off.
- Also disable Roslyn/generator runtime plugins under `lib/<allowed-tfm>/`, including
  `*.Generator.dll` and conservative known analyzer/generator package IDs, using
  the same disable rules as analyzers. Allowed TFM folders must not re-enable
  generator DLLs as player plugins.

**B. TFM policy (new)**

- Determine the Unity project's supported plugin TFM roots from API compatibility.
- For the initial implementation scope, target Unity 2022.3 / Tuanjie / Unity-CN
  defaults: select runtime plugins with this priority:
  - enable `Assets/Packages/**/lib/netstandard2.1/<assembly>.dll`;
  - enable `Assets/Packages/**/lib/netstandard2.0/<assembly>.dll` only when the
    same package does not contain `lib/netstandard2.1/<assembly>.dll`;
  - disable the lower-priority `netstandard2.0` DLL when the same package also
    contains the `netstandard2.1` DLL for that assembly.
- Treat these as **forbidden** plugin roots:
  - `lib/net10.0/`, `lib/net9.0/`, `lib/net8.0/`, `lib/net7.0/`, `lib/net6.0/`;
  - `lib/net472/`, `lib/net48/`, `lib/net481/`, and other legacy .NET Framework
    folders when present.

**Enable / disable semantics (required)**

Disabling forbidden DLLs is not sufficient. The guard must implement both paths:

- **Forbidden TFM runtime DLLs:** disable Any Platform, Editor, and explicit
  per-platform compatibility flags. A DLL is non-compliant if Editor or any
  standalone target is enabled, not only when Any Platform is enabled.
- **Selected allowed TFM runtime DLLs (non-analyzer, non-generator):** explicitly
  enable plugin import for player targets, e.g. `SetCompatibleWithAnyPlatform(true)`,
  when currently disabled.
- **Unselected fallback DLLs:** disable lower-priority fallback DLLs, such as
  `netstandard2.0`, when a higher-priority sibling for the same assembly exists.
- **No-op when already correct:** call `SaveAndReimport()` only when enablement or
  disablement actually changes.

**Timing (required)**

- Run a synchronous full scan from the `[InitializeOnLoad]` static constructor
  before first compile on editor load (not only `EditorApplication.delayCall`).
- Also run on `AssetPostprocessor.OnPostprocessAllAssets` for imported/moved DLLs.
- After batch policy fixes, request script recompilation when any importer changed.
- `delayCall` may remain as a secondary safety net, but must not be the only
  first-run path.

**Implementation constraints**

- Keep the guard in generated `Assets/Editor/` code (no new runtime package
  required for v1).
- Use `AssetDatabase.StartAssetEditing()` / `StopAssetEditing()` around batch
  scans to reduce repeated domain reloads.
- Match paths with normalized `/` separators and case-insensitive segment checks.
- Resolve the package root, TFM segment, and file name from the asset path before
  applying the `netstandard2.1` / `netstandard2.0` sibling preference.
- Do not touch DLLs outside `Assets/Packages/`.
- Tuanjie uses a different NuGet feed in generated `NuGet.config`; TFM policy is
  unchanged. Unity-CN shares the same API compatibility default as Unity 2022.3.

**Future-proofing note**

When Lakona documents/supports Unity versions whose Api Compatibility Level maps
to `.NET 8+`, the guard should read that setting and switch allowed TFM roots
accordingly. The first implementation may hardcode the current default
(`netstandard2.1`) inside a single helper that can later map compatibility level
to allowed `lib/<tfm>/` folders.

### 2. Make generated NuGet metadata express intent

Update `Lakona.Tool` rendering so **every** Unity-planned `packages.config` entry
includes `targetFramework="netstandard2.1"` (including transitive packages such as
`System.Memory`, `MemoryPack.Core`, and Roslyn dependencies).

```xml
<package id="Lakona.Rpc.Transport.Kcp" version="..." targetFramework="netstandard2.1" manuallyInstalled="true" />
```

Changes:

- `PackageReferenceRenderer.RenderNuGetForUnityPackage` always emits
  `targetFramework="netstandard2.1"` for Unity-planned packages.
- Add a short comment block to generated `Client/Assets/NuGet.config` explaining:
  - `targetFramework` guides NuGet dependency resolution;
  - Unity plugin enablement is enforced by
    `LakonaGameNuGetPackageImportGuard`.

This does not replace the guard, but keeps generated project metadata aligned
with actual Unity runtime expectations.

### 3. Backport to maintained Unity samples

Apply to all maintained Unity samples under `samples/` that use NuGetForUnity:

- `samples/Game.Unity.Agar/Client`
- `samples/Rpc.Unity.MemoryPack.Kcp/Client`
- `samples/Rpc.Unity.MemoryPack.Tcp/Client`
- `samples/Rpc.Unity.Json.Websocket/Client`

Prefer a **single template source** in `Lakona.Tool` (`RenderNuGetPackageImportGuard`)
consumed by both the generator and samples. Do not duplicate guard logic in sample
trees long term.

Also align sample `packages.config` entries with `targetFramework="netstandard2.1"`
where missing.

Hand-written Unity projects outside Lakona samples may copy
`LakonaGameNuGetPackageImportGuard.cs` as a standalone Editor script.

### 4. Add Unity-only regression coverage

**Tool tests**

- Assert generated Unity/Tuanjie/Unity-CN plans include the extended guard source
  with TFM enable/disable rules (dedicated `ImportGuard` tests, not only
  `ClientRenderer` path checks).
- Assert every generated `packages.config` package entry includes
  `targetFramework="netstandard2.1"`.
- Assert analyzer disabling behavior remains intact.

**Repository script / CI helper (recommended, not optional for merge)**

Add `scripts/game/ci/check-unity-nuget-plugin-policy.ps1` that fails when a Unity
client tree contains either:

- forbidden TFM plugin metas with **Any Platform, Editor, or any standalone
  platform** enabled;
- `netstandard2.0` fallback plugin metas enabled while the same package contains
  a `netstandard2.1` sibling for the same assembly.

**Manual / MCP validation (Agar sample release gate)**

After restore and domain reload, Unity console must not report:

- `CS7069` from Lakona RPC transport/serializer factories
- `Microsoft.AspNetCore.*` unresolved reference errors from
  `Lakona.Rpc.Transport.WebSocket` net10.0 plugins

Bee compile response files for Unity gameplay/rpc assemblies should reference
`lib/netstandard2.1/` for Lakona RPC packages, not `lib/net10.0/`.

Cold-open validation: opening an already-restored project must pass the first
script compile without requiring a manual reimport.

## Architecture

```txt
Lakona RPC NuGet package (multi-TFM nupkg)
        |
        +--> Godot/Console (.csproj, net10.0)
        |         NuGet restore -> net10.0 assets (unchanged)
        |
        +--> Unity (NuGetForUnity + Assets/Packages)
                  restore extracts multiple TFMs
                  Import Guard enforces allowed lib/<tfm>/ enablement
                  Unity compiles against netstandard2.1 plugins only
```

The guard is the Unity-side adapter between **portable multi-TFM packages** and
**Unity's single plugin import model**.

## Alternatives Considered

### A. Rely on `packages.config targetFramework` only

Rejected. It does not control PluginImporter state or Unity `-r:` selection when
multiple TFMs are extracted.

### B. Split client packages / remove net10.0 from nupkgs

Rejected. SDK clients (Godot, Console, future net8.0 clients) require modern
TFMs in the same package line. Unity-specific constraints must not shrink the
shared package surface.

### C. Post-restore delete `lib/net10.0` folders from `Assets/Packages`

Rejected as primary strategy. Deleting files fights NuGetForUnity's install
model and makes repeated restores brittle. Disabling plugins via
`PluginImporter` is the Unity-native, idempotent approach.

### D. Generated Import Guard with TFM policy (recommended)

Accepted. Reuses existing Lakona.Tool pattern, applies only to Unity, survives
package upgrades, and keeps multi-TFM packages intact.

### E. Upgrade NuGetForUnity TFM selection only

Rejected as sole fix. May help dependency resolution but does not reliably
control PluginImporter enablement when multiple TFMs remain on disk. Guard remains
required even if NuGetForUnity behavior improves later.

## Compatibility Boundary

- **In scope:** Unity, Unity-CN, Tuanjie clients generated by `Lakona.Tool`, plus
  all maintained Unity samples listed above.
- **Out of scope:** Hand-written Unity projects that do not adopt the guard.
- **Unchanged:** Godot/Console dependency planning (`DependencyPlanner` SDK
  packages, `TargetFramework=net10.0`), server `net10.0` projects, and Lakona
  RPC package multi-target publishing.

## Risks

| Risk | Mitigation |
| --- | --- |
| Import loop from aggressive `SaveAndReimport()` | Only save when importer flags actually change; batch with `StartAssetEditing` |
| First compile before guard scan | Synchronous scan on load + postprocess on import; recompile after policy fixes |
| Future Unity uses .NET 8+ API compatibility | Centralize allowed TFM mapping in one guard helper |
| Third-party NuGet packages with unusual lib layout | Restrict policy to `Assets/Packages/**/lib/<tfm>/`; document non-goals |
| Generator/Roslyn DLLs under `lib/` enabled as plugins | Keep analyzer-style disable for `/analyzers/` and `*.Generator.dll` |
| `netstandard2.0` fallback reintroduces duplicate assemblies | Enable it only when the same package lacks a `netstandard2.1` sibling for that assembly |
| Sample drift from generated guard | Single template source in `Lakona.Tool`; CI meta check |
| `asmdef` bare `precompiledReferences` ambiguity | Guard ensures only allowed TFM DLLs are enabled; validate Bee `.rsp` paths |

## Test And Validation Expectations

Focused tests and checks should cover:

- generated Unity plan includes extended `LakonaGameNuGetPackageImportGuard`;
- generated `packages.config` entries all declare `targetFramework="netstandard2.1"`;
- guard source contains forbidden TFM disable rules and allowed TFM enable rules;
- guard source prefers `netstandard2.1` over `netstandard2.0` for the same package
  assembly and disables the unselected fallback;
- analyzer and generator disabling behavior remains intact;
- Agar Unity client compiles after NuGet restore with no Lakona RPC `CS7069` or
  AspNetCore plugin load errors;
- Godot/Console tool matrix tests remain green (no change to SDK client TFMs);
- `check-unity-nuget-plugin-policy.ps1` fails on forbidden enabled metas and
  enabled fallback metas shadowed by `netstandard2.1` siblings;
- cold-open first compile passes on an already-restored Unity client.

Before implementation is considered complete, run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter "FullyQualifiedName~ClientRenderer|FullyQualifiedName~PackageReferenceRenderer|FullyQualifiedName~ImportGuard"
pwsh -NoProfile -File scripts/game/ci/check-unity-nuget-plugin-policy.ps1 -ClientPath samples/Game.Unity.Agar/Client
```

and the existing Lakona.Tool matrix / Agar Unity validation path appropriate
for the change.

## Documentation Updates

- `src/Lakona.Tool/README.md`: guard prevents analyzer **and incompatible TFM**
  plugins, not analyzers only.

## Implementation Follow-Up

Implementation plan:

`docs/superpowers/plans/2026-07-07-unity-nuget-tfm-plugin-policy.md`
