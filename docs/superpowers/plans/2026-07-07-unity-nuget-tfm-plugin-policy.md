# Unity NuGet TFM Plugin Policy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure Unity/Tuanjie/Unity-CN clients always compile against netstandard2.1 NuGet plugins while multi-TFM Lakona packages remain unchanged for Godot/Console net10.0 clients.

**Architecture:** Extend the generated `LakonaGameNuGetPackageImportGuard` Editor script to synchronously enforce PluginImporter enable/disable rules on `Assets/Packages/**/lib/<tfm>/` DLLs; align `packages.config` metadata; backport to all NuGetForUnity Unity samples; add Tool tests and a PS meta checker.

**Tech Stack:** C# (`Lakona.Tool` string templates), Unity Editor `PluginImporter` API, NuGetForUnity `packages.config`, xUnit, PowerShell 7.

**Design spec:** `docs/superpowers/specs/2026-07-07-unity-nuget-tfm-plugin-policy-design.md`

---

## File Map

| File | Action |
| --- | --- |
| `src/Lakona.Tool/Rendering/Client/UnityClientCodeTemplates.cs` | Rewrite `RenderNuGetPackageImportGuard()` |
| `src/Lakona.Tool/Rendering/Common/PackageReferenceRenderer.cs` | Emit `targetFramework="netstandard2.1"` |
| `src/Lakona.Tool/Rendering/Client/UnityClientRenderer.cs` | Add `NuGet.config` comment block |
| `src/Lakona.Tool/README.md` | Update guard description |
| `tests/Lakona.Tool.Tests/Rendering/ImportGuardTemplateTests.cs` | New tests |
| `tests/Lakona.Tool.Tests/Rendering/PackageReferenceRendererTests.cs` | Update assertions |
| `tests/Lakona.Tool.Tests/Rendering/ClientRendererTests.cs` | Optional guard content assertions |
| `scripts/game/ci/check-unity-nuget-plugin-policy.ps1` | New CI helper |
| `samples/Game.Unity.Agar/Client/Assets/Editor/LakonaGameNuGetPackageImportGuard.cs` | Add (match template) |
| `samples/Rpc.Unity.*/Client/Assets/Editor/...` | Add (3 samples) |
| `samples/*/Client/Assets/packages.config` | Add missing `targetFramework` |

---

### Task 1: Add failing Import Guard template tests

**Files:**
- Create: `tests/Lakona.Tool.Tests/Rendering/ImportGuardTemplateTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Lakona.Tool.Rendering.Client;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class ImportGuardTemplateTests
{
    [Fact]
    public void RenderNuGetPackageImportGuard_ContainsForbiddenTfmDisableRules()
    {
        var source = UnityClientCodeTemplates.RenderNuGetPackageImportGuard();

        Assert.Contains("net10.0", source, StringComparison.Ordinal);
        Assert.Contains("net8.0", source, StringComparison.Ordinal);
        Assert.Contains("SetCompatibleWithAnyPlatform(false)", source, StringComparison.Ordinal);
        Assert.Contains("SetCompatibleWithEditor(false)", source, StringComparison.Ordinal);
        Assert.Contains("SetCompatibleWithPlatform", source, StringComparison.Ordinal);
        Assert.Contains("BuildTarget", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNuGetPackageImportGuard_ContainsAllowedTfmEnableRules()
    {
        var source = UnityClientCodeTemplates.RenderNuGetPackageImportGuard();

        Assert.Contains("PreferredRuntimeTfm", source, StringComparison.Ordinal);
        Assert.Contains("netstandard2.1", source, StringComparison.Ordinal);
        Assert.Contains("SetCompatibleWithAnyPlatform(true)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNuGetPackageImportGuard_PrefersNetstandard21OverNetstandard20()
    {
        var source = UnityClientCodeTemplates.RenderNuGetPackageImportGuard();

        Assert.Contains("FallbackRuntimeTfm", source, StringComparison.Ordinal);
        Assert.Contains("netstandard2.0", source, StringComparison.Ordinal);
        Assert.Contains("HasHigherPriorityRuntimeSibling", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNuGetPackageImportGuard_RunsSynchronousScanOnLoad()
    {
        var source = UnityClientCodeTemplates.RenderNuGetPackageImportGuard();

        Assert.Contains("ApplyNuGetPluginPolicy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("delayCall += DisableExistingAnalyzerPlugins", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNuGetPackageImportGuard_StillDisablesAnalyzers()
    {
        var source = UnityClientCodeTemplates.RenderNuGetPackageImportGuard();

        Assert.Contains("/analyzers/", source, StringComparison.Ordinal);
        Assert.Contains("IsAnalyzerOrGeneratorPlugin", source, StringComparison.Ordinal);
        Assert.Contains("KnownAnalyzerPackageIds", source, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter "FullyQualifiedName~ImportGuardTemplateTests" --no-restore
```

