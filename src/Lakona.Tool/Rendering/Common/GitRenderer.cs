using Lakona.Tool.Domain;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Rendering.Common;

internal sealed class GitRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile(".gitignore", RenderGitIgnore(spec.ClientEngine is ClientEngine.Unity or ClientEngine.UnityCn or ClientEngine.Tuanjie), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile(".gitattributes", RenderGitAttributes(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderGitIgnore(bool isUnity)
    {
        var lines = new List<string>
        {
            "**/bin/",
            "**/obj/",
            "/_artifacts/",
            ".vs/"
        };

        if (isUnity)
        {
            lines.Add("/Client/[Ll]ibrary/");
            lines.Add("/Client/[Tt]emp/");
            lines.Add("/Client/[Oo]bj/");
            lines.Add("/Client/[Bb]uild/");
            lines.Add("/Client/Assets/Packages/");
        }
        else
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
