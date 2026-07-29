using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Common;

namespace Lakona.ProjectSystem.Generation.Rendering.Client;

internal sealed class UnityClientRenderer : IClientRenderer
{
    private const string NuGetForUnityAssetResourceName = "Lakona.ProjectSystem.Generation.Rendering.Client.TemplateAssets.NuGetForUnity.4.5.0.zip";

    private static readonly (string PackageId, string Version)[] Unity2022Packages =
    [
        ("com.unity.inputsystem", "1.14.0"),
        ("com.unity.collab-proxy", "2.12.4"),
        ("com.unity.feature.development", "1.0.1"),
        ("com.unity.textmeshpro", "3.0.7"),
        ("com.unity.timeline", "1.7.7"),
        ("com.unity.ugui", "1.0.0"),
        ("com.unity.visualscripting", "1.9.4"),
        ("com.unity.modules.ai", "1.0.0"),
        ("com.unity.modules.androidjni", "1.0.0"),
        ("com.unity.modules.animation", "1.0.0"),
        ("com.unity.modules.assetbundle", "1.0.0"),
        ("com.unity.modules.audio", "1.0.0"),
        ("com.unity.modules.cloth", "1.0.0"),
        ("com.unity.modules.director", "1.0.0"),
        ("com.unity.modules.imageconversion", "1.0.0"),
        ("com.unity.modules.imgui", "1.0.0"),
        ("com.unity.modules.jsonserialize", "1.0.0"),
        ("com.unity.modules.particlesystem", "1.0.0"),
        ("com.unity.modules.physics", "1.0.0"),
        ("com.unity.modules.physics2d", "1.0.0"),
        ("com.unity.modules.screencapture", "1.0.0"),
        ("com.unity.modules.terrain", "1.0.0"),
        ("com.unity.modules.terrainphysics", "1.0.0"),
        ("com.unity.modules.tilemap", "1.0.0"),
        ("com.unity.modules.ui", "1.0.0"),
        ("com.unity.modules.uielements", "1.0.0"),
        ("com.unity.modules.umbra", "1.0.0"),
        ("com.unity.modules.unityanalytics", "1.0.0"),
        ("com.unity.modules.unitywebrequest", "1.0.0"),
        ("com.unity.modules.unitywebrequestassetbundle", "1.0.0"),
        ("com.unity.modules.unitywebrequestaudio", "1.0.0"),
        ("com.unity.modules.unitywebrequesttexture", "1.0.0"),
        ("com.unity.modules.unitywebrequestwww", "1.0.0"),
        ("com.unity.modules.vehicles", "1.0.0"),
        ("com.unity.modules.video", "1.0.0"),
        ("com.unity.modules.vr", "1.0.0"),
        ("com.unity.modules.wind", "1.0.0"),
        ("com.unity.modules.xr", "1.0.0")
    ];

    private static readonly (string PackageId, string Version)[] Unity60Packages =
    [
        ("com.unity.ai.navigation", "2.0.8"),
        ("com.unity.collab-proxy", "2.13.3"),
        ("com.unity.ide.rider", "3.0.36"),
        ("com.unity.ide.visualstudio", "2.0.23"),
        ("com.unity.inputsystem", "1.14.0"),
        ("com.unity.multiplayer.center", "1.0.0"),
        ("com.unity.render-pipelines.universal", "17.0.4"),
        ("com.unity.test-framework", "1.5.1"),
        ("com.unity.timeline", "1.8.7"),
        ("com.unity.ugui", "2.0.0"),
        ("com.unity.visualscripting", "1.9.7"),
        ("com.unity.modules.accessibility", "1.0.0"),
        ("com.unity.modules.ai", "1.0.0"),
        ("com.unity.modules.androidjni", "1.0.0"),
        ("com.unity.modules.animation", "1.0.0"),
        ("com.unity.modules.assetbundle", "1.0.0"),
        ("com.unity.modules.audio", "1.0.0"),
        ("com.unity.modules.cloth", "1.0.0"),
        ("com.unity.modules.director", "1.0.0"),
        ("com.unity.modules.imageconversion", "1.0.0"),
        ("com.unity.modules.imgui", "1.0.0"),
        ("com.unity.modules.jsonserialize", "1.0.0"),
        ("com.unity.modules.particlesystem", "1.0.0"),
        ("com.unity.modules.physics", "1.0.0"),
        ("com.unity.modules.physics2d", "1.0.0"),
        ("com.unity.modules.screencapture", "1.0.0"),
        ("com.unity.modules.terrain", "1.0.0"),
        ("com.unity.modules.terrainphysics", "1.0.0"),
        ("com.unity.modules.tilemap", "1.0.0"),
        ("com.unity.modules.ui", "1.0.0"),
        ("com.unity.modules.uielements", "1.0.0"),
        ("com.unity.modules.umbra", "1.0.0"),
        ("com.unity.modules.unityanalytics", "1.0.0"),
        ("com.unity.modules.unitywebrequest", "1.0.0"),
        ("com.unity.modules.unitywebrequestassetbundle", "1.0.0"),
        ("com.unity.modules.unitywebrequestaudio", "1.0.0"),
        ("com.unity.modules.unitywebrequesttexture", "1.0.0"),
        ("com.unity.modules.unitywebrequestwww", "1.0.0"),
        ("com.unity.modules.vehicles", "1.0.0"),
        ("com.unity.modules.video", "1.0.0"),
        ("com.unity.modules.vr", "1.0.0"),
        ("com.unity.modules.wind", "1.0.0"),
        ("com.unity.modules.xr", "1.0.0")
    ];

