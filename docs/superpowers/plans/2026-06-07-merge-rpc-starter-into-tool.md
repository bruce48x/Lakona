# Merge Lakona.Rpc.Starter Into Lakona.Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge `src/Lakona.Rpc.Starter` into `src/Lakona.Tool`, expose only the `lakona-tool` command, and remove the standalone `Lakona.Rpc.Starter` project, package, command, tests, workflow job, and docs surface.

**Architecture:** `Lakona.Tool` becomes the only .NET tool package. The old starter generator moves into `src/Lakona.Tool/RpcStarter` as internal in-process generation code called directly by `CliApplication.NewAsync`; `ToolProcessRunner` and all `lakona-starter` installation behavior disappear. Starter tests move into `tests/Lakona.Tool.Tests/RpcStarter`, and release/workflow/docs references point to `Lakona.Tool` and `lakona-tool`.

**Tech Stack:** .NET 10, C# internal modules, MSBuild embedded resources, xUnit v3 tests, GitHub Actions, PowerShell and Bash verification scripts.

---

## Reference Documents

Read these first:

- `docs/superpowers/specs/2026-06-07-merge-rpc-starter-into-tool-design.md`
- `src/Lakona.Rpc.Starter/Lakona.Rpc.Starter.csproj`
- `src/Lakona.Tool/Lakona.Tool.csproj`
- `src/Lakona.Tool/Cli/CliApplication.cs`
- `src/Lakona.Tool/Infrastructure/ToolProcessRunner.cs`
- `tests/Lakona.Rpc.Starter.Tests/Lakona.Rpc.Starter.Tests.csproj`
- `tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj`
- `.github/workflows/godot-daily.yml`
- `scripts/game/ci/verify-lakona-tool-godot.sh`

## Important Constraints

- Final command is `lakona-tool`, not `lakona`.
- Do not keep `lakona-starter` as an alias.
- Do not keep `Lakona.Rpc.Starter` as a package, project, namespace, or current user-facing installation path.
- Historical docs may mention old names only when they explicitly describe imported history or archived roadmap content.
- Do not redesign generated project layout.
- Do not rename generated RPC public APIs.
- Keep commits small. Each task below ends with a commit step.

## Task 1: Change Tool Command Identity And Help Text

**Files:**

- Modify: `src/Lakona.Tool/Lakona.Tool.csproj`
- Modify: `src/Lakona.Tool/Cli/ToolText.cs`
- Modify: `tests/Lakona.Tool.Tests/ToolTextTests.cs`

- [ ] **Step 1: Write the failing command identity tests**

Add tests to `tests/Lakona.Tool.Tests/ToolTextTests.cs`.

Use this code near the other help/localization tests:

```csharp
[Fact]
public void PackageToolCommandName_IsLakonaTool()
{
    var repositoryRoot = FindRepositoryRoot();
    var projectPath = Path.Combine(repositoryRoot, "src", "Lakona.Tool", "Lakona.Tool.csproj");
    var xml = System.Xml.Linq.XDocument.Load(projectPath);

    var toolCommandName = xml
        .Descendants("ToolCommandName")
        .Single()
        .Value;

    Assert.Equal("lakona-tool", toolCommandName);
}

[Fact]
public void HelpText_UsesLakonaToolAndDoesNotMentionLakonaStarter()
{
    var english = ToolText.ForCulture(CultureInfo.GetCultureInfo("en-US"));
    var simplifiedChinese = ToolText.ForCulture(CultureInfo.GetCultureInfo("zh-CN"));
    var traditionalChinese = ToolText.ForCulture(CultureInfo.GetCultureInfo("zh-TW"));

    Assert.Contains("lakona-tool new", english.HelpText, StringComparison.Ordinal);
    Assert.Contains("lakona-tool new", simplifiedChinese.HelpText, StringComparison.Ordinal);
    Assert.Contains("lakona-tool new", traditionalChinese.HelpText, StringComparison.Ordinal);

    Assert.Contains("lakona-tool help", english.RunHelpForUsage, StringComparison.Ordinal);
    Assert.Contains("lakona-tool help", simplifiedChinese.RunHelpForUsage, StringComparison.Ordinal);
    Assert.Contains("lakona-tool help", traditionalChinese.RunHelpForUsage, StringComparison.Ordinal);

    Assert.DoesNotContain("lakona-starter", english.HelpText, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Lakona.Rpc.Starter", english.HelpText, StringComparison.OrdinalIgnoreCase);
}

private static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Lakona.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not find repository root.");
}
```

If `ToolTextTests.cs` already has a repository-root helper after another agent
change, reuse the existing helper instead of adding a duplicate.

