using System.Diagnostics;
using Lakona.ProjectSystem;

namespace Lakona.Hub.Applications;

internal sealed record ApplicationLaunchPlan(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

internal static class ApplicationLaunchPlanner
{
    public static ApplicationLaunchPlan OpenServer(
        string projectRoot,
        LocalApplicationInstallation editor)
    {
        var root = ResolveProjectRoot(projectRoot);
        return OpenServerDirectory(Path.Combine(root, "Server"), editor);
    }

    public static ApplicationLaunchPlan OpenServerDirectory(
        string serverDirectory,
        LocalApplicationInstallation editor)
    {
        serverDirectory = ResolveDirectory(serverDirectory, "Server");
        var solutionPath = Path.Combine(serverDirectory, "Server.slnx");
        var target = File.Exists(solutionPath) ? solutionPath : serverDirectory;
        return OpenCodeProject(editor, serverDirectory, target);
    }

    public static ApplicationLaunchPlan OpenClient(
        string projectRoot,
        LakonaProjectClient client,
        LocalApplicationInstallation application)
    {
        var root = ResolveProjectRoot(projectRoot);
        return OpenClientDirectory(Path.Combine(root, "Client"), client, application);
    }

    public static ApplicationLaunchPlan OpenClientDirectory(
        string clientDirectory,
        LakonaProjectClient client,
        LocalApplicationInstallation application)
    {
        clientDirectory = ResolveDirectory(clientDirectory, "Client");

        var arguments = (client, application.Kind) switch
        {
            (LakonaProjectClient.Unity, LocalApplicationKind.Unity) => new[] { "-projectPath", clientDirectory },
            (LakonaProjectClient.Tuanjie, LocalApplicationKind.Tuanjie) => new[] { "-projectPath", clientDirectory },
            (LakonaProjectClient.Godot, LocalApplicationKind.Godot) => new[] { "--editor", "--path", clientDirectory },
            (LakonaProjectClient.Console, LocalApplicationKind.Rider) or
            (LakonaProjectClient.Console, LocalApplicationKind.VisualStudio) or
            (LakonaProjectClient.Console, LocalApplicationKind.VisualStudioCode) or
            (LakonaProjectClient.Console, LocalApplicationKind.Other) => null,
            _ => throw new ArgumentException("The selected application does not match the project client.", nameof(application))
        };

        if (arguments is null)
        {
            var projectPath = Path.Combine(clientDirectory, "Client.csproj");
            var target = File.Exists(projectPath) ? projectPath : clientDirectory;
            return OpenCodeProject(application, clientDirectory, target);
        }

        return Build(application, clientDirectory, arguments);
    }

    private static ApplicationLaunchPlan OpenCodeProject(
        LocalApplicationInstallation editor,
        string workingDirectory,
        string target)
    {
        var arguments = editor.Kind switch
        {
            LocalApplicationKind.Rider => new[] { target },
            LocalApplicationKind.VisualStudio => new[] { target },
            LocalApplicationKind.VisualStudioCode => new[] { "--reuse-window", workingDirectory },
            LocalApplicationKind.Other => new[] { target },
            _ => throw new ArgumentException("The selected application is not a supported code editor.", nameof(editor))
        };

        return Build(editor, workingDirectory, arguments);
    }

    private static ApplicationLaunchPlan Build(
        LocalApplicationInstallation application,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        if (!Path.IsPathFullyQualified(application.ExecutablePath) || !File.Exists(application.ExecutablePath))
        {
            throw new FileNotFoundException("Application executable was not found.", application.ExecutablePath);
        }

        return new ApplicationLaunchPlan(application.ExecutablePath, workingDirectory, arguments);
    }

    private static string ResolveProjectRoot(string projectRoot)
    {
        var root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Project directory does not exist: {root}");
        }

        return root;
    }

    private static string ResolveDirectory(string directory, string displayName)
    {
        var fullPath = Path.GetFullPath(directory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"{displayName} directory does not exist: {fullPath}");
        }

        return fullPath;
    }
}

internal sealed class ApplicationLauncher
{
    public void Launch(ApplicationLaunchPlan plan)
    {
        var startInfo = new ProcessStartInfo(plan.ExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = plan.WorkingDirectory
        };
        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("The application process could not be started.");
        }
    }
}
