namespace Lakona.Tool.RpcStarter;

internal static class StarterGodotTemplate
{
    public static void Generate(StarterTemplateContext context)
    {
        EnsureClientDirectories(context.Paths.ClientPath);
        var sdk = ResolveGodotSdk();

        StarterFileWriter.Write(Path.Combine(context.Paths.ClientPath, "project.godot"), BuildProjectFile(context));
        StarterFileWriter.Write(Path.Combine(context.Paths.ClientPath, "Client.csproj"), BuildClientProject(context, sdk));
        StarterFileWriter.Write(Path.Combine(context.Paths.ClientPath, "README.md"), BuildReadme(context, sdk));
        if (sdk is not null)
        {
            StarterFileWriter.Write(Path.Combine(context.Paths.ClientPath, "NuGet.config"), BuildNuGetConfig(sdk));
        }
    }

    private static void EnsureClientDirectories(string clientPath)
    {
        Directory.CreateDirectory(Path.Combine(clientPath, "Scripts"));
    }

    private static string BuildProjectFile(StarterTemplateContext context) =>
        StarterTemplateRenderer.Render("Godot/project.godot.template", new Dictionary<string, string>
        {
            ["ProjectName"] = context.ProjectName
        });

    private static string BuildClientProject(StarterTemplateContext context, GodotSdkReference? sdk)
    {
        var packageReferences = PackageReferenceText.RenderSdkPackageReferences(StarterDependencyPlanner.Create(context, StarterProjectRole.GodotClient));

        var sdkVersion = sdk?.Version ?? "4.6.1";

        return $$"""
<Project Sdk="Godot.NET.Sdk/{{sdkVersion}}">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Client</RootNamespace>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <NuGetAudit>false</NuGetAudit>
    <LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>
    <LakonaRpcGeneratedNamespace>Rpc.Generated</LakonaRpcGeneratedNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Shared\Shared.csproj" />
{{packageReferences}}
  </ItemGroup>

</Project>
""";
    }

    private static string BuildReadme(StarterTemplateContext context, GodotSdkReference? sdk)
    {
        var sdkNote = sdk is null
            ? """
5. If build restore fails, add your Godot Mono `GodotSharp/Tools/nupkgs` folder as a NuGet source and update `Client.csproj` to the matching `Godot.NET.Sdk` version.
"""
            : $$"""
5. `NuGet.config` points to the detected local Godot Mono SDK packages:
   - `{{sdk.PackageSourcePath}}`
""";

        return $$"""
# Godot Client Starter (Godot 4.6)

1. Open this folder with Godot 4.6.
2. Let Godot restore the C# solution, or run `dotnet restore Client.csproj`.
3. Build the project once so generated assemblies load.
4. Add your own scripts and scenes to start building.
{{sdkNote}}
Selected transport: {{context.Transport}}
Selected serializer: {{context.Serializer}}
""";
    }

    private static string BuildNuGetConfig(GodotSdkReference sdk) => $$"""
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="godot-local" value="{{sdk.PackageSourcePath}}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
""";

    private static GodotSdkReference? ResolveGodotSdk()
    {
        foreach (var candidate in EnumerateGodotNugetSources())
        {
            var resolved = TryResolveGodotSdk(candidate);
            if (resolved is not null)
                return resolved;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateGodotNugetSources()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var envName in new[] { "LAKONA_RPC_GODOT_NUPKGS", "GODOT_MONO_NUPKGS" })
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                yield return value;
        }

        foreach (var path in new[]
        {
            @"D:\godot",
            @"C:\godot",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        })
        {
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                yield return path;
        }
    }

    private static GodotSdkReference? TryResolveGodotSdk(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
            return null;

        var nupkgDir = Path.GetFileName(candidate).Equals("nupkgs", StringComparison.OrdinalIgnoreCase)
            ? candidate
            : Directory
                .EnumerateFiles(candidate, "Godot.NET.Sdk.*.nupkg", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path));

        if (string.IsNullOrWhiteSpace(nupkgDir) || !Directory.Exists(nupkgDir))
            return null;

        var versions = Directory
            .EnumerateFiles(nupkgDir, "Godot.NET.Sdk.*.nupkg", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Select(static name => name?["Godot.NET.Sdk.".Length..])
            .Select(static versionText => Version.TryParse(versionText, out var version) ? version : null)
            .Where(static version => version is not null)
            .Cast<Version>()
            .OrderByDescending(static version => version)
            .ToArray();

        if (versions.Length == 0)
            return null;

        return new GodotSdkReference(versions[0].ToString(), nupkgDir);
    }

    private sealed record GodotSdkReference(string Version, string PackageSourcePath);
}
