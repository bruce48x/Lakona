using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Common;

namespace Lakona.Tool.Rendering.Client;

internal sealed class UnityClientRenderer : IClientRenderer
{
    private const string NuGetForUnityAssetResourceName = "Lakona.Tool.Rendering.Client.TemplateAssets.NuGetForUnity.4.5.0.zip";

    public bool Supports(ClientEngine engine)
    {
        return ClientEnginePolicy.IsUnityCompatible(engine);
    }

    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        if (spec.NuGetForUnitySource == NuGetForUnitySource.Embedded)
        {
            builder.AddArchive(NuGetForUnityAssetResourceName, "Client/Packages");
        }

        builder.AddFile("Client/Packages/manifest.json", RenderManifest(spec), FileWriteMode.Replace, GeneratedFileKind.Json);
        builder.AddFile("Client/ProjectSettings/ProjectVersion.txt", RenderProjectVersion(spec.ClientEngine), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/ProjectSettings/ProjectSettings.asset", UnityClientAssetTemplates.RenderPlayerSettings(spec.Name), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/packages.config", RenderPackagesConfig(spec), FileWriteMode.Replace, GeneratedFileKind.Xml);
        builder.AddFile("Client/Assets/NuGet.config", RenderNuGetConfig(spec.ClientEngine), FileWriteMode.Replace, GeneratedFileKind.Xml);
        AddClientCodeFiles(spec, builder);
        AddUnityAssetFiles(spec, builder);
    }

    private static string RenderManifest(LakonaProjectSpec spec)
    {
        return $$"""
        {
          "dependencies": {
        {{RenderNuGetForUnityDependencyLine(spec)}}
            "{{spec.Layout.UnityPackageId}}.shared": "file:../../Shared",
            "com.unity.inputsystem": "1.14.0",
            "com.unity.ugui": "1.0.0",
            "com.unity.modules.audio": "1.0.0",
            "com.unity.modules.imgui": "1.0.0",
            "com.unity.modules.ui": "1.0.0",
            "com.unity.modules.physics": "1.0.0",
            "com.unity.modules.physics2d": "1.0.0",
            "com.unity.modules.uielements": "1.0.0"
          }{{RenderScopedRegistriesBlock(spec)}}
        }
        """;
    }

    private static string RenderNuGetForUnityDependencyLine(LakonaProjectSpec spec)
    {
        return spec.NuGetForUnitySource == NuGetForUnitySource.OpenUpm
            ? "    \"com.github-glitchenzo.nugetforunity\": \"4.5.0\",\n"
            : string.Empty;
    }

    private static string RenderScopedRegistriesBlock(LakonaProjectSpec spec)
    {
        return spec.NuGetForUnitySource == NuGetForUnitySource.OpenUpm
            ? """
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
        """
            : string.Empty;
    }

    private static string RenderProjectVersion(ClientEngine engine)
    {
        return engine switch
        {
            ClientEngine.Tuanjie => $"m_EditorVersion: {ClientEngineVersions.TuanjieUnityEditor}\nm_TuanjieEditorVersion: {ClientEngineVersions.Tuanjie}",
            ClientEngine.UnityCn => "m_EditorVersion: 2022.3.62f3c1",
            _ => "m_EditorVersion: 2022.3.62f1"
        };
    }

    private static string RenderPackagesConfig(LakonaProjectSpec spec)
    {
        var packages = PackageReferenceRenderer.RenderNuGetForUnityPackages(
            DependencyPlanner.Create(ProjectTarget.UnityClient, spec).PackageReferences);
        return $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <packages>
        {{packages}}
        </packages>
        """;
    }

    private static string RenderNuGetConfig(ClientEngine engine)
    {
        var source = engine == ClientEngine.Tuanjie
            ? "https://nuget.cdn.azure.cn/v3/index.json"
            : "https://api.nuget.org/v3/index.json";
        return $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="nuget.org" value="{{source}}" enableCredentialProvider="false" />
          </packageSources>
          <disabledPackageSources />
          <activePackageSource>
            <add key="All" value="(Aggregate source)" />
          </activePackageSource>
          <!--
            targetFramework in packages.config guides NuGet dependency resolution.
            Unity plugin TFM enablement is enforced by LakonaGameNuGetPackageImportGuard.
          -->
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

    private static void AddClientCodeFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Client/Assets/Scripts/Game/GameClient.cs", UnityClientCodeTemplates.RenderGameClient(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Game/GameClient.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.GameClientGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Game/GameController.cs", UnityClientCodeTemplates.RenderGameController(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Game/GameController.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.GameControllerGuid), FileWriteMode.Replace, GeneratedFileKind.Text);

        builder.AddFile("Client/Assets/Editor/DefaultSceneLoader.cs", UnityClientCodeTemplates.RenderDefaultSceneLoader(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Editor/DefaultSceneLoader.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.DefaultSceneLoaderGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Editor/LakonaGameNuGetPackageImportGuard.cs", UnityClientCodeTemplates.RenderNuGetPackageImportGuard(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Editor/LakonaGameNuGetPackageImportGuard.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.ImportGuardGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static void AddUnityAssetFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Client/Assets/Input/LakonaInputActions.inputactions", UnityClientAssetTemplates.RenderInputActions(), FileWriteMode.Replace, GeneratedFileKind.Json);
        builder.AddFile("Client/Assets/Input/LakonaInputActions.inputactions.meta", UnityClientAssetTemplates.RenderInputActionsMeta(), FileWriteMode.Replace, GeneratedFileKind.Text);

        builder.AddFile("Client/Assets/UI/Game.uxml", UnityClientAssetTemplates.RenderGameUxml(), FileWriteMode.Replace, GeneratedFileKind.Xml);
        builder.AddFile("Client/Assets/UI/Game.uxml.meta", UnityClientAssetTemplates.RenderUxmlMeta(UnityClientAssetTemplates.GameUxmlGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI/Game.uss", UnityClientAssetTemplates.RenderGameUss(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI/Game.uss.meta", UnityClientAssetTemplates.RenderUssMeta(UnityClientAssetTemplates.GameUssGuid), FileWriteMode.Replace, GeneratedFileKind.Text);

        builder.AddFile("Client/Assets/UI/LakonaGamePanelSettings.asset", UnityClientAssetTemplates.RenderPanelSettingsAsset(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI/LakonaGamePanelSettings.asset.meta", UnityClientAssetTemplates.RenderNativeAssetMeta(UnityClientAssetTemplates.PanelSettingsGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss", UnityClientAssetTemplates.RenderDefaultRuntimeTheme(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss.meta", UnityClientAssetTemplates.RenderTssMeta(UnityClientAssetTemplates.RuntimeThemeGuid), FileWriteMode.Replace, GeneratedFileKind.Text);

        builder.AddFile("Client/Assets/Scenes/Game.unity", UnityClientAssetTemplates.RenderGameScene(spec.Transport), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scenes/Game.unity.meta", UnityClientAssetTemplates.RenderSceneMeta(UnityClientAssetTemplates.GameSceneGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/ProjectSettings/EditorBuildSettings.asset", UnityClientAssetTemplates.RenderGameEditorBuildSettings(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }
}
