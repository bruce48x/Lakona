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
        builder.AddFile("Client/Assets/Scripts/Login/LoginClient.cs", RenderLoginClient(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Assets/Scripts/Chat/ChatClient.cs", RenderChatClient(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderManifest(LakonaProjectSpec spec)
    {
        return $$"""
        {
          "dependencies": {
        {{RenderNuGetForUnityDependencyLine(spec)}}
            "{{spec.Layout.UnityPackageId}}.shared": "file:../../Shared"
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
            <add key="nuget.org" value="{{source}}" enableCredentialProvider="false" />
          </packageSources>
        </configuration>
        """;
    }

    private static string RenderLoginClient()
    {
        return """
        namespace Client.Login
        {
            public sealed class LoginClient
            {
            }
        }
        """;
    }

    private static string RenderChatClient()
    {
        return """
        namespace Client.Chat
        {
            public sealed class ChatClient
            {
            }
        }
        """;
    }
}
