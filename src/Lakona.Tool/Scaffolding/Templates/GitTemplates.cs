internal static class GitTemplates
{
    public static string RenderGitIgnore(bool isUnityCompatible)
    {
        var unitySection = isUnityCompatible
            ? """

# Unity generated project/IDE files
/Client/*.csproj
/Client/*.sln
/Client/*.slnx
/Client/*.unityproj
/Client/*.pidb
/Client/*.booproj
/Client/*.svd
/Client/*.pdb
/Client/*.mdb
/Client/*.opendb
/Client/*.VC.db
"""
            : "";

        return $$"""
# OS / Editor
.DS_Store
Thumbs.db
.idea/
.vs/
*.suo
*.user
*.userprefs
*.DotSettings.user

# .NET build outputs
**/bin/
**/obj/
/_artifacts/
/Client/.godot/

# Unity generated folders
/Client/[Ll]ibrary/
/Client/[Tt]emp/
/Client/[Ll]ogs/
/Client/[Uu]ser[Ss]ettings/
/Client/[Oo]bj/
/Client/[Bb]uild/
/Client/[Bb]uilds/
/Client/[Mm]emoryCaptures/
/Client/[Rr]ecordings/
{{unitySection}}

# NuGetForUnity restored packages
/Client/Assets/Packages/

# Godot generated files
/Client/.mono/
/Client/export_presets.cfg
/Client/*.sln

# Logs
*.log
""";
    }

    public static string RenderGitAttributes(bool isGodotOrConsole)
    {
        if (!isGodotOrConsole)
            return string.Empty;

        return """
* text=auto eol=lf

*.cs text eol=lf
*.csproj text eol=lf
*.sln text eol=lf
*.slnx text eol=lf
*.props text eol=lf
*.targets text eol=lf

*.gd text eol=lf
*.tscn text eol=lf
*.tres text eol=lf
*.shader text eol=lf

*.json text eol=lf
*.md text eol=lf
*.yml text eol=lf
*.yaml text eol=lf
*.xml text eol=lf

# Git LFS: commit-worthy binary assets and distributables
*.png filter=lfs diff=lfs merge=lfs -text
*.jpg filter=lfs diff=lfs merge=lfs -text
*.jpeg filter=lfs diff=lfs merge=lfs -text
*.gif filter=lfs diff=lfs merge=lfs -text
*.webp filter=lfs diff=lfs merge=lfs -text
*.ico filter=lfs diff=lfs merge=lfs -text
*.mp3 filter=lfs diff=lfs merge=lfs -text
*.wav filter=lfs diff=lfs merge=lfs -text
*.ogg filter=lfs diff=lfs merge=lfs -text
*.ttf filter=lfs diff=lfs merge=lfs -text
*.otf filter=lfs diff=lfs merge=lfs -text
*.zip filter=lfs diff=lfs merge=lfs -text
*.nupkg filter=lfs diff=lfs merge=lfs -text

# Non-text binaries that should never be line-normalized
*.dll binary
*.exe binary
*.pdb binary
*.so binary
*.dylib binary
*.bin binary
""";
    }
}