Expected: FAIL (missing strings / old delayCall-only implementation)

---

### Task 2: Implement extended Import Guard template

**Files:**
- Modify: `src/Lakona.Tool/Rendering/Client/UnityClientCodeTemplates.cs` (`RenderNuGetPackageImportGuard`)

- [ ] **Step 1: Replace `RenderNuGetPackageImportGuard()` body**

Implement a generated Editor script with this structure (adapt naming to fit existing style):

```csharp
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;

[InitializeOnLoad]
internal sealed class LakonaGameNuGetPackageImportGuard : AssetPostprocessor
{
    private const string PreferredRuntimeTfm = "netstandard2.1";
    private const string FallbackRuntimeTfm = "netstandard2.0";

    private static readonly string[] ForbiddenTfms =
    {
        "net10.0", "net9.0", "net8.0", "net7.0", "net6.0",
        "net472", "net48", "net481"
    };

    private static readonly string[] KnownAnalyzerPackageIds =
    {
        "Lakona.Rpc.Analyzers",
        "MemoryPack.Generator",
        "Microsoft.CodeAnalysis.Common",
        "Microsoft.CodeAnalysis.CSharp"
    };

    static LakonaGameNuGetPackageImportGuard()
    {
        ApplyNuGetPluginPolicy();
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        var touched = false;
        foreach (var assetPath in importedAssets)
        {
            touched |= TryApplyPolicy(assetPath);
        }

        foreach (var assetPath in movedAssets)
        {
            touched |= TryApplyPolicy(assetPath);
        }

        if (touched)
        {
            CompilationPipeline.RequestScriptCompilation();
        }
    }

    private static void ApplyNuGetPluginPolicy()
    {
        var changed = false;
        AssetDatabase.StartAssetEditing();
        try
        {
            var pluginGuids = AssetDatabase.FindAssets("t:PluginImporter", new[] { "Assets/Packages" });
            foreach (var guid in pluginGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                changed |= TryApplyPolicy(assetPath);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        if (changed)
        {
            CompilationPipeline.RequestScriptCompilation();
        }
    }

    private static bool TryApplyPolicy(string assetPath)
    {
        var normalizedPath = assetPath.Replace('\\', '/');
        if (!normalizedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.IndexOf("Assets/Packages/", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
        if (importer == null)
        {
            return false;
        }

        if (IsAnalyzerOrGeneratorPlugin(normalizedPath))
        {
            return DisableAllPlatforms(importer);
        }

        if (TryGetLibAsset(normalizedPath, out var packageRoot, out var tfm, out var fileName))
        {
            if (IsForbiddenTfm(tfm))
            {
                return DisableAllPlatforms(importer);
            }

            if (IsPreferredRuntimeTfm(tfm))
            {
                return EnableRuntimePlugin(importer);
            }

            if (IsFallbackRuntimeTfm(tfm))
            {
                return HasHigherPriorityRuntimeSibling(packageRoot, fileName)
                    ? DisableAllPlatforms(importer)
                    : EnableRuntimePlugin(importer);
            }
        }

        return false;
    }

    private static bool IsAnalyzerOrGeneratorPlugin(string normalizedPath)
    {
        return normalizedPath.IndexOf("/analyzers/", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedPath.IndexOf(".Generator.dll", StringComparison.OrdinalIgnoreCase) >= 0
            || IsKnownAnalyzerPackage(normalizedPath);
    }

    private static bool IsKnownAnalyzerPackage(string normalizedPath)
    {
        foreach (var packageId in KnownAnalyzerPackageIds)
        {
            var packageMarker = "Assets/Packages/" + packageId + ".";
            if (normalizedPath.IndexOf(packageMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetLibAsset(
        string normalizedPath,
        out string packageRoot,
        out string tfm,
        out string fileName)
    {
        const string marker = "/lib/";
        var libIndex = normalizedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (libIndex < 0)
        {
            packageRoot = string.Empty;
            tfm = string.Empty;
            fileName = string.Empty;
            return false;
        }

        var start = libIndex + marker.Length;
        var end = normalizedPath.IndexOf('/', start);
        if (end < 0)
        {
            packageRoot = string.Empty;
            tfm = string.Empty;
            fileName = string.Empty;
            return false;
        }

        var fileStart = normalizedPath.LastIndexOf('/') + 1;
        if (fileStart <= end)
        {
            packageRoot = string.Empty;
            tfm = string.Empty;
            fileName = string.Empty;
            return false;
        }

        packageRoot = normalizedPath.Substring(0, libIndex);
        tfm = normalizedPath.Substring(start, end - start);
        fileName = normalizedPath.Substring(fileStart);
        return true;
    }

    private static bool IsPreferredRuntimeTfm(string tfm) =>
        string.Equals(tfm, PreferredRuntimeTfm, StringComparison.OrdinalIgnoreCase);

    private static bool IsFallbackRuntimeTfm(string tfm) =>
        string.Equals(tfm, FallbackRuntimeTfm, StringComparison.OrdinalIgnoreCase);

    private static bool IsForbiddenTfm(string tfm) =>
        Array.Exists(ForbiddenTfms, candidate => string.Equals(candidate, tfm, StringComparison.OrdinalIgnoreCase));

    private static bool HasHigherPriorityRuntimeSibling(string packageRoot, string fileName)
    {
        var preferredPath = packageRoot + "/lib/" + PreferredRuntimeTfm + "/" + fileName;
        return AssetDatabase.LoadMainAssetAtPath(preferredPath) != null;
    }

    private static bool DisableAllPlatforms(PluginImporter importer)
    {
        var changed = false;

        if (importer.GetCompatibleWithAnyPlatform())
        {
            importer.SetCompatibleWithAnyPlatform(false);
            changed = true;
        }

        if (importer.GetCompatibleWithEditor())
        {
            importer.SetCompatibleWithEditor(false);
            changed = true;
        }

        foreach (var target in EnumerateBuildTargets())
        {
            if (TryGetCompatibleWithPlatform(importer, target))
            {
                changed |= TrySetCompatibleWithPlatform(importer, target, false);
            }
        }

        if (!changed)
        {
            return false;
        }

        importer.SaveAndReimport();
        return true;
    }

    private static bool EnableRuntimePlugin(PluginImporter importer)
    {
        if (importer.GetCompatibleWithAnyPlatform())
        {
            return false;
        }

        importer.SetCompatibleWithAnyPlatform(true);
        importer.SaveAndReimport();
        return true;
    }

    private static IEnumerable<BuildTarget> EnumerateBuildTargets()
    {
        foreach (BuildTarget target in Enum.GetValues(typeof(BuildTarget)))
        {
            if (target == BuildTarget.NoTarget)
            {
                continue;
            }

            yield return target;
        }
    }

    private static bool TryGetCompatibleWithPlatform(PluginImporter importer, BuildTarget target)
    {
        try
        {
            return importer.GetCompatibleWithPlatform(target);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
        {
            return false;
        }
    }

    private static bool TrySetCompatibleWithPlatform(PluginImporter importer, BuildTarget target, bool enabled)
    {
        try
        {
            importer.SetCompatibleWithPlatform(target, enabled);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
        {
            return false;
        }
    }
}
#endif
```

