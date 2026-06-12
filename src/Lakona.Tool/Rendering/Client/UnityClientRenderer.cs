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
            "com.unity.modules.audio": "1.0.0",
            "com.unity.modules.imgui": "1.0.0",
            "com.unity.modules.ui": "1.0.0",
            "com.unity.modules.physics": "1.0.0",
            "com.unity.modules.physics2d": "1.0.0",
            "com.unity.modules.uielements": "1.0.0",
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
            ClientEngine.Tuanjie => "m_EditorVersion: 2022.3.61t11\nm_TuanjieEditorVersion: 1.6.10",
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
        builder.AddFile("Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs", UnityClientCodeTemplates.RenderRpcGeneration(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.RpcGenerationGuid), FileWriteMode.Replace, GeneratedFileKind.Text);

        builder.AddFile("Client/Assets/Scripts/Login/LoginClient.cs", UnityClientCodeTemplates.RenderLoginClient(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Login/LoginClient.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.LoginClientGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Login/LoginUI.cs", UnityClientCodeTemplates.RenderLoginUI(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Login/LoginUI.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.LoginUiGuid), FileWriteMode.Replace, GeneratedFileKind.Text);

        builder.AddFile("Client/Assets/Scripts/Chat/ChatClient.cs", UnityClientCodeTemplates.RenderChatClient(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Chat/ChatClient.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.ChatClientGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Chat/ChatSession.cs", UnityClientCodeTemplates.RenderChatSession(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Chat/ChatSession.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.ChatSessionGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Chat/ChatUI.cs", UnityClientCodeTemplates.RenderChatUI(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Chat/ChatUI.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.ChatUiGuid), FileWriteMode.Replace, GeneratedFileKind.Text);

        builder.AddFile("Client/Assets/Editor/LakonaGameNuGetPackageImportGuard.cs", UnityClientCodeTemplates.RenderNuGetPackageImportGuard(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Editor/LakonaGameNuGetPackageImportGuard.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.ImportGuardGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static void AddUnityAssetFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Client/Assets/UI/LoginScene.uxml", UnityClientAssetTemplates.RenderLoginUxml(), FileWriteMode.Replace, GeneratedFileKind.Xml);
        builder.AddFile("Client/Assets/UI/LoginScene.uxml.meta", UnityClientAssetTemplates.RenderUxmlMeta(UnityClientAssetTemplates.LoginSceneUxmlGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI/LoginScene.uss", UnityClientAssetTemplates.RenderLoginUss(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI/LoginScene.uss.meta", UnityClientAssetTemplates.RenderUssMeta(UnityClientAssetTemplates.LoginSceneUssGuid), FileWriteMode.Replace, GeneratedFileKind.Text);

        builder.AddFile("Client/Assets/UI/ChatScene.uxml", UnityClientAssetTemplates.RenderChatUxml(), FileWriteMode.Replace, GeneratedFileKind.Xml);
        builder.AddFile("Client/Assets/UI/ChatScene.uxml.meta", UnityClientAssetTemplates.RenderUxmlMeta(UnityClientAssetTemplates.ChatSceneUxmlGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI/ChatScene.uss", UnityClientAssetTemplates.RenderChatUss(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI/ChatScene.uss.meta", UnityClientAssetTemplates.RenderUssMeta(UnityClientAssetTemplates.ChatSceneUssGuid), FileWriteMode.Replace, GeneratedFileKind.Text);

        builder.AddFile("Client/Assets/UI/LakonaGameChatPanelSettings.asset", UnityClientAssetTemplates.RenderPanelSettingsAsset(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI/LakonaGameChatPanelSettings.asset.meta", UnityClientAssetTemplates.RenderNativeAssetMeta(UnityClientAssetTemplates.PanelSettingsGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss", UnityClientAssetTemplates.RenderDefaultRuntimeTheme(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss.meta", UnityClientAssetTemplates.RenderTssMeta(UnityClientAssetTemplates.RuntimeThemeGuid), FileWriteMode.Replace, GeneratedFileKind.Text);

        builder.AddFile("Client/Assets/Scenes/LoginScene.unity", UnityClientAssetTemplates.RenderLoginScene(spec.Transport), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scenes/LoginScene.unity.meta", UnityClientAssetTemplates.RenderSceneMeta(UnityClientAssetTemplates.LoginSceneGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scenes/ChatScene.unity", UnityClientAssetTemplates.RenderChatScene(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scenes/ChatScene.unity.meta", UnityClientAssetTemplates.RenderSceneMeta(UnityClientAssetTemplates.ChatSceneGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/ProjectSettings/EditorBuildSettings.asset", UnityClientAssetTemplates.RenderEditorBuildSettings(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }
}
