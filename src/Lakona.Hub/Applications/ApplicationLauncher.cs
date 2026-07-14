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
        var serverDirectory = Path.Combine(root, "Server");
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
        var clientDirectory = Path.Combine(root, "Client");
        if (!Directory.Exists(clientDirectory))
        {
            throw new DirectoryNotFoundException($"Client directory does not exist: {clientDirectory}");
        }

        var arguments = (client, application.Kind) switch
        {
            (LakonaProjectClient.Unity, LocalApplicationKind.Unity) => new[] { "-projectPath", clientDirectory },
            (LakonaProjectClient.Godot, LocalApplicationKind.Godot) => new[] { "--editor", "--path", clientDirectory },
            (LakonaProjectClient.Console, LocalApplicationKind.Rider) or
            (LakonaProjectClient.Console, LocalApplicationKind.VisualStudio) or
            (LakonaProjectClient.Console, LocalApplicationKind.VisualStudioCode) => null,
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
