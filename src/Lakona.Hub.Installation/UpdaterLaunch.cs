using System.Diagnostics;

namespace Lakona.Hub.Installation;

public static class UpdaterLaunch
{
    public static async Task StartAsync(string archive, string expectedVersion, string language, string theme, CancellationToken cancellationToken)
    {
        var paths = InstallationPaths.Current;
        paths.Prepare();
        paths.CleanupCompletedWork();
        var handoff = Path.Combine(paths.State, "handoff-" + Guid.NewGuid().ToString("N"));
        var manifest = SignedPackage.Release().Extract(archive, handoff, cancellationToken);
        using var lease = paths.HoldWork(handoff);
        if (manifest.Version != expectedVersion) throw new InvalidDataException("The package does not match the requested update version.");
        var ready = Path.Combine(handoff, "ready");
        var start = new ProcessStartInfo(Path.Combine(handoff, "Lakona.Hub.Updater.exe")) { UseShellExecute = false, WorkingDirectory = handoff };
        foreach (var argument in new[] { "--package", Path.GetFullPath(archive), "--parent", Environment.ProcessId.ToString(), "--parent-start", Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(), "--language", language, "--theme", theme, "--ready", ready })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("The update window could not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            while (!File.Exists(ready))
            {
                if (process.HasExited) throw new IOException("The update window closed before accepting the update.");
                await Task.Delay(100, timeout.Token);
            }
        }
        catch
        {
            if (!process.HasExited) process.Kill();
            throw;
        }
    }
}