- [ ] **Step 2: Run the focused tests and confirm failure**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --filter "PackageToolCommandName_IsLakonaTool|HelpText_UsesLakonaToolAndDoesNotMentionLakonaStarter"
```

Expected result: fail because `ToolCommandName` is still `lakona` and help text
still mentions `lakona-starter`.

- [ ] **Step 3: Update the project command name**

In `src/Lakona.Tool/Lakona.Tool.csproj`, change:

```xml
<ToolCommandName>lakona</ToolCommandName>
```

to:

```xml
<ToolCommandName>lakona-tool</ToolCommandName>
```

- [ ] **Step 4: Update help and usage text**

In `src/Lakona.Tool/Cli/ToolText.cs`, update `RunHelpForUsage`:

```csharp
public string RunHelpForUsage => Language switch
{
    ToolLanguage.SimplifiedChinese => "运行 `lakona-tool help` 查看用法。",
    ToolLanguage.TraditionalChinese => "執行 `lakona-tool help` 查看用法。",
    _ => "Run `lakona-tool help` for usage."
};
```

Update each `HelpText` string so the command line starts with `lakona-tool new`
and the description says it generates the RPC workspace directly. Example
English text:

```csharp
_ =>
    """
    Lakona.Tool

    Commands:
      lakona-tool new [--name MyGame] [--output .] [--client-engine unity|unity-cn|tuanjie|godot] [--transport tcp|websocket|kcp] [--serializer json|memorypack] [--persistence none|mysql|postgres] [--nugetforunity-source embedded|openupm] [--deploy-profile none|compose]
          Generate a Lakona RPC project and augment it with Lakona.Game.Server, Lakona.Game.Client, and the Lakona.Game actor runtime.
          Generates explicit cluster configuration scaffolding by default; no network profile argument is required.
    """
```

Use equivalent wording for Simplified Chinese and Traditional Chinese. Keep the
same option list.

- [ ] **Step 5: Remove starter install/update text from current tests**

In `ToolTextTests.cs`, replace assertions that call:

```csharp
text.InstallingStarter("Lakona.Rpc.Starter", ToolPackageVersions.ULinkRpcStarter)
```

with assertions about the current help or next-step text. For example:

```csharp
Assert.Contains("lakona-tool new", text.HelpText, StringComparison.Ordinal);
```

Do not keep tests that pin `ToolPackageVersions.ULinkRpcStarter`.

- [ ] **Step 6: Run focused tests and confirm pass**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --filter "PackageToolCommandName_IsLakonaTool|HelpText_UsesLakonaToolAndDoesNotMentionLakonaStarter|SimplifiedChineseTextLocalizesHelpAndNextSteps"
```