Notes for implementer:

- Keep this inside the existing raw string return of `RenderNuGetPackageImportGuard()`.
- Escape any `"""` conflicts if present (use `""` doubling in raw string).
- Keep `KnownAnalyzerPackageIds` conservative. Add package IDs only when they are
  analyzer/generator packages or Roslyn compiler assemblies that must never become
  Unity runtime plugins; do not blanket-disable arbitrary `System.*` runtime
  dependencies.
- Do not ship if forbidden DLLs are enabled through Editor or explicit
  per-`BuildTarget` compatibility flags.

- [ ] **Step 2: Run Import Guard tests**

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter "FullyQualifiedName~ImportGuardTemplateTests"
```

Expected: PASS

---

### Task 3: Emit `targetFramework` in generated `packages.config`

**Files:**
- Modify: `src/Lakona.Tool/Rendering/Common/PackageReferenceRenderer.cs`
- Modify: `tests/Lakona.Tool.Tests/Rendering/PackageReferenceRendererTests.cs`

- [ ] **Step 1: Update failing test**

```csharp
[Fact]
public void RenderNuGetForUnityPackages_RendersTargetFrameworkAndManualInstallFlag()
{
    var references = new[]
    {
        new PackageReferenceSpec("Lakona.Rpc.Core", "1.2.3", PackageReferenceStyle.NuGetForUnity),
        new PackageReferenceSpec("Lakona.Rpc.Client", "2.3.4", PackageReferenceStyle.NuGetForUnity, ManuallyInstalled: true)
    };

    var xml = PackageReferenceRenderer.RenderNuGetForUnityPackages(references);

    Assert.Contains("<package id=\"Lakona.Rpc.Core\" version=\"1.2.3\" targetFramework=\"netstandard2.1\" />", xml, StringComparison.Ordinal);
    Assert.Contains("<package id=\"Lakona.Rpc.Client\" version=\"2.3.4\" targetFramework=\"netstandard2.1\" manuallyInstalled=\"true\" />", xml, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Implement renderer change**

```csharp
private static string RenderNuGetForUnityPackage(PackageReferenceSpec reference)
{
    var manuallyInstalled = reference.ManuallyInstalled ? " manuallyInstalled=\"true\"" : string.Empty;
    return $"  <package id=\"{Escape(reference.Id)}\" version=\"{Escape(reference.Version)}\" targetFramework=\"netstandard2.1\"{manuallyInstalled} />";
}
```

- [ ] **Step 3: Run tests**

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter "FullyQualifiedName~PackageReferenceRenderer"
```

Expected: PASS

---

### Task 4: Document intent in generated `NuGet.config`

**Files:**
- Modify: `src/Lakona.Tool/Rendering/Client/UnityClientRenderer.cs` (`RenderNuGetConfig`)

- [ ] **Step 1: Add XML comment block above `<config>`**

```xml
  <!--
    targetFramework in packages.config guides NuGet dependency resolution.
    Unity plugin TFM enablement is enforced by LakonaGameNuGetPackageImportGuard.
  -->
```

XML comments are valid in `NuGet.config` for Unity; place inside `<configuration>` before `<config>`.

- [ ] **Step 2: Run ClientRenderer smoke test**

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter "FullyQualifiedName~UnityClientRenderer_EmitsUnityFilesAndNoGodotFiles"
```

Expected: PASS

---

### Task 5: Add CI meta checker script

**Files:**
- Create: `scripts/game/ci/check-unity-nuget-plugin-policy.ps1`

- [ ] **Step 1: Implement script**

```powershell
param(
    [Parameter(Mandatory = $true)]
    [string]$ClientPath
)

$ErrorActionPreference = "Stop"
$forbiddenSegments = @("net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net472", "net48", "net481")
$fallbackSegment = "netstandard2.0"
$preferredSegment = "netstandard2.1"
$packagesRoot = Join-Path $ClientPath "Assets/Packages"
if (-not (Test-Path $packagesRoot)) {
    Write-Host "No Assets/Packages at $packagesRoot; skipping."
    exit 0
}

function Test-ForbiddenTfmPath {
    param([Parameter(Mandatory = $true)][string]$NormalizedPath)

    foreach ($segment in $forbiddenSegments) {
        $pattern = "/lib/{0}/" -f [regex]::Escape($segment)
        if ($NormalizedPath -match $pattern) {
            return $true
        }
    }

    return $false
}

function Test-ShadowedFallbackTfmPath {
    param([Parameter(Mandatory = $true)][string]$NormalizedPath)

    $fallbackPattern = "/lib/{0}/" -f [regex]::Escape($fallbackSegment)
    if ($NormalizedPath -notmatch $fallbackPattern) {
        return $false
    }

    $preferredPath = $NormalizedPath -replace $fallbackPattern, ("/lib/{0}/" -f $preferredSegment)
    return Test-Path -LiteralPath $preferredPath
}

function Test-EnabledPluginCompatibility {
    param([Parameter(Mandatory = $true)][string]$Text)

    $platformNames = "Any|Editor|Standalone|Win|Win64|OSXUniversal|Linux64"
    $platformBlockPattern = "(?ms)-\s*first:\s*\r?\n\s*(?::\s*)?($platformNames):?\s*\r?\n\s*second:\s*\r?\n\s*enabled:\s*1"
    $legacyEditorPattern = "editorCompatibility:\s*1"
    $buildTargetPattern = "(?ms)buildTarget:\s*($platformNames)\b.*?enabled:\s*1"

    return $Text -match $platformBlockPattern -or
        $Text -match $legacyEditorPattern -or
        $Text -match $buildTargetPattern
}

$violations = @()
Get-ChildItem -Path $packagesRoot -Recurse -Filter "*.dll.meta" | ForEach-Object {
    $normalized = $_.FullName.Replace('\', '/')
    $reason = $null
    if (Test-ForbiddenTfmPath $normalized) {
        $reason = "forbidden TFM enabled"
    }
    elseif (Test-ShadowedFallbackTfmPath $normalized) {
        $reason = "netstandard2.0 fallback enabled while netstandard2.1 sibling exists"
    }
    else {
        return
    }

    $text = Get-Content -Raw -LiteralPath $_.FullName
    if (Test-EnabledPluginCompatibility $text) {
        $violations += "$normalized ($reason)"
    }
}

if ($violations.Count -gt 0) {
    Write-Error ("Unity NuGet plugin policy violations:`n" + ($violations -join "`n"))
}

Write-Host "Unity NuGet plugin policy check passed for $ClientPath"
```

- [ ] **Step 2: Run against Agar (may fail until sample backport + local restore)**

```powershell
pwsh -NoProfile -File scripts/game/ci/check-unity-nuget-plugin-policy.ps1 -ClientPath samples/Game.Unity.Agar/Client
```

---

### Task 6: Backport guard + `packages.config` to Unity samples

**Files:**
- Create: `samples/Game.Unity.Agar/Client/Assets/Editor/LakonaGameNuGetPackageImportGuard.cs`
- Create: `samples/Game.Unity.Agar/Client/Assets/Editor/LakonaGameNuGetPackageImportGuard.cs.meta` (copy mono script meta pattern from tool template guid or generate new guid)
- Repeat for:
  - `samples/Rpc.Unity.MemoryPack.Kcp/Client`
  - `samples/Rpc.Unity.MemoryPack.Tcp/Client`
  - `samples/Rpc.Unity.Json.Websocket/Client`
- Modify: each sample `Assets/packages.config` to add `targetFramework="netstandard2.1"` on entries missing it

- [ ] **Step 1: Copy guard source**

Render once from `UnityClientCodeTemplates.RenderNuGetPackageImportGuard()` (temporary console snippet or test helper) and paste identical C# into each sample `Assets/Editor/` path. Use the same `ImportGuardGuid` as tool template (`0fdc9d512cbf4d71a198872e996940f7`) only for tool-generated projects; samples may use new guids per `.meta`.

- [ ] **Step 2: Normalize `packages.config`**

Ensure every `<package>` in all four Unity samples includes `targetFramework="netstandard2.1"`.

- [ ] **Step 3: Run PS checker on all samples**

```powershell
foreach ($p in @(
  "samples/Game.Unity.Agar/Client",
  "samples/Rpc.Unity.MemoryPack.Kcp/Client",
  "samples/Rpc.Unity.MemoryPack.Tcp/Client",
  "samples/Rpc.Unity.Json.Websocket/Client")) {
  pwsh -NoProfile -File scripts/game/ci/check-unity-nuget-plugin-policy.ps1 -ClientPath $p
}
```

Expected: PASS after Unity opens and guard runs locally; if `Assets/Packages` absent, script skips.

---

### Task 7: Update Lakona.Tool README

**Files:**
- Modify: `src/Lakona.Tool/README.md`

- [ ] **Step 1: Replace analyzer-only sentence**

From:

> generates an editor import guard that prevents NuGet analyzer DLLs from being loaded as Unity runtime plugins.

To:

> generates an editor import guard that prevents NuGet analyzer/generator DLLs and incompatible multi-TFM plugins (for example `lib/net10.0/`) from being loaded as Unity runtime plugins, while explicitly enabling `netstandard2.1` runtime DLLs under `Assets/Packages`.

---

### Task 8: Full validation

- [ ] **Step 1: Run Tool tests**

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter "FullyQualifiedName~ImportGuard|FullyQualifiedName~PackageReference|FullyQualifiedName~ClientRenderer"
```

Expected: PASS

- [ ] **Step 2: Run Lakona.Tool matrix (Godot/Console unaffected)**

```powershell
pwsh -NoProfile -File scripts/game/ci/test-lakona-tool-matrix.ps1 -SkipUnityOpen
```

Use matrix flags appropriate to your environment; at minimum verify generated Unity plan still builds at Tool test level.

- [ ] **Step 3: Unity manual / MCP gate (Agar)**

1. Open `samples/Game.Unity.Agar/Client` in Unity
2. NuGet Restore if needed
3. Confirm console has no Lakona RPC `CS7069` / AspNetCore errors
4. Confirm `Library/Bee/artifacts/*/SampleClient.Rpc.rsp` references `lib/netstandard2.1/` for Lakona packages

---

## Acceptance Checklist

- [ ] Import guard disables forbidden TFMs and enables allowed TFMs
- [ ] Import guard disables `netstandard2.0` fallback DLLs when a same-package
  `netstandard2.1` sibling exists
- [ ] Synchronous scan on editor load (not delayCall-only)
- [ ] All generated `packages.config` entries include `targetFramework="netstandard2.1"`
- [ ] Four Unity samples contain Editor guard
- [ ] PS checker script catches forbidden enabled TFMs and shadowed fallback TFMs
- [ ] `Lakona.Tool/README.md` updated
- [ ] Godot/Console tool matrix unchanged
- [ ] Agar Unity cold-open compiles without net10.0 Lakona plugin errors
