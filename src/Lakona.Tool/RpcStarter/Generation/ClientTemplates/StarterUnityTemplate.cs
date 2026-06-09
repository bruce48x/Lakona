namespace Lakona.Tool.RpcStarter;

internal static class StarterUnityTemplate
{
    private const string NuGetForUnityAssetResourceName = "Lakona.Tool.RpcStarter.TemplateAssets.NuGetForUnity.4.5.0.zip";

    public static void Generate(StarterTemplateContext context)
    {
        EnsureClientDirectories(context.Paths.ClientPath);
        if (context.NuGetForUnitySource == NuGetForUnitySourceKind.Embedded)
        {
            ExtractEmbeddedNuGetForUnityPackage(context.Paths.ClientPath);
        }

        var artifacts = BuildArtifacts(context);

        var clientPath = context.Paths.ClientPath;
        var generationMarkerPath = Path.Combine(clientPath, "Assets", "Scripts", "Rpc", "LakonaRpcGeneration.cs");

        StarterFileWriter.Write(Path.Combine(clientPath, "Packages", "manifest.json"), artifacts.Manifest);
        StarterFileWriter.Write(Path.Combine(clientPath, "Assets", "packages.config"), artifacts.PackagesConfig);
        StarterFileWriter.Write(Path.Combine(clientPath, "Assets", "NuGet.config"), artifacts.NuGetConfig);
        StarterFileWriter.Write(generationMarkerPath, GetUnityGenerationMarkerScript());
        StarterFileWriter.Write(Path.Combine(clientPath, "README.md"), artifacts.Readme);
        StarterFileWriter.Write(Path.Combine(clientPath, "ProjectSettings", "ProjectVersion.txt"), artifacts.ProjectVersion);
    }

