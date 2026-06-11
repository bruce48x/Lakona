using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Common;

namespace Lakona.Tool.Rendering.Client;

internal sealed class GodotClientRenderer : IClientRenderer
{
    public bool Supports(ClientEngine engine)
    {
        return engine == ClientEngine.Godot;
    }

    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Client/project.godot", RenderProjectGodot(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Client.csproj", RenderClientProject(spec), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Client/Theme/LakonaTheme.tres", GodotClientAssetTemplates.RenderTheme(), FileWriteMode.Replace, GeneratedFileKind.GodotTheme);
        builder.AddFile("Client/Login.tscn", GodotClientAssetTemplates.RenderLoginScene(), FileWriteMode.Replace, GeneratedFileKind.GodotScene);
        builder.AddFile("Client/Chat.tscn", GodotClientAssetTemplates.RenderChatScene(), FileWriteMode.Replace, GeneratedFileKind.GodotScene);
        AddClientCodeFiles(spec, builder);
    }

    private static string RenderClientProject(LakonaProjectSpec spec)
    {
        var packageReferences = PackageReferenceRenderer.RenderSdkPackageReferences(
            DependencyPlanner.Create(ProjectTarget.GodotClient, spec).PackageReferences);

        return $$"""
        <Project Sdk="Godot.NET.Sdk/4.6.1">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <EnableDynamicLoading>true</EnableDynamicLoading>
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
          </ItemGroup>

          <ItemGroup>
        {{packageReferences}}
          </ItemGroup>
        </Project>
        """;
    }

    private static string RenderProjectGodot(LakonaProjectSpec spec)
    {
        return $$"""
        ; Engine configuration file.
        ; It's best edited using the editor UI and not directly,
        ; since the parameters that go here are not all obvious.

        config_version=5

        [application]

        config/name="{{spec.Name}}"
        run/main_scene="res://Login.tscn"
        config/features=PackedStringArray("4.6", "C#")

        [autoload]

        ChatSession="*res://Scripts/Chat/ChatSession.cs"

        [dotnet]

        project/assembly_name="Client"
        """;
    }

    private static void AddClientCodeFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Client/Scripts/Login/LoginClient.cs", GodotClientCodeTemplates.RenderLoginClient(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Login/LoginClient.cs.uid", GodotClientAssetTemplates.RenderUid(GodotClientAssetTemplates.LoginClientUid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Login/LoginScene.cs", GodotClientCodeTemplates.RenderLoginScene(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Login/LoginScene.cs.uid", GodotClientAssetTemplates.RenderUid(GodotClientAssetTemplates.LoginSceneUid), FileWriteMode.Replace, GeneratedFileKind.Text);

        builder.AddFile("Client/Scripts/Chat/ChatClient.cs", GodotClientCodeTemplates.RenderChatClient(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Chat/ChatClient.cs.uid", GodotClientAssetTemplates.RenderUid(GodotClientAssetTemplates.ChatClientUid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Chat/ChatSession.cs", GodotClientCodeTemplates.RenderChatSession(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Chat/ChatSession.cs.uid", GodotClientAssetTemplates.RenderUid(GodotClientAssetTemplates.ChatSessionUid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Chat/ChatScene.cs", GodotClientCodeTemplates.RenderChatScene(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Chat/ChatScene.cs.uid", GodotClientAssetTemplates.RenderUid(GodotClientAssetTemplates.ChatSceneUid), FileWriteMode.Replace, GeneratedFileKind.Text);
    }
}
