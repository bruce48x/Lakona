using System.Diagnostics;
using System.Reflection;
using Lakona.Hub.Installation;

namespace Lakona.Hub.Updates;

internal static class HubStartupConfirmation
{
    private static string? token;
    public static string[] Capture(string[] args)
    {
        var index = Array.IndexOf(args, "--hub-startup-token");
        if (index < 0) return args;
        if (index + 1 >= args.Length || args[index + 1].Length != 64 || !args[index + 1].All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid installation startup token.");
        token = args[index + 1];
        return args.Where((_, position) => position != index && position != index + 1).ToArray();
    }

    public static bool StartRecovery()
    {
        var paths = InstallationPaths.Current;
        if (token is not null || !OperatingSystem.IsWindows() || !paths.Owns(AppContext.BaseDirectory) || !File.Exists(paths.Journal)) return false;
        var start = new ProcessStartInfo(paths.Updater) { UseShellExecute = false, WorkingDirectory = paths.Application };
        foreach (var value in new[] { "--recover", "--parent", Environment.ProcessId.ToString(), "--parent-start", Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString() }) start.ArgumentList.Add(value);
        Process.Start(start)?.Dispose();
        return true;
    }

    public static void Confirm()
    {
        if (token is null) return;
        var version = Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        InstallationTransaction.ConfirmStartup(token, version);
        token = null;
    }
}