    private static readonly (string PackageId, string Version)[] Unity63Packages =
    [
        ("com.unity.ai.navigation", "2.0.9"),
        ("com.unity.collab-proxy", "2.10.2"),
        ("com.unity.ide.rider", "3.0.38"),
        ("com.unity.ide.visualstudio", "2.0.25"),
        ("com.unity.inputsystem", "1.17.0"),
        ("com.unity.multiplayer.center", "1.0.1"),
        ("com.unity.render-pipelines.universal", "17.3.0"),
        ("com.unity.test-framework", "1.6.0"),
        ("com.unity.timeline", "1.8.10"),
        ("com.unity.ugui", "2.0.0"),
        ("com.unity.visualscripting", "1.9.9"),
        ("com.unity.modules.accessibility", "1.0.0"),
        ("com.unity.modules.adaptiveperformance", "1.0.0"),
        ("com.unity.modules.ai", "1.0.0"),
        ("com.unity.modules.androidjni", "1.0.0"),
        ("com.unity.modules.animation", "1.0.0"),
        ("com.unity.modules.assetbundle", "1.0.0"),
        ("com.unity.modules.audio", "1.0.0"),
        ("com.unity.modules.cloth", "1.0.0"),
        ("com.unity.modules.director", "1.0.0"),
        ("com.unity.modules.imageconversion", "1.0.0"),
        ("com.unity.modules.imgui", "1.0.0"),
        ("com.unity.modules.jsonserialize", "1.0.0"),
        ("com.unity.modules.particlesystem", "1.0.0"),
        ("com.unity.modules.physics", "1.0.0"),
        ("com.unity.modules.physics2d", "1.0.0"),
        ("com.unity.modules.screencapture", "1.0.0"),
        ("com.unity.modules.terrain", "1.0.0"),
        ("com.unity.modules.terrainphysics", "1.0.0"),
        ("com.unity.modules.tilemap", "1.0.0"),
        ("com.unity.modules.ui", "1.0.0"),
        ("com.unity.modules.uielements", "1.0.0"),
        ("com.unity.modules.umbra", "1.0.0"),
        ("com.unity.modules.unityanalytics", "1.0.0"),
        ("com.unity.modules.unitywebrequest", "1.0.0"),
        ("com.unity.modules.unitywebrequestassetbundle", "1.0.0"),
        ("com.unity.modules.unitywebrequestaudio", "1.0.0"),
        ("com.unity.modules.unitywebrequesttexture", "1.0.0"),
        ("com.unity.modules.unitywebrequestwww", "1.0.0"),
        ("com.unity.modules.vectorgraphics", "1.0.0"),
        ("com.unity.modules.vehicles", "1.0.0"),
        ("com.unity.modules.video", "1.0.0"),
        ("com.unity.modules.vr", "1.0.0"),
        ("com.unity.modules.wind", "1.0.0"),
        ("com.unity.modules.xr", "1.0.0")
    ];

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
        builder.AddFile("Client/ProjectSettings/ProjectVersion.txt", RenderProjectVersion(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
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
        {{RenderClientEngineDependencyLines(spec)}}
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

    private static string RenderClientEngineDependencyLines(LakonaProjectSpec spec)
    {
        if (spec.ClientEngine == ClientEngine.Tuanjie)
        {
            return RenderTuanjieDependencyLines();
        }

        var packages = spec.ClientEngineVersion switch
        {
            ClientEngineVersion.Unity2022 => Unity2022Packages,
            ClientEngineVersion.Unity60 => Unity60Packages,
            ClientEngineVersion.Unity63 => Unity63Packages,
            _ => throw new ArgumentOutOfRangeException(
                nameof(spec.ClientEngineVersion),
                spec.ClientEngineVersion,
                "Unity projects require a supported Unity version.")
        };

        return string.Join(
            ",\n",
            packages.Select(static package => $"    \"{package.PackageId}\": \"{package.Version}\""));
    }

    private static string RenderTuanjieDependencyLines()
    {
        (string PackageId, string Version)[] packages =
        [
            ("cn.tuanjie.codely.bridge", "1.0.69"),
            ("com.unity.collab-proxy", "2.5.1"),
            ("com.unity.feature.development", "1.0.1"),
            ("com.unity.inputsystem", "1.14.0"),
            ("com.unity.textmeshpro", "3.0.7"),
            ("com.unity.timeline", "1.7.7"),
            ("com.unity.ugui", "1.0.0"),
            ("com.unity.visualscripting", "1.9.4"),
            ("com.unity.modules.ai", "1.0.0"),
            ("com.unity.modules.androidjni", "1.0.0"),
            ("com.unity.modules.animation", "1.0.0"),
            ("com.unity.modules.assetbundle", "1.0.0"),
            ("com.unity.modules.audio", "1.0.0"),
            ("com.unity.modules.cloth", "1.0.0"),
            ("com.unity.modules.director", "1.0.0"),
            ("com.unity.modules.imageconversion", "1.0.0"),
            ("com.unity.modules.imgui", "1.0.0"),
            ("com.unity.modules.infinity", "1.0.0"),
            ("com.unity.modules.jsonserialize", "1.0.0"),
            ("com.unity.modules.particlesystem", "1.0.0"),
            ("com.unity.modules.physics", "1.0.0"),
            ("com.unity.modules.physics2d", "1.0.0"),
            ("com.unity.modules.screencapture", "1.0.0"),
            ("com.unity.modules.terrain", "1.0.0"),
            ("com.unity.modules.terrainphysics", "1.0.0"),
            ("com.unity.modules.tilemap", "1.0.0"),
            ("com.unity.modules.ui", "1.0.0"),
            ("com.unity.modules.uielements", "1.0.0"),
            ("com.unity.modules.unityanalytics", "1.0.0"),
            ("com.unity.modules.unitywebrequest", "1.0.0"),
            ("com.unity.modules.unitywebrequestassetbundle", "1.0.0"),
            ("com.unity.modules.unitywebrequestaudio", "1.0.0"),
            ("com.unity.modules.unitywebrequesttexture", "1.0.0"),
            ("com.unity.modules.unitywebrequestwww", "1.0.0"),
            ("com.unity.modules.vehicles", "1.0.0"),
            ("com.unity.modules.video", "1.0.0"),
            ("com.unity.modules.vr", "1.0.0"),
            ("com.unity.modules.wind", "1.0.0"),
            ("com.unity.modules.xr", "1.0.0")
        ];

        return string.Join(
            ",\n",
            packages.Select(static package => $"    \"{package.PackageId}\": \"{package.Version}\""));
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

    private static string RenderProjectVersion(LakonaProjectSpec spec)
    {
        return spec.ClientEngine switch
        {
            ClientEngine.Tuanjie =>
                $"m_EditorVersion: {ClientEngineVersions.TuanjieUnityEditor}\n" +
                $"m_EditorVersionWithRevision: {ClientEngineVersions.TuanjieUnityEditor} ({ClientEngineVersions.TuanjieUnityEditorRevision})\n" +
                $"m_TuanjieEditorVersion: {ClientEngineVersions.Tuanjie}",
            ClientEngine.Unity => spec.ClientEngineVersion switch
            {
                ClientEngineVersion.Unity2022 => RenderUnityProjectVersion(
                    ClientEngineVersions.Unity2022,
                    ClientEngineVersions.Unity2022Revision),
                ClientEngineVersion.Unity60 => RenderUnityProjectVersion(
                    ClientEngineVersions.Unity60,
                    ClientEngineVersions.Unity60Revision),
                ClientEngineVersion.Unity63 => RenderUnityProjectVersion(
                    ClientEngineVersions.Unity63,
                    ClientEngineVersions.Unity63Revision),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(spec.ClientEngineVersion),
                    spec.ClientEngineVersion,
                    "Unity projects require a supported Unity version.")
            },
            _ => throw new ArgumentOutOfRangeException(nameof(spec.ClientEngine), spec.ClientEngine, null)
        };
    }

    private static string RenderUnityProjectVersion(string editorVersion, string revision)
    {
        return $"m_EditorVersion: {editorVersion}\n" +
               $"m_EditorVersionWithRevision: {editorVersion} ({revision})";
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