Expected result: pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/Lakona.Tool/Lakona.Tool.csproj src/Lakona.Tool/Cli/ToolText.cs tests/Lakona.Tool.Tests/ToolTextTests.cs
git commit -m "Switch Lakona tool command to lakona-tool"
```

## Task 2: Move Starter Source And Resources Into Lakona.Tool

**Files:**

- Create: `src/Lakona.Tool/RpcStarter/**`
- Modify: `src/Lakona.Tool/Lakona.Tool.csproj`
- Do not delete yet: `src/Lakona.Rpc.Starter/**`

- [ ] **Step 1: Create target directories**

Create these directories:

```powershell
New-Item -ItemType Directory -Force -Path `
  src\Lakona.Tool\RpcStarter\Cli, `
  src\Lakona.Tool\RpcStarter\Generation\ClientTemplates, `
  src\Lakona.Tool\RpcStarter\Infrastructure, `
  src\Lakona.Tool\RpcStarter\Templates\Godot, `
  src\Lakona.Tool\RpcStarter\Templates\Unity, `
  src\Lakona.Tool\RpcStarter\TemplateAssets
```

- [ ] **Step 2: Move non-executable starter files**

Move these files and folders from `src/Lakona.Rpc.Starter` to
`src/Lakona.Tool/RpcStarter`:

```txt
src/Lakona.Rpc.Starter/ReleaseVersions.json
src/Lakona.Rpc.Starter/Generation/**
src/Lakona.Rpc.Starter/Infrastructure/**
src/Lakona.Rpc.Starter/Templates/**
src/Lakona.Rpc.Starter/TemplateAssets/**
src/Lakona.Rpc.Starter/Cli/StarterText.cs
```

Do not move:

```txt
src/Lakona.Rpc.Starter/Cli/Program.cs
src/Lakona.Rpc.Starter/Cli/StarterCli.cs
src/Lakona.Rpc.Starter/Lakona.Rpc.Starter.csproj
src/Lakona.Rpc.Starter/README.md
```

`StarterCli.cs` is command-line parsing for the old executable. It should not
survive as the new top-level parser.

- [ ] **Step 3: Rename namespaces**

In all moved `.cs` files under `src/Lakona.Tool/RpcStarter`, replace:

```csharp
namespace Lakona.Rpc.Starter;
```

with:

```csharp
namespace Lakona.Tool.RpcStarter;
```

Also update any `using Lakona.Rpc.Starter;` in moved files to:

```csharp
using Lakona.Tool.RpcStarter;
```

- [ ] **Step 4: Update embedded resource names in the tool csproj**

Add this item group to `src/Lakona.Tool/Lakona.Tool.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="RpcStarter\TemplateAssets\NuGetForUnity.4.5.0.zip" LogicalName="Lakona.Tool.RpcStarter.TemplateAssets.NuGetForUnity.4.5.0.zip" />
  <EmbeddedResource Include="RpcStarter\ReleaseVersions.json" LogicalName="Lakona.Tool.RpcStarter.ReleaseVersions.json" />
  <EmbeddedResource Include="RpcStarter\Templates\Godot\project.godot.template" LogicalName="Lakona.Tool.RpcStarter.Templates.Godot.project.godot.template" />
  <EmbeddedResource Include="RpcStarter\Templates\Godot\Main.tscn.template" LogicalName="Lakona.Tool.RpcStarter.Templates.Godot.Main.tscn.template" />
  <EmbeddedResource Include="RpcStarter\Templates\Unity\EditorBuildSettings.asset.template" LogicalName="Lakona.Tool.RpcStarter.Templates.Unity.EditorBuildSettings.asset.template" />
  <EmbeddedResource Include="RpcStarter\Templates\Unity\AutoOpenConnectionScene.template" LogicalName="Lakona.Tool.RpcStarter.Templates.Unity.AutoOpenConnectionScene.template" />
</ItemGroup>
```

- [ ] **Step 5: Update resource lookup code**

In `src/Lakona.Tool/RpcStarter/Infrastructure/StarterTemplateRenderer.cs`,
change the resource prefix to:

```csharp
private const string ResourcePrefix = "Lakona.Tool.RpcStarter.Templates.";
```

In `src/Lakona.Tool/RpcStarter/Infrastructure/NuGetVersionResolver.cs`, change:

```csharp
const string resourceName = "Lakona.Rpc.Starter.ReleaseVersions.json";
```

to:

```csharp
const string resourceName = "Lakona.Tool.RpcStarter.ReleaseVersions.json";
```

In `src/Lakona.Tool/RpcStarter/Infrastructure/StarterFileWriter.cs`, change
resource logical names used for `NuGetForUnity.4.5.0.zip` to the
`Lakona.Tool.RpcStarter.TemplateAssets...` prefix.

- [ ] **Step 6: Run build and capture expected failures**

Run:

```powershell
dotnet build src\Lakona.Tool\Lakona.Tool.csproj
```

Expected result: this may fail because the old starter code has no in-process
entry point and some old CLI types may still be referenced. Do not fix by
referencing `src/Lakona.Rpc.Starter`; fix by completing Task 3.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/Lakona.Tool/RpcStarter src/Lakona.Tool/Lakona.Tool.csproj
git commit -m "Move starter generator into Lakona.Tool"
```

## Task 3: Replace External Starter Process With In-Process Generation

**Files:**

- Create: `src/Lakona.Tool/RpcStarter/RpcStarterGenerator.cs`
- Modify: `src/Lakona.Tool/Cli/CliApplication.cs`
- Modify: `src/Lakona.Tool/Cli/Program.cs`
- Modify: `src/Lakona.Tool/Infrastructure/ToolProcessRunner.cs`
- Modify: `src/Lakona.Tool/Scaffolding/ToolModels.cs`
- Modify: `src/Lakona.Tool/Cli/ToolText.cs`
- Test: `tests/Lakona.Tool.Tests/ToolTextTests.cs`

- [ ] **Step 1: Write failing test for no external starter dependency**

Add this test to `tests/Lakona.Tool.Tests/ToolTextTests.cs`:

```csharp
[Fact]
public void ToolText_DoesNotExposeStarterInstallMessages()
{
    var text = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "Lakona.Tool",
        "Cli",
        "ToolText.cs"));

    Assert.DoesNotContain("InstallingStarter", text, StringComparison.Ordinal);
    Assert.DoesNotContain("UnableToInstallStarter", text, StringComparison.Ordinal);
    Assert.DoesNotContain("StarterVersionMismatch", text, StringComparison.Ordinal);
    Assert.DoesNotContain("Lakona.Rpc.Starter", text, StringComparison.Ordinal);
    Assert.DoesNotContain("lakona-starter", text, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the test and confirm failure**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --filter ToolText_DoesNotExposeStarterInstallMessages
```

Expected result: fail because current `ToolText.cs` still contains starter
install/update messages.

- [ ] **Step 3: Add an in-process starter API**

Create `src/Lakona.Tool/RpcStarter/RpcStarterGenerator.cs`:

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
    public void Generate(RpcStarterNewOptions options)
    {
        var rootPath = Path.GetFullPath(Path.Combine(options.OutputDirectory, options.ProjectName));
        var versions = NuGetVersionResolver.ResolveVersions(options.Transport, options.Serializer);
        var generator = new StarterTemplateGenerator(ProcessRunner.RunGit);

        StarterOutputManager.GenerateIntoTargetDirectory(
            rootPath,
            stagingRootPath => generator.GenerateTemplate(
                stagingRootPath,
                options.ProjectName,
                options.ClientEngine,
                options.Transport,
                options.Serializer,
                options.NuGetForUnitySource,
                versions));
    }
}
```

If `StarterTemplateGenerator` still requires a `runDotNet` constructor argument
after Task 2, use the constructor overload that accepts only `runGit`. The
current implementation has that overload.

- [ ] **Step 4: Add conversion helpers to CliApplication**

In `src/Lakona.Tool/Cli/CliApplication.cs`, add:

```csharp
using Lakona.Tool.RpcStarter;
```

Change the constructor signature from:

```csharp
internal sealed class CliApplication(
    ToolProcessRunner processRunner,
    ProjectScaffolder projectScaffolder,
    ToolConfigStore configStore,
    ToolText? text = null)
```

to:

```csharp
internal sealed class CliApplication(
    RpcStarterGenerator rpcStarterGenerator,
    ProjectScaffolder projectScaffolder,
    ToolConfigStore configStore,
    ToolText? text = null)
```

Add conversion methods inside `CliApplication`:

```csharp
private static RpcStarterNewOptions ToRpcStarterOptions(
    string projectName,
    string outputDirectory,
    NewCommandOptions options)
{
    return new RpcStarterNewOptions(
        projectName,
        outputDirectory,
        ParseClientEngine(options.ClientEngine),
        ParseTransport(options.Transport),
        ParseSerializer(options.Serializer),
        ParseNuGetForUnitySource(options.NuGetForUnitySource));
}

private static ClientEngineKind ParseClientEngine(string value) => value switch
{
    "unity" => ClientEngineKind.Unity,
    "unity-cn" => ClientEngineKind.UnityCn,
    "tuanjie" => ClientEngineKind.Tuanjie,
    "godot" => ClientEngineKind.Godot,
    _ => throw new InvalidOperationException($"Unsupported --client-engine value after validation: {value}")
};

private static TransportKind ParseTransport(string value) => value switch
{
    "tcp" => TransportKind.Tcp,
    "websocket" => TransportKind.WebSocket,
    "kcp" => TransportKind.Kcp,
    _ => throw new InvalidOperationException($"Unsupported --transport value after validation: {value}")
};

private static SerializerKind ParseSerializer(string value) => value switch
{
    "json" => SerializerKind.Json,
    "memorypack" => SerializerKind.MemoryPack,
    _ => throw new InvalidOperationException($"Unsupported --serializer value after validation: {value}")
};

private static NuGetForUnitySourceKind ParseNuGetForUnitySource(string value) => value switch
{
    "embedded" => NuGetForUnitySourceKind.Embedded,
    "openupm" => NuGetForUnitySourceKind.OpenUpm,
    _ => throw new InvalidOperationException($"Unsupported --nugetforunity-source value after validation: {value}")
};
```

- [ ] **Step 5: Replace the external starter call**

In `CliApplication.NewAsync`, replace:

```csharp
var starterExitCode = await processRunner.RunStarterNewAsync(projectName, outputDirectory, options).ConfigureAwait(false);
if (starterExitCode != 0)
{
    return starterExitCode;
}
```

with:

```csharp
rpcStarterGenerator.Generate(ToRpcStarterOptions(projectName, outputDirectory, options));
```

Keep the existing `projectRoot` existence check, augmentation, config save, and
next-step output.

- [ ] **Step 6: Update Program.cs**

In `src/Lakona.Tool/Cli/Program.cs`, add:

```csharp
using Lakona.Tool.RpcStarter;
```

Change:

```csharp
new ToolProcessRunner(text),
```

to:

```csharp
new RpcStarterGenerator(),
```

- [ ] **Step 7: Delete ToolProcessRunner**

Delete `src/Lakona.Tool/Infrastructure/ToolProcessRunner.cs`.

If the project still compiles references to `ProcessRunResult`, remove those
references. The only process runner left should be the moved starter
`RpcStarter/Infrastructure/ProcessRunner.cs`, which is used for generated
project setup.

- [ ] **Step 8: Remove starter package version constant**

In `src/Lakona.Tool/Scaffolding/ToolModels.cs`, delete:

```csharp
public const string ULinkRpcStarter = "0.4.2";
```

Do not replace it with another starter package version. There is no starter
package after the merge.

- [ ] **Step 9: Remove starter install/update text**

In `src/Lakona.Tool/Cli/ToolText.cs`, delete these members:

```csharp
UnableToLocateStarter
InstallingStarter
UnableToInstallStarter
InstallStarterBeforeNew
StarterVersionMismatch
StarterUpdated
UnableToUpdateStarter
```

Then run:

```powershell
rg -n "InstallingStarter|UnableToInstallStarter|StarterVersionMismatch|Lakona\\.Rpc\\.Starter|lakona-starter|ULinkRpcStarter" src\Lakona.Tool tests\Lakona.Tool.Tests -g '!**/bin/**' -g '!**/obj/**'
```

Expected result: no matches, except matches under `src\Lakona.Tool\RpcStarter`
that still contain internal type names such as `StarterTemplateGenerator`.

- [ ] **Step 10: Run focused tests and build**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --filter ToolText_DoesNotExposeStarterInstallMessages
dotnet build src\Lakona.Tool\Lakona.Tool.csproj
```

Expected result: both pass.

- [ ] **Step 11: Commit**

Run:

```powershell
git add src/Lakona.Tool tests/Lakona.Tool.Tests
git commit -m "Generate starter projects in-process from Lakona.Tool"
```

## Task 4: Move Starter Tests Into Lakona.Tool.Tests

**Files:**

- Create: `tests/Lakona.Tool.Tests/RpcStarter/**`
- Modify: `tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj`
- Do not delete yet: `tests/Lakona.Rpc.Starter.Tests/**`

- [ ] **Step 1: Move starter test files**

Move these files:

```txt
tests/Lakona.Rpc.Starter.Tests/ProcessRunnerTests.cs
tests/Lakona.Rpc.Starter.Tests/StarterDependencyPlannerTests.cs
tests/Lakona.Rpc.Starter.Tests/StarterLocalizationTests.cs
tests/Lakona.Rpc.Starter.Tests/StarterTemplateGeneratorTests.cs
tests/Lakona.Rpc.Starter.Tests/UnitySamplePackageTests.cs
tests/Lakona.Rpc.Starter.Tests/Golden/**
```

to:

```txt
tests/Lakona.Tool.Tests/RpcStarter/ProcessRunnerTests.cs
tests/Lakona.Tool.Tests/RpcStarter/StarterDependencyPlannerTests.cs
tests/Lakona.Tool.Tests/RpcStarter/StarterLocalizationTests.cs
tests/Lakona.Tool.Tests/RpcStarter/StarterTemplateGeneratorTests.cs
tests/Lakona.Tool.Tests/RpcStarter/UnitySamplePackageTests.cs
tests/Lakona.Tool.Tests/RpcStarter/Golden/**
```

- [ ] **Step 2: Update namespaces and using directives**

In moved test files, replace:

```csharp
using Lakona.Rpc.Starter;
namespace Lakona.Rpc.Starter.Tests;
```

with:

```csharp
using Lakona.Tool.RpcStarter;
namespace Lakona.Tool.Tests.RpcStarter;
```

If a moved test also needs existing tool types such as `CliParser`, add:

```csharp
using Lakona.Tool.Tests;
```

only if the compiler requires it. Most existing tool code is in the global
namespace, so this should not be necessary.

- [ ] **Step 3: Update paths inside moved tests**

In moved tests, replace repository paths:

```csharp
Path.Combine(repositoryRoot, "src", "Lakona.Rpc.Starter", "README.md")
```

with:

```csharp
Path.Combine(repositoryRoot, "src", "Lakona.Tool", "README.md")
```

Replace test temp root strings that include `Lakona.Rpc.Starter.Tests` with
`Lakona.Tool.Tests.RpcStarter`.

Replace assertions that require `lakona-starter new` in current help with
`lakona-tool new`. Assertions about archived codegen removal may remain only if
they read archived docs.

- [ ] **Step 4: Update test project file**

In `tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj`, add:

```xml
<ItemGroup>
  <None Include="RpcStarter\Golden\**\*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Do not add a project reference to `src/Lakona.Rpc.Starter`.

Do not copy the old xUnit v2 package references from
`tests/Lakona.Rpc.Starter.Tests`. Keep the current xUnit v3 references already
in `tests/Lakona.Tool.Tests`.

- [ ] **Step 5: Run the moved starter tests and fix compile errors**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --filter "FullyQualifiedName~RpcStarter"
```

Expected first result: compile errors are likely. Fix them by updating
namespaces, resource names, and expectations to the new `Lakona.Tool.RpcStarter`
module. Do not reintroduce `Lakona.Rpc.Starter`.

Common fixes:

```csharp
Assert.Contains("lakona-tool new", output, StringComparison.Ordinal);
Assert.DoesNotContain("lakona-starter", output, StringComparison.OrdinalIgnoreCase);
```

For version tests, keep this behavior:

```csharp
Assert.Equal(
    ReadProjectVersion(repositoryRoot, "src", "Lakona.Rpc.Core", "Lakona.Rpc.Core.csproj"),
    StarterReleaseVersions.Core);
```

- [ ] **Step 6: Run all tool tests**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj
```

Expected result: pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add tests/Lakona.Tool.Tests src/Lakona.Tool
git commit -m "Move starter tests under Lakona.Tool.Tests"
```

## Task 5: Delete Standalone Starter Project And Test Project

**Files:**

- Delete: `src/Lakona.Rpc.Starter/**`
- Delete: `tests/Lakona.Rpc.Starter.Tests/**`
- Modify: `Lakona.slnx`
- Modify: `tests/Tests.slnx`

- [ ] **Step 1: Remove old project and tests from solution**

In `Lakona.slnx`, delete these lines:

```xml
<Project Path="src/Lakona.Rpc.Starter/Lakona.Rpc.Starter.csproj" />
<Project Path="tests/Lakona.Rpc.Starter.Tests/Lakona.Rpc.Starter.Tests.csproj" />
```

Keep these lines:

```xml
<Project Path="src/Lakona.Tool/Lakona.Tool.csproj" />
<Project Path="tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj" />
```

- [ ] **Step 2: Fix tests solution**

Open `tests/Tests.slnx`.

If it contains:

```xml
<Project Path="Lakona.Game.Tool.Tests/Lakona.Game.Tool.Tests.csproj" />
```

replace it with:

```xml
<Project Path="Lakona.Tool.Tests/Lakona.Tool.Tests.csproj" />
```

If it is missing other existing test projects that normal CI runs by scanning
`tests/**/*.csproj`, add them. At minimum it must include
`Lakona.Tool.Tests/Lakona.Tool.Tests.csproj`.

- [ ] **Step 3: Delete old directories**

Delete:

```txt
src/Lakona.Rpc.Starter
tests/Lakona.Rpc.Starter.Tests
```

Use PowerShell after checking paths:

```powershell
$starter = Resolve-Path src\Lakona.Rpc.Starter
$starterTests = Resolve-Path tests\Lakona.Rpc.Starter.Tests
Remove-Item -LiteralPath $starter.Path -Recurse -Force
Remove-Item -LiteralPath $starterTests.Path -Recurse -Force
```

- [ ] **Step 4: Scan for removed project references**

Run:

```powershell
rg -n "src/Lakona\\.Rpc\\.Starter|tests/Lakona\\.Rpc\\.Starter|Lakona\\.Rpc\\.Starter\\.csproj|Lakona\\.Rpc\\.Starter\\.Tests\\.csproj" . -g '!**/bin/**' -g '!**/obj/**'
```

Expected result: no current project references. Historical docs may mention
`Lakona.Rpc.Starter`, but they must not mention deleted project paths as current
instructions.

- [ ] **Step 5: Build solution**

Run:

```powershell
dotnet build Lakona.slnx --no-restore
```

Expected result: pass.

- [ ] **Step 6: Commit**

Run:

```powershell
git add Lakona.slnx tests/Tests.slnx src/Lakona.Rpc.Starter tests/Lakona.Rpc.Starter.Tests
git commit -m "Remove standalone Lakona.Rpc.Starter project"
```

## Task 6: Update Scripts And Godot Daily Workflow

**Files:**

- Modify: `.github/workflows/godot-daily.yml`
- Modify: `scripts/game/ci/verify-lakona-tool-godot.sh`
- Delete or modify: `scripts/rpc/ci/verify-starter-godot.sh`
- Modify if needed: `scripts/rpc/check-generated-code.ps1`
- Modify if needed: `scripts/rpc/sample.ps1`

- [ ] **Step 1: Write a text check for scripts**

Run:

```powershell
rg -n "Lakona\\.Rpc\\.Starter|lakona-starter|verify-starter-godot|STARTER_" .github scripts -g '!**/bin/**' -g '!**/obj/**'
```

Expected result before changes: matches in `godot-daily.yml`,
`verify-lakona-tool-godot.sh`, and `verify-starter-godot.sh`.

- [ ] **Step 2: Remove starter job from Godot daily**

In `.github/workflows/godot-daily.yml`, delete the entire
`verify-rpc-godot-starter` job.

Keep `verify-lakona-godot-tool` with its matrix. Rename its display name to
mention `lakona-tool` if desired:

```yaml
name: Verify lakona-tool Godot project (${{ matrix.transport }} + ${{ matrix.serializer }})
```

- [ ] **Step 3: Update verify-lakona-tool-godot.sh package packing**

In `scripts/game/ci/verify-lakona-tool-godot.sh`, remove this line if present:

```bash
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Starter/Lakona.Rpc.Starter.csproj"
```

Keep packing `src/Lakona.Tool/Lakona.Tool.csproj` and all runtime packages
required by generated projects.

- [ ] **Step 4: Update verify-lakona-tool-godot.sh tool invocation**

Remove installation of `Lakona.Rpc.Starter`, including lines like:

```bash
echo "Installing lakona-starter into $TOOLS_DIR"
dotnet tool install Lakona.Rpc.Starter --version 0.4.2 --add-source "$LOCAL_FEED" --tool-path "$TOOLS_DIR"
```

Generate projects by running `Lakona.Tool` directly:

```bash
dotnet run --project "$ROOT_DIR/src/Lakona.Tool/Lakona.Tool.csproj" -- \
  new \
  --name "$PROJECT_NAME" \
  --output "$WORK_DIR" \
  --client-engine godot \
  --transport "$TRANSPORT" \
  --serializer "$SERIALIZER"
