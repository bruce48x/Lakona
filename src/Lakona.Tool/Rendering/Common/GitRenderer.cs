using Lakona.Tool.Domain;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Rendering.Common;

internal sealed class GitRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        var isUnity = spec.ClientEngine is ClientEngine.Unity or ClientEngine.Tuanjie;
        var isGodot = spec.ClientEngine is ClientEngine.Godot;
        builder.AddFile(".gitignore", RenderGitIgnore(isUnity, isGodot), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile(".gitattributes", RenderGitAttributes(), FileWriteMode.Replace, GeneratedFileKind.Text);
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

    private static string RenderGitAttributes()
    {
        return """
        * text=auto
        *.cs text eol=lf
        *.json text eol=lf
        *.csproj text eol=lf
        *.slnx text eol=lf
        """;
    }
}
