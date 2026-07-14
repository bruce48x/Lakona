namespace Lakona.Hub.Updates;

internal static class HubUpdateStartup
{
    private static string? backupDirectory;
    private static string? readySignalPath;
    private static string? failureMessage;

    public static string[] Capture(string[] args)
    {
        if (args.Length == 3 && args[0] == "--complete-update")
        {
            backupDirectory = args[1];
            readySignalPath = args[2];
            return [];
        }

        if (args.Length == 2 && args[0] == "--update-failed")
        {
            var updateRoot = Path.GetFullPath(HubInstallation.UpdateRoot()) + Path.DirectorySeparatorChar;
            var failurePath = Path.GetFullPath(args[1]);
            if (failurePath.StartsWith(updateRoot, HubFileSystem.PathComparison) && File.Exists(failurePath))
            {
                failureMessage = File.ReadAllText(failurePath);
                File.Delete(failurePath);
            }

            return [];
        }

        return args;
    }

    public static string? TakeFailureMessage()
    {
        var message = failureMessage;
        failureMessage = null;
        return message;
    }

    public static void Complete()
    {
        if (backupDirectory is not { } backup ||
            readySignalPath is not { } readySignal ||
            !Directory.Exists(backup))
        {
            return;
        }

        var installDirectory = HubInstallation.CurrentDirectory();
        var expectedPrefix = $".{Path.GetFileName(installDirectory)}.previous-";
        var backupInfo = new DirectoryInfo(backup);
        if (!string.Equals(backupInfo.Parent?.FullName, Directory.GetParent(installDirectory)?.FullName, HubFileSystem.PathComparison) ||
            !backupInfo.Name.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var updateRoot = Path.GetFullPath(HubInstallation.UpdateRoot()) + Path.DirectorySeparatorChar;
        var signalPath = Path.GetFullPath(readySignal);
        if (!signalPath.StartsWith(updateRoot, HubFileSystem.PathComparison))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(signalPath)!);
        File.WriteAllText(signalPath, "ready");
    }
}
