namespace Lakona.Tool.RpcStarter;

internal static class StarterWorkspace
{
    public static string? FindProjectRoot(string startPath)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startPath));
        while (dir != null)
        {
            if (LooksLikeStarterProjectRoot(dir.FullName))
                return dir.FullName;

            dir = dir.Parent;
        }

        return null;
    }

    public static bool TryResolveProjectContext(string startPath, out StarterProjectContext context, out string error)
    {
        context = default!;
        error = string.Empty;

        var rootPath = FindProjectRoot(startPath);
        if (rootPath is null)
        {
            error = "Could not find a Lakona.Rpc starter project root. Expected Shared/, Server/Server/, and Client/ folders.";
            return false;
        }

        var sharedPath = Path.Combine(rootPath, "Shared");
        var serverAppPath = Path.Combine(rootPath, "Server", "Server");
        var clientPath = Path.Combine(rootPath, "Client");

        if (!Directory.Exists(sharedPath) || !Directory.Exists(serverAppPath) || !Directory.Exists(clientPath))
        {
            error = $"Invalid Lakona.Rpc starter project layout under '{rootPath}'.";
            return false;
        }

        if (!TryDetectClientEngine(clientPath, out var clientEngine))
        {
            error = $"Could not detect client engine from '{clientPath}'.";
            return false;
        }

        context = new StarterProjectContext(
            rootPath,
            sharedPath,
            serverAppPath,
            clientPath,
            clientEngine);
        return true;
    }

    private static bool LooksLikeStarterProjectRoot(string path) =>
        Directory.Exists(Path.Combine(path, "Shared")) &&
        Directory.Exists(Path.Combine(path, "Server", "Server")) &&
        Directory.Exists(Path.Combine(path, "Client"));

    private static bool TryDetectClientEngine(string clientPath, out ClientEngineKind clientEngine)
    {
        if (File.Exists(Path.Combine(clientPath, "project.godot")))
        {
            clientEngine = ClientEngineKind.Godot;
            return true;
        }

        var clientProjectPath = Path.Combine(clientPath, "Client.csproj");
        if (File.Exists(clientProjectPath))
        {
            var clientProject = File.ReadAllText(clientProjectPath);
            if (clientProject.Contains("<LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>", StringComparison.Ordinal) &&
                clientProject.Contains("<OutputType>Exe</OutputType>", StringComparison.Ordinal))
            {
                clientEngine = ClientEngineKind.Console;
                return true;
            }
        }

        var projectVersionPath = Path.Combine(clientPath, "ProjectSettings", "ProjectVersion.txt");
        if (File.Exists(projectVersionPath))
        {
            var projectVersion = File.ReadAllText(projectVersionPath);
            clientEngine = projectVersion.Contains("m_TuanjieEditorVersion:", StringComparison.Ordinal)
                ? ClientEngineKind.Tuanjie
                : ClientEngineKind.Unity;

            var manifestPath = Path.Combine(clientPath, "Packages", "manifest.json");
            if (clientEngine == ClientEngineKind.Unity && File.Exists(manifestPath))
            {
                var manifest = File.ReadAllText(manifestPath);
                if (!manifest.Contains("package.openupm.com", StringComparison.OrdinalIgnoreCase))
                    clientEngine = ClientEngineKind.UnityCn;
            }

            return true;
        }

        clientEngine = default;
        return false;
    }
}

internal sealed record StarterProjectContext(
    string RootPath,
    string SharedPath,
    string ServerAppPath,
    string ClientPath,
    ClientEngineKind ClientEngine);