    private static void EnsureClientDirectories(string clientPath)
    {
        Directory.CreateDirectory(Path.Combine(clientPath, "Assets"));
        Directory.CreateDirectory(Path.Combine(clientPath, "Packages"));
        Directory.CreateDirectory(Path.Combine(clientPath, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(clientPath, "Assets", "Scripts", "Rpc"));
    }

    private static UnityClientArtifacts BuildArtifacts(StarterTemplateContext context) => new(
        BuildManifest(context),
        BuildPackagesConfig(context),
        BuildNuGetConfig(context.ClientEngine),
        BuildReadme(context),
        BuildProjectVersion(context.ClientEngine),
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);

    private static string BuildManifest(StarterTemplateContext context) => $$"""
{
  "dependencies": {
{{BuildNuGetForUnityDependencyLine(context)}}
    "com.unity.ide.rider": "3.0.39",
    "com.unity.ide.visualstudio": "2.0.23",
    "com.unity.modules.audio": "1.0.0",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.physics2d": "1.0.0",
    "com.unity.modules.screencapture": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.ugui": "1.0.0",
    "com.{{context.CompanyId}}.shared": "file:../../{{context.SharedProjectName}}"
  }{{BuildScopedRegistriesBlock(context)}}
}
""";

    private static string BuildNuGetForUnityDependencyLine(StarterTemplateContext context) => context.NuGetForUnitySource switch
    {
        NuGetForUnitySourceKind.Embedded => string.Empty,
        NuGetForUnitySourceKind.OpenUpm => "    \"com.github-glitchenzo.nugetforunity\": \"4.5.0\",\n",
        _ => throw new ArgumentOutOfRangeException(nameof(context.NuGetForUnitySource), context.NuGetForUnitySource, null)
    };

    private static string BuildScopedRegistriesBlock(StarterTemplateContext context) => context.NuGetForUnitySource switch
    {
        NuGetForUnitySourceKind.Embedded => string.Empty,
        NuGetForUnitySourceKind.OpenUpm => """
,
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.github-glitchenzo.nugetforunity"
      ]
    }
  ]
""",
        _ => throw new ArgumentOutOfRangeException(nameof(context.NuGetForUnitySource), context.NuGetForUnitySource, null)
    };

    private static string BuildPackagesConfig(StarterTemplateContext context)
    {
        var packageReferences = PackageReferenceText.RenderNuGetForUnityPackages(StarterDependencyPlanner.Create(context, StarterProjectRole.UnityClient));

        return $$"""
<?xml version="1.0" encoding="utf-8"?>
<packages>
{{packageReferences}}
</packages>
""";
    }

    private static string BuildNuGetConfig(ClientEngineKind clientEngine)
    {
        var packageSource = clientEngine == ClientEngineKind.Tuanjie
            ? "https://nuget.cdn.azure.cn/v3/index.json"
            : "https://api.nuget.org/v3/index.json";

        return $$"""
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="{{packageSource}}" enableCredentialProvider="false" />
  </packageSources>
  <disabledPackageSources />
  <activePackageSource>
    <add key="All" value="(Aggregate source)" />
  </activePackageSource>
  <config>
    <add key="packageInstallLocation" value="CustomWithinAssets" />
    <add key="repositoryPath" value="./Packages" />
    <add key="PackagesConfigDirectoryPath" value="." />
    <add key="slimRestore" value="true" />
    <add key="PreferNetStandardOverNetFramework" value="true" />
  </config>
</configuration>
""";
    }

    private static string BuildReadme(StarterTemplateContext context) => $$"""
# {{context.ClientEngine.GetDisplayName()}} Client Starter ({{context.ClientEngine.GetStarterClientLabel()}})

1. Open this folder with {{context.ClientEngine.GetStarterClientLabel()}}.
2. Wait for {{GetNuGetForUnitySetupDescription(context)}}.
3. In the editor: `NuGet -> Restore Packages` to install Lakona.Rpc latest packages.
4. Shared code is provided by local UPM package:
   - `com.{{context.CompanyId}}.shared` -> `../../{{context.SharedProjectName}}`
5. Add your own scripts and scenes to start building.

Selected transport: {{context.Transport}}
Selected serializer: {{context.Serializer}}
NuGetForUnity source: {{context.NuGetForUnitySource}}
""";

    private static string GetNuGetForUnitySetupDescription(StarterTemplateContext context) => context.NuGetForUnitySource switch
    {
        NuGetForUnitySourceKind.Embedded => "the bundled embedded `NuGetForUnity` package to import",
        NuGetForUnitySourceKind.OpenUpm => "`NuGetForUnity` to download from OpenUPM and finish importing",
        _ => throw new ArgumentOutOfRangeException(nameof(context.NuGetForUnitySource), context.NuGetForUnitySource, null)
    };

    private static void ExtractEmbeddedNuGetForUnityPackage(string clientPath)
    {
        StarterFileWriter.ExtractEmbeddedZip(
            NuGetForUnityAssetResourceName,
            Path.Combine(clientPath, "Packages"));
    }

    private static string BuildProjectVersion(ClientEngineKind clientEngine) => clientEngine switch
    {
        ClientEngineKind.Unity => "m_EditorVersion: 2022.3.62f3c1\nm_EditorVersionWithRevision: 2022.3.62f3c1 (1623fc0bbb97)\n",
        ClientEngineKind.UnityCn => "m_EditorVersion: 2022.3.62f3c1\nm_EditorVersionWithRevision: 2022.3.62f3c1 (1623fc0bbb97)\n",
        ClientEngineKind.Tuanjie => "m_EditorVersion: 2022.3.61t11\nm_EditorVersionWithRevision: 2022.3.61t11 (122146d53e32)\nm_TuanjieEditorVersion: 1.6.10\n",
        _ => throw new ArgumentOutOfRangeException(nameof(clientEngine), clientEngine, null)
    };

    private static string GetUnityGenerationMarkerScript() => """
#nullable enable

using Lakona.Rpc.Core;

[assembly: LakonaRpcGenerateClient("Rpc.Generated")]
""";

}
