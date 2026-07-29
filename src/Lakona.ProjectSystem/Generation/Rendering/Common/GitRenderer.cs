using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Generation.Rendering.Common;

internal sealed class GitRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        var isUnity = spec.ClientEngine is ClientEngine.Unity or ClientEngine.Tuanjie;
        var isGodot = spec.ClientEngine is ClientEngine.Godot;
        builder.AddFile(".gitignore", RenderGitIgnore(isUnity, isGodot), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile(".gitattributes", RenderGitAttributes(spec.ClientEngine), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderGitIgnore(bool isUnity, bool isGodot)
    {
        var lines = new List<string>
        {
            "**/bin/",
            "**/obj/",
            "/_artifacts/",
            ".vs/",
            ".idea/",
            "*.user",
            "*.suo"
        };

        if (isUnity)
        {
            lines.Add("/Client/[Ll]ibrary/");
            lines.Add("/Client/[Tt]emp/");
            lines.Add("/Client/[Oo]bj/");
            lines.Add("/Client/[Bb]uild/");
            lines.Add("/Client/[Bb]uilds/");
            lines.Add("/Client/[Ll]ogs/");
            lines.Add("/Client/[Uu]ser[Ss]ettings/");
            lines.Add("/Client/Assets/Packages/");
        }
        else if (isGodot)
        {
            lines.Add("/Client/.godot/");
            lines.Add("/Client/.import/");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string RenderGitAttributes(ClientEngine engine)
    {
        var common = """
        # Normalize text consistently across Windows, macOS, and Linux.
        * text=auto eol=lf
        *.bat text eol=crlf
        *.cmd text eol=crlf

        # .NET, C#, and MSBuild source.
        *.cs text eol=lf diff=csharp
        *.config text eol=lf
        *.json text eol=lf
        *.csproj text eol=lf
        *.editorconfig text eol=lf
        *.globalconfig text eol=lf
        *.http text eol=lf
        *.proj text eol=lf
        *.projitems text eol=lf
        *.props text eol=lf
        *.resx text eol=lf
        *.ruleset text eol=lf
        *.targets text eol=lf
        *.sln text eol=crlf
        *.slnx text eol=lf
        *.dll binary
        *.exe binary
        *.pdb binary
        *.snk binary
        *.pfx binary
        *.nupkg binary
        *.snupkg binary
        *.wasm binary
        *.7z binary
        *.gz binary
        *.zip binary

        # Documentation, automation, and deployment configuration.
        Dockerfile text eol=lf
        .dockerignore text eol=lf
        *.md text eol=lf
        *.ps1 text eol=lf
        *.sh text eol=lf
        *.xml text eol=lf
        *.yaml text eol=lf
        *.yml text eol=lf
        """;

        return engine switch
        {
            ClientEngine.Unity or ClientEngine.Tuanjie => common + Environment.NewLine + RenderUnityAttributes(),
            ClientEngine.Godot => common + Environment.NewLine + RenderGodotAttributes(),
            ClientEngine.Console => common,
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };
    }

    private static string RenderUnityAttributes()
    {
        return """

        # Unity and Tuanjie text-serialized project data.
        *.meta text eol=lf
        *.unity text eol=lf merge=unityyamlmerge
        *.prefab text eol=lf merge=unityyamlmerge
        *.mat text eol=lf
        *.physicMaterial text eol=lf
        *.physicsMaterial2D text eol=lf
        *.controller text eol=lf
        *.anim text eol=lf
        *.overrideController text eol=lf
        *.mask text eol=lf
        *.playable text eol=lf
        *.preset text eol=lf
        *.renderTexture text eol=lf
        *.spriteatlas text eol=lf
        *.asmdef text eol=lf
        *.asmref text eol=lf
        *.inputactions text eol=lf
        /Client/Packages/** text eol=lf
        /Client/ProjectSettings/** text eol=lf

        # Git LFS for source art, media, fonts, and large interchange assets.
        *.3ds filter=lfs diff=lfs merge=lfs -text
        *.blend filter=lfs diff=lfs merge=lfs -text
        *.dxf filter=lfs diff=lfs merge=lfs -text
        *.fbx filter=lfs diff=lfs merge=lfs -text
        *.gltf filter=lfs diff=lfs merge=lfs -text
        *.glb filter=lfs diff=lfs merge=lfs -text
        *.ma filter=lfs diff=lfs merge=lfs -text
        *.max filter=lfs diff=lfs merge=lfs -text
        *.mb filter=lfs diff=lfs merge=lfs -text
        *.obj filter=lfs diff=lfs merge=lfs -text
        *.exr filter=lfs diff=lfs merge=lfs -text
        *.gif filter=lfs diff=lfs merge=lfs -text
        *.hdr filter=lfs diff=lfs merge=lfs -text
        *.jpeg filter=lfs diff=lfs merge=lfs -text
        *.jpg filter=lfs diff=lfs merge=lfs -text
        *.png filter=lfs diff=lfs merge=lfs -text
        *.psb filter=lfs diff=lfs merge=lfs -text
        *.psd filter=lfs diff=lfs merge=lfs -text
        *.tga filter=lfs diff=lfs merge=lfs -text
        *.tif filter=lfs diff=lfs merge=lfs -text
        *.tiff filter=lfs diff=lfs merge=lfs -text
        *.webp filter=lfs diff=lfs merge=lfs -text
        *.aif filter=lfs diff=lfs merge=lfs -text
        *.aiff filter=lfs diff=lfs merge=lfs -text
        *.flac filter=lfs diff=lfs merge=lfs -text
        *.mp3 filter=lfs diff=lfs merge=lfs -text
        *.ogg filter=lfs diff=lfs merge=lfs -text
        *.wav filter=lfs diff=lfs merge=lfs -text
        *.avi filter=lfs diff=lfs merge=lfs -text
        *.mov filter=lfs diff=lfs merge=lfs -text
        *.mp4 filter=lfs diff=lfs merge=lfs -text
        *.webm filter=lfs diff=lfs merge=lfs -text
        *.otf filter=lfs diff=lfs merge=lfs -text
        *.ttf filter=lfs diff=lfs merge=lfs -text
        *.unitypackage filter=lfs diff=lfs merge=lfs -text
        """;
    }

    private static string RenderGodotAttributes()
    {
        return """

        # Godot text resources remain readable and mergeable.
        *.cfg text eol=lf
        *.gd text eol=lf
        *.gdextension text eol=lf
        *.gdshader text eol=lf
        *.godot text eol=lf
        *.tscn text eol=lf
        *.tres text eol=lf

        # Git LFS for source assets and Godot binary resources.
        *.3ds filter=lfs diff=lfs merge=lfs -text
        *.blend filter=lfs diff=lfs merge=lfs -text
        *.dxf filter=lfs diff=lfs merge=lfs -text
        *.fbx filter=lfs diff=lfs merge=lfs -text
        *.gltf filter=lfs diff=lfs merge=lfs -text
        *.glb filter=lfs diff=lfs merge=lfs -text
        *.obj filter=lfs diff=lfs merge=lfs -text
        *.dds filter=lfs diff=lfs merge=lfs -text
        *.exr filter=lfs diff=lfs merge=lfs -text
        *.gif filter=lfs diff=lfs merge=lfs -text
        *.hdr filter=lfs diff=lfs merge=lfs -text
        *.jpeg filter=lfs diff=lfs merge=lfs -text
        *.jpg filter=lfs diff=lfs merge=lfs -text
        *.png filter=lfs diff=lfs merge=lfs -text
        *.tga filter=lfs diff=lfs merge=lfs -text
        *.webp filter=lfs diff=lfs merge=lfs -text
        *.mp3 filter=lfs diff=lfs merge=lfs -text
        *.ogg filter=lfs diff=lfs merge=lfs -text
        *.wav filter=lfs diff=lfs merge=lfs -text
        *.otf filter=lfs diff=lfs merge=lfs -text
        *.ttf filter=lfs diff=lfs merge=lfs -text
        *.anim filter=lfs diff=lfs merge=lfs -text
        *.lmbake filter=lfs diff=lfs merge=lfs -text
        *.material filter=lfs diff=lfs merge=lfs -text
        *.mesh filter=lfs diff=lfs merge=lfs -text
        *.res filter=lfs diff=lfs merge=lfs -text
        *.scn filter=lfs diff=lfs merge=lfs -text
        """;
    }
}
