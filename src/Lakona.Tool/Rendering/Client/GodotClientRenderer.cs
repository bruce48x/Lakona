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
        builder.AddFile("Client/Game.tscn", GodotClientAssetTemplates.RenderGameScene(), FileWriteMode.Replace, GeneratedFileKind.GodotScene);
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
            <LakonaRpcGeneratedNamespace>Client.Generated</LakonaRpcGeneratedNamespace>
            <LakonaGameGenerateClient>true</LakonaGameGenerateClient>
          </PropertyGroup>

          <ItemGroup>
            <CompilerVisibleProperty Include="LakonaRpcGenerateClient" />
            <CompilerVisibleProperty Include="LakonaRpcGeneratedNamespace" />
            <CompilerVisibleProperty Include="LakonaGameGenerateClient" />
          </ItemGroup>

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
        run/main_scene="res://Game.tscn"
        config/features=PackedStringArray("4.6", "C#")

        [dotnet]

        project/assembly_name="Client"
        """;
    }

    private static void AddClientCodeFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Client/Scripts/Game/GameClient.cs", GodotClientCodeTemplates.RenderGameClient(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Game/GameClient.cs.uid", GodotClientAssetTemplates.RenderUid(GodotClientAssetTemplates.GameClientUid), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Game/GameScene.cs", GodotClientCodeTemplates.RenderGameScene(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Game/GameScene.cs.uid", GodotClientAssetTemplates.RenderUid(GodotClientAssetTemplates.GameSceneScriptUid), FileWriteMode.Replace, GeneratedFileKind.Text);
    }
}