```

If the script verifies packed tool installation, install only `Lakona.Tool`:

```bash
dotnet tool install Lakona.Tool --version "$LAKONA_TOOL_VERSION" --add-source "$LOCAL_FEED" --tool-path "$TOOLS_DIR"
"$TOOLS_DIR/lakona-tool" new \
  --name "$PROJECT_NAME" \
  --output "$WORK_DIR" \
  --client-engine godot \
  --transport "$TRANSPORT" \
  --serializer "$SERIALIZER"
```

Use one path consistently. The simpler first implementation is `dotnet run`
because it avoids tool-path differences on Windows and Linux.

- [ ] **Step 5: Delete or rename verify-starter-godot.sh**

If no workflow calls `scripts/rpc/ci/verify-starter-godot.sh`, delete it.

Run:

```powershell
rg -n "verify-starter-godot" . scripts docs README.md .github -g '!**/bin/**' -g '!**/obj/**'
```

If there are no current references after deleting the workflow job, delete the
script:

```powershell
$script = Resolve-Path scripts\rpc\ci\verify-starter-godot.sh
Remove-Item -LiteralPath $script.Path -Force
```

- [ ] **Step 6: Update generated-code scripts only if needed**

Run:

```powershell
rg -n "Lakona\\.Rpc\\.Starter|lakona-starter|src/Lakona\\.Rpc\\.Starter|STARTER_" scripts\rpc\check-generated-code.ps1 scripts\rpc\sample.ps1
```

If there are matches, replace current instructions with `lakona-tool` and
`src/Lakona.Tool`.

- [ ] **Step 7: Syntax-check bash scripts**

Run:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' -n scripts/game/ci/verify-lakona-tool-godot.sh
```

