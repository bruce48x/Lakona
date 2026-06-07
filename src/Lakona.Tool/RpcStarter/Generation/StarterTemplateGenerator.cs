namespace Lakona.Tool.RpcStarter;

internal sealed class StarterTemplateGenerator
{
    private readonly Action<string, string> _runGit;

    public StarterTemplateGenerator(Action<string, string> runDotNet, Action<string, string> runGit)
        : this(runGit)
    {
        _ = runDotNet;
    }

    public StarterTemplateGenerator(Action<string, string> runGit)
    {
        _runGit = runGit;
    }

    public void GenerateTemplate(string rootPath, string projectName, ClientEngineKind clientEngine, TransportKind transport, SerializerKind serializer, ResolvedVersions versions)
    {
        GenerateTemplate(rootPath, projectName, clientEngine, transport, serializer, clientEngine.GetDefaultNuGetForUnitySource(), versions);
    }

    public void GenerateTemplate(string rootPath, string projectName, ClientEngineKind clientEngine, TransportKind transport, SerializerKind serializer, NuGetForUnitySourceKind nuGetForUnitySource, ResolvedVersions versions)
    {
        var context = CreateContext(rootPath, projectName, clientEngine, transport, serializer, nuGetForUnitySource, versions);

        GenerateGitIgnore(context);
        GenerateGitAttributes(context);
        StarterSharedTemplate.Generate(context);
        StarterServerTemplate.Generate(context);
        GenerateSolution(context.Paths.ServerRootPath);
        GenerateClientTemplate(context);
        InitializeGit(context.Paths.RootPath);
    }

    private static StarterTemplateContext CreateContext(
        string rootPath,
        string projectName,
        ClientEngineKind clientEngine,
        TransportKind transport,
        SerializerKind serializer,
        NuGetForUnitySourceKind nuGetForUnitySource,
        ResolvedVersions versions)
    {
        var paths = new StarterPaths(
            rootPath,
            Path.Combine(rootPath, "Shared"),
            Path.Combine(rootPath, "Server"),
            Path.Combine(rootPath, "Server", "Server"),
            Path.Combine(rootPath, "Client"));

        Directory.CreateDirectory(paths.SharedPath);
        Directory.CreateDirectory(paths.ServerRootPath);
        Directory.CreateDirectory(paths.ServerAppPath);
        Directory.CreateDirectory(paths.ClientPath);

        return new StarterTemplateContext(
            projectName,
            MakeCompanyId(projectName),
            clientEngine,
            transport,
            serializer,
            nuGetForUnitySource,
            versions,
            paths);
    }

    private static void GenerateClientTemplate(StarterTemplateContext context)
    {
        switch (context.ClientEngine)
        {
            case ClientEngineKind.Unity:
            case ClientEngineKind.UnityCn:
            case ClientEngineKind.Tuanjie:
                StarterUnityTemplate.Generate(context);
                return;
            case ClientEngineKind.Godot:
                StarterGodotTemplate.Generate(context);
                return;
            case ClientEngineKind.Console:
                StarterConsoleTemplate.Generate(context);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(context.ClientEngine), context.ClientEngine, null);
        }
    }

    private void InitializeGit(string rootPath)
    {
        _runGit(rootPath, "init");
    }

    private static void GenerateSolution(string serverPath)
    {
        var solutionPath = Path.Combine(serverPath, "Server.slnx");
        StarterFileWriter.Write(solutionPath, """
<Solution>
  <Project Path="../Shared/Shared.csproj" />
  <Project Path="Server/Server.csproj" />
</Solution>
""");
    }

    private static string MakeCompanyId(string projectName)
    {
        var filtered = new string(projectName.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(filtered) ? "lakona-rpc.sample" : $"lakona-rpc.{filtered.ToLowerInvariant()}";
    }

    private static void GenerateGitIgnore(StarterTemplateContext context)
    {
        var unityProjectFiles = context.ClientEngine.IsUnityCompatible()
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
            : string.Empty;

        var godotGeneratedFiles = context.ClientEngine == ClientEngineKind.Godot
            ? """

# Godot generated files
/Client/.mono/
/Client/export_presets.cfg
/Client/*.sln
"""
            : string.Empty;

        var gitIgnore = $$"""
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
{{unityProjectFiles}}

# NuGetForUnity restored packages
/Client/Assets/Packages/
{{godotGeneratedFiles}}

# Logs
*.log
""";

        StarterFileWriter.Write(Path.Combine(context.Paths.RootPath, ".gitignore"), gitIgnore);
    }

    private static void GenerateGitAttributes(StarterTemplateContext context)
    {
        if (context.ClientEngine is not (ClientEngineKind.Godot or ClientEngineKind.Console))
            return;

        var gitAttributes = """
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

        StarterFileWriter.Write(Path.Combine(context.Paths.RootPath, ".gitattributes"), gitAttributes);
    }
}
