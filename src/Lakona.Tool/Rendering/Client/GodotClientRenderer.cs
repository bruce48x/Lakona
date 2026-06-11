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
        builder.AddFile("Client/project.godot", RenderProjectGodot(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Client.csproj", RenderClientProject(spec), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Client/Login.tscn", RenderLoginScene(), FileWriteMode.Replace, GeneratedFileKind.GodotScene);
        builder.AddFile("Client/Chat.tscn", RenderChatScene(), FileWriteMode.Replace, GeneratedFileKind.GodotScene);
        builder.AddFile("Client/Theme/LakonaTheme.tres", RenderTheme(), FileWriteMode.Replace, GeneratedFileKind.GodotTheme);
        builder.AddFile("Client/Scripts/Login/LoginScene.cs", RenderLoginScript(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/Scripts/Chat/ChatScene.cs", RenderChatScript(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderClientProject(LakonaProjectSpec spec)
    {
        var packageReferences = PackageReferenceRenderer.RenderSdkPackageReferences(
            DependencyPlanner.Create(ProjectTarget.GodotClient, spec).PackageReferences);

        return $$"""
        <Project Sdk="Godot.NET.Sdk/4.4.1">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <EnableDynamicLoading>true</EnableDynamicLoading>
            <RootNamespace>Client</RootNamespace>
            <LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>
            <LakonaRpcGeneratedNamespace>Client.Generated</LakonaRpcGeneratedNamespace>
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

    private static string RenderProjectGodot()
    {
        return """
        ; Engine configuration file.

        [application]
        config/name="Lakona Client"
        run/main_scene="res://Login.tscn"
        """;
    }

    private static string RenderLoginScene()
    {
        return """
        [gd_scene load_steps=2 format=3]

        [ext_resource type="Script" path="res://Scripts/Login/LoginScene.cs" id="1"]

        [node name="Login" type="Control"]
        script = ExtResource("1")

        [node name="PlayerName" type="LineEdit" parent="."]
        unique_name_in_owner = true

        [node name="LoginButton" type="Button" parent="."]
        unique_name_in_owner = true
        text = "Login"
        """;
    }

    private static string RenderChatScene()
    {
        return """
        [gd_scene load_steps=2 format=3]

        [ext_resource type="Script" path="res://Scripts/Chat/ChatScene.cs" id="1"]

        [node name="Chat" type="Control"]
        script = ExtResource("1")

        [node name="Messages" type="RichTextLabel" parent="."]
        unique_name_in_owner = true

        [node name="MessageText" type="LineEdit" parent="."]
        unique_name_in_owner = true
        """;
    }

    private static string RenderTheme()
    {
        return """
        [gd_resource type="Theme" format=3]
        """;
    }

    private static string RenderLoginScript()
    {
        return """
        using Godot;

        namespace Client.Login;

        public partial class LoginScene : Control
        {
            public override void _Ready()
            {
                _ = GetNode<LineEdit>("%PlayerName");
                _ = GetNode<Button>("%LoginButton");
            }
        }
        """;
    }

    private static string RenderChatScript()
    {
        return """
        using Godot;

        namespace Client.Chat;

        public partial class ChatScene : Control
        {
            public override void _Ready()
            {
                _ = GetNode<RichTextLabel>("%Messages");
                _ = GetNode<LineEdit>("%MessageText");
            }
        }
        """;
    }
}