If `verify-starter-godot.sh` still exists, also run:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' -n scripts/rpc/ci/verify-starter-godot.sh
```

Expected result: no output and exit code `0`.

- [ ] **Step 8: Scan workflow and scripts**

Run:

```powershell
rg -n "Lakona\\.Rpc\\.Starter|lakona-starter|verify-starter-godot|STARTER_" .github scripts -g '!**/bin/**' -g '!**/obj/**'
```

Expected result: no matches.

- [ ] **Step 9: Commit**

Run:

```powershell
git add .github/workflows/godot-daily.yml scripts
git commit -m "Verify Godot projects through lakona-tool"
```

## Task 7: Update Documentation And Package README Files

**Files:**

- Modify: `README.md`
- Modify: `CONTRIBUTING.md`
- Modify: `src/Lakona.Tool/README.md`
- Modify: `docs/rpc/overview.md`
- Modify: `docs/rpc/README.md`
- Modify: `docs/rpc/starter/dependency-planning.md`
- Modify: `docs/rpc/starter/source-generation.md`
- Modify: `docs/rpc/starter/unity-shared-source-link.md`
- Modify: `docs/lakona-monorepo.md`
- Leave historical references: `CHANGELOG.md`, `docs/maintenance/imported-contributing-notes.md`, `docs/rpc/archive/**`

- [ ] **Step 1: Scan current docs**

Run:

```powershell
rg -n "Lakona\\.Rpc\\.Starter|lakona-starter|lakona new|`lakona`|ToolCommandName>lakona" README.md CONTRIBUTING.md docs src/Lakona.Tool/README.md -g '*.md' -g '*.csproj'
```

Expected result before edits: multiple current-doc references.

- [ ] **Step 2: Update root README**

In `README.md`, change the package entry:

```md
- `Lakona.Tool` for `lakona new`
```

to:

```md
- `Lakona.Tool` for `lakona-tool new`
```

Update any install example to:

```bash
dotnet tool install -g Lakona.Tool
lakona-tool new --name MyGame --client-engine unity --transport websocket --serializer json
```

- [ ] **Step 3: Rewrite src/Lakona.Tool/README.md**

Make `src/Lakona.Tool/README.md` the canonical tool package README.

It must include:

```md
# Lakona.Tool

`Lakona.Tool` is the single command-line project tool for Lakona. It generates
the base Lakona.Rpc shared/server/client workspace and then adds Lakona.Game
server, client, actor, hotfix, cluster, and configuration scaffolding.

## Install

```bash
dotnet tool install -g Lakona.Tool
```

## Create A Project

```bash
lakona-tool new --name MyGame --client-engine unity --transport websocket --serializer json
```
```

The README must not say it installs or delegates to `Lakona.Rpc.Starter`.

- [ ] **Step 4: Update RPC overview**

In `docs/rpc/overview.md`, replace:

```bash
dotnet tool install -g Lakona.Rpc.Starter
lakona-starter new --name MyGame --client-engine unity --transport websocket --serializer json
```

with:

```bash
dotnet tool install -g Lakona.Tool
lakona-tool new --name MyGame --client-engine unity --transport websocket --serializer json
```

Also replace the Godot example with `lakona-tool new`.

Change link text from `Getting started with Lakona.Rpc.Starter` to
`Getting started with Lakona.Tool`.

- [ ] **Step 5: Update RPC starter design docs**

The directory name `docs/rpc/starter` may stay for now because it describes the
starter-template design area, but current package references must change.

In `docs/rpc/starter/*.md`, replace current product statements:

```md
`Lakona.Rpc.Starter`
```

with:

```md
`Lakona.Tool`'s RPC starter module
```

When command examples appear, use:

```bash
lakona-tool new ...
```

- [ ] **Step 6: Update monorepo and contributing docs**

In `docs/lakona-monorepo.md` and `CONTRIBUTING.md`, remove instructions that
treat `Lakona.Rpc.Starter` as a separate package or project.

Add a sentence to the relevant tool/package section:

```md
`Lakona.Tool` owns the RPC starter templates and the game-framework
augmentation flow. Do not add a second CLI package for project generation.
```

- [ ] **Step 7: Run docs consistency check**

Run:

```powershell
pwsh -NoProfile -File scripts\rpc\check-docs-consistency.ps1
```

Expected result: pass.

- [ ] **Step 8: Scan current docs again**

Run:

```powershell
rg -n "Lakona\\.Rpc\\.Starter|lakona-starter" README.md CONTRIBUTING.md docs src/Lakona.Tool/README.md -g '*.md'
```

Expected result: only explicitly historical files may match:

```txt
CHANGELOG.md
docs/maintenance/imported-contributing-notes.md
docs/rpc/archive/**
```

If a current file matches, update it.

- [ ] **Step 9: Commit**

Run:

```powershell
git add README.md CONTRIBUTING.md src/Lakona.Tool/README.md docs
git commit -m "Document lakona-tool as the only project generator"
```

## Task 8: Update Packaging And Publish Verification

**Files:**

- Modify if needed: `.github/workflows/publish-nuget.yml`
- Modify if needed: `src/Lakona.Tool/Lakona.Tool.csproj`

- [ ] **Step 1: Verify publish workflow packs only existing src projects**

Read `.github/workflows/publish-nuget.yml`. It currently packs:

```bash
find src -maxdepth 2 -name "*.csproj" -print | sort
```

This is acceptable after `src/Lakona.Rpc.Starter` is deleted. Do not add a
hard-coded exclusion unless the old directory still exists, which it should not.

- [ ] **Step 2: Verify local pack project list**

Run:

```powershell
Get-ChildItem -Path src -Depth 1 -Filter *.csproj | Select-Object FullName
```

Expected result:

- includes `src\Lakona.Tool\Lakona.Tool.csproj`
- does not include `src\Lakona.Rpc.Starter\Lakona.Rpc.Starter.csproj`

- [ ] **Step 3: Pack Lakona.Tool**

Run:

```powershell
dotnet pack src\Lakona.Tool\Lakona.Tool.csproj -c Release -o artifacts\plan-check
```

Expected result: creates a `Lakona.Tool.*.nupkg` package. No
`Lakona.Rpc.Starter.*.nupkg` is created by this command.

- [ ] **Step 4: Inspect generated package command metadata**

Run:

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = Get-ChildItem artifacts\plan-check\Lakona.Tool.*.nupkg | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$extract = Join-Path $env:TEMP ("lakona-tool-package-" + [guid]::NewGuid().ToString("N"))
[System.IO.Compression.ZipFile]::ExtractToDirectory($package.FullName, $extract)
Get-ChildItem -Path $extract -Recurse | Select-Object FullName
```

Expected result: package files exist. The `.nuspec` should identify
`Lakona.Tool`. If there is a tool settings file or shim metadata containing the
command name, it must use `lakona-tool`.

- [ ] **Step 5: Commit if packaging files changed**

If `.github/workflows/publish-nuget.yml` or `src/Lakona.Tool/Lakona.Tool.csproj`
changed in this task, commit:

```powershell
git add .github/workflows/publish-nuget.yml src/Lakona.Tool/Lakona.Tool.csproj
git commit -m "Publish only Lakona.Tool project generator"
```

If no files changed, do not create an empty commit.

## Task 9: Final Verification And Cleanup

**Files:**

- All files touched by earlier tasks

- [ ] **Step 1: Run final text scans**

Run:

```powershell
rg -n "Lakona\\.Rpc\\.Starter|lakona-starter|ULinkRpcStarter|src/Lakona\\.Rpc\\.Starter|tests/Lakona\\.Rpc\\.Starter" . -g '!**/bin/**' -g '!**/obj/**' -g '!CHANGELOG.md' -g '!docs/maintenance/imported-contributing-notes.md' -g '!docs/rpc/archive/**'
```

Expected result: no matches.

Run:

```powershell
rg -n "ToolCommandName>lakona<|Run `lakona help|lakona new" . -g '!**/bin/**' -g '!**/obj/**'
```

Expected result: no current command examples using `lakona` as the command.
Historical docs may need explicit exclusion if they are intentionally archival.

- [ ] **Step 2: Run docs check**

Run:

```powershell
pwsh -NoProfile -File scripts\rpc\check-docs-consistency.ps1
```

Expected result:

```txt
Documentation consistency check passed.
```

- [ ] **Step 3: Build the full solution**

Run:

```powershell
dotnet build Lakona.slnx --no-restore -m:1 /nr:false /p:UseSharedCompilation=false
```

Expected result: build succeeds.

- [ ] **Step 4: Test the merged tool project**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-build
```

Expected result: all `Lakona.Tool.Tests` pass, including moved RPC starter
tests.

- [ ] **Step 5: Test former starter sample generation through lakona-tool**

Run:

```powershell
$out = Join-Path $env:TEMP ("lakona-tool-rpc-only-" + [guid]::NewGuid().ToString("N"))
dotnet run --project src\Lakona.Tool\Lakona.Tool.csproj -- new --name RpcOnlySmoke --output $out --client-engine console --transport tcp --serializer json
Test-Path (Join-Path $out "RpcOnlySmoke\Server\Server\Server.csproj")
Test-Path (Join-Path $out "RpcOnlySmoke\Shared\Shared.csproj")
Test-Path (Join-Path $out "RpcOnlySmoke\Client\Client.csproj")
Test-Path (Join-Path $out "RpcOnlySmoke\lakona.tool.json")
```

Expected result: the command exits `0` and all four `Test-Path` calls print
`True`.

- [ ] **Step 6: Run all tests if time allows**

Run:

```powershell
$projects = Get-ChildItem -Path tests -Recurse -Filter '*.csproj' | Sort-Object FullName
foreach ($project in $projects) {
  dotnet test $project.FullName --no-build
  if ($LASTEXITCODE -ne 0) { throw "Tests failed for $($project.FullName)" }
}
```

Expected result: all test projects pass.

- [ ] **Step 7: Check git status**

Run:

```powershell
git status --short
```

Expected result: only intentional files are modified. There must be no
generated smoke-test output, package extraction output, `bin`, `obj`, or
temporary Godot/Unity folders staged.

- [ ] **Step 8: Final commit**

If final cleanup changed files after the previous commits, run:

```powershell
git add .
git commit -m "Finish lakona-tool starter consolidation"
```

If there are no remaining changes, do not create an empty commit.

## Completion Checklist

Before handing off, verify every item:

- [ ] `src/Lakona.Rpc.Starter` is deleted.
- [ ] `tests/Lakona.Rpc.Starter.Tests` is deleted.
- [ ] `src/Lakona.Tool/Lakona.Tool.csproj` uses `ToolCommandName` `lakona-tool`.
- [ ] `Lakona.slnx` does not reference `Lakona.Rpc.Starter`.
- [ ] `tests/Tests.slnx` references `Lakona.Tool.Tests`, not `Lakona.Game.Tool.Tests`.
- [ ] `.github/workflows/godot-daily.yml` has one integrated `lakona-tool` Godot job.
- [ ] `scripts/game/ci/verify-lakona-tool-godot.sh` does not install `Lakona.Rpc.Starter`.
- [ ] Current docs show `dotnet tool install -g Lakona.Tool` and `lakona-tool new`.
- [ ] Final text scan has no current `lakona-starter` or `Lakona.Rpc.Starter` references.
- [ ] `dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj` passes.
- [ ] `dotnet build Lakona.slnx --no-restore` passes.
