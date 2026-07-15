using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lakona.Hub.Updates;

internal static class HubUpdateInstaller
{
    private const string PackageManifestName = "hub-package.json";
    private const string DeltaManifestName = "hub-delta.json";

    public static bool TryRun(string[] args)
    {
        if (args.Length != 2 || args[0] != "--apply-update")
        {
            return false;
        }

        try
        {
            ApplyAsync(args[1]).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            TryRestartPrevious(args[1], ex.Message);
        }

        return true;
    }

    internal static async Task ApplyAsync(string planPath)
    {
        var plan = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(planPath),
            HubJsonContext.Default.HubUpdateLaunchPlan)
            ?? throw new InvalidDataException("The update launch plan is empty.");
        WaitForParent(plan.ParentProcessId);

        var installDirectory = Path.GetFullPath(plan.InstallDirectory);
        var parentDirectory = Directory.GetParent(installDirectory)?.FullName
            ?? throw new InvalidOperationException("The installation directory has no parent.");
        var name = Path.GetFileName(installDirectory);
        var candidate = Path.Combine(parentDirectory, $".{name}.update-{Guid.NewGuid():N}");
        var backup = Path.Combine(parentDirectory, $".{name}.previous-{Guid.NewGuid():N}");
        Process? updatedProcess = null;
        var readySignal = Path.Combine(Path.GetDirectoryName(planPath)!, $"ready-{Guid.NewGuid():N}.signal");

        try
        {
            await PrepareCandidateAsync(plan, candidate);

            Directory.Move(installDirectory, backup);
            try
            {
                Directory.Move(candidate, installDirectory);
                updatedProcess = StartUpdatedApplication(installDirectory, plan.ExecutablePath, backup, readySignal);
                if (!WaitForReady(updatedProcess, readySignal))
                {
                    throw new InvalidOperationException("The updated Lakona Hub did not report a successful startup.");
                }

                updatedProcess.Dispose();
                updatedProcess = null;
                TryDeleteDirectory(backup);
            }
            catch
            {
                StopProcess(updatedProcess);
                if (Directory.Exists(installDirectory))
                {
                    Directory.Delete(installDirectory, recursive: true);
                }

                Directory.Move(backup, installDirectory);
                throw;
            }
        }
        finally
        {
            if (File.Exists(readySignal))
            {
                File.Delete(readySignal);
            }

            if (Directory.Exists(candidate))
            {
                Directory.Delete(candidate, recursive: true);
            }
        }
    }

    internal static async Task PrepareCandidateAsync(HubUpdateLaunchPlan plan, string candidate)
    {
        if (plan.IsDelta)
        {
            HubFileSystem.CopyDirectory(plan.InstallDirectory, candidate);
            await ApplyDeltaAsync(plan, candidate);
        }
        else
        {
            var unpacked = candidate + $".unpack-{Guid.NewGuid():N}";
            try
            {
                Directory.CreateDirectory(unpacked);
                HubFileSystem.ExtractZip(plan.ArchivePath, unpacked);
                var packageRoot = HubFileSystem.SafeDestination(unpacked, plan.PackageRoot);
                if (!Directory.Exists(packageRoot))
                {
                    throw new InvalidDataException($"The full update does not contain {plan.PackageRoot}.");
                }

                Directory.Move(packageRoot, candidate);
            }
            finally
            {
                if (Directory.Exists(unpacked))
                {
                    Directory.Delete(unpacked, recursive: true);
                }
            }
        }

        await VerifyCandidateAsync(candidate, plan.TargetVersion);
    }

    private static async Task ApplyDeltaAsync(HubUpdateLaunchPlan plan, string candidate)
    {
        var overlay = Path.Combine(Path.GetTempPath(), $"lakona-hub-delta-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(overlay);
            HubFileSystem.ExtractZip(plan.ArchivePath, overlay);
            var deltaPath = Path.Combine(overlay, DeltaManifestName);
            var delta = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(deltaPath),
                HubJsonContext.Default.HubDeltaManifest)
                ?? throw new InvalidDataException("The delta manifest is empty.");
            if (delta.SchemaVersion != HubReleaseManifest.CurrentSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported delta schema {delta.SchemaVersion}.");
            }
            if (!HubVersionComparer.Equals(delta.FromVersion, plan.CurrentVersion) ||
                !HubVersionComparer.Equals(delta.ToVersion, plan.TargetVersion))
            {
                throw new InvalidDataException("The delta manifest does not match the installed and target versions.");
            }

            foreach (var deletedFile in delta.DeletedFiles)
            {
                var destination = HubFileSystem.SafeDestination(candidate, deletedFile);
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }

            File.Delete(deltaPath);
            HubFileSystem.CopyDirectory(overlay, candidate, overwrite: true);
        }
        finally
        {
            if (Directory.Exists(overlay))
            {
                Directory.Delete(overlay, recursive: true);
            }
        }
    }

    private static async Task VerifyCandidateAsync(string candidate, string targetVersion)
    {
        var manifestPath = Path.Combine(candidate, PackageManifestName);
        var manifest = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(manifestPath),
            HubJsonContext.Default.HubPackageManifest)
            ?? throw new InvalidDataException("The package manifest is empty.");
        if (manifest.SchemaVersion != HubReleaseManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported package schema {manifest.SchemaVersion}.");
        }
        if (!HubVersionComparer.Equals(manifest.Version, targetVersion))
        {
            throw new InvalidDataException("The package manifest does not match the target version.");
        }

        foreach (var file in manifest.Files)
        {
            var path = HubFileSystem.SafeDestination(candidate, file.Path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Size)
            {
                throw new InvalidDataException($"Updated file validation failed: {file.Path}.");
            }

            await using var stream = info.OpenRead();
            var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Updated file checksum failed: {file.Path}.");
            }
        }

        foreach (var executableFile in manifest.ExecutableFiles)
        {
            HubFileSystem.MakeExecutable(HubFileSystem.SafeDestination(candidate, executableFile));
        }
    }

    private static void WaitForParent(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("Lakona Hub did not exit before the update timeout.");
            }
        }
        catch (ArgumentException)
        {
            // The parent exited before the updater opened its process handle.
        }
    }

    private static Process StartUpdatedApplication(
        string installDirectory,
        string executablePath,
        string backup,
        string readySignal)
    {
        var executable = HubFileSystem.SafeDestination(installDirectory, executablePath);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? installDirectory
        };
        startInfo.ArgumentList.Add("--complete-update");
        startInfo.ArgumentList.Add(backup);
        startInfo.ArgumentList.Add(readySignal);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the updated Lakona Hub.");
    }

    private static bool WaitForReady(Process process, string readySignal)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (File.Exists(readySignal))
            {
                return true;
            }

            if (process.HasExited)
            {
                return false;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    private static void StopProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        using (process)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Retain the rollback directory if the operating system still has a handle open.
        }
        catch (UnauthorizedAccessException)
        {
            // Retaining a validated previous version does not invalidate the update.
        }
    }

    private static void TryRestartPrevious(string planPath, string errorMessage)
    {
        try
        {
            var plan = JsonSerializer.Deserialize(
                File.ReadAllText(planPath),
                HubJsonContext.Default.HubUpdateLaunchPlan);
            if (plan is null || !Directory.Exists(plan.InstallDirectory))
            {
                return;
            }

            var executable = HubFileSystem.SafeDestination(plan.InstallDirectory, plan.ExecutablePath);
            if (!File.Exists(executable))
            {
                return;
            }

            var failurePath = Path.Combine(Path.GetDirectoryName(planPath)!, "last-update-error.txt");
            File.WriteAllText(failurePath, errorMessage);
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? plan.InstallDirectory
            };
            startInfo.ArgumentList.Add("--update-failed");
            startInfo.ArgumentList.Add(failurePath);
            _ = Process.Start(startInfo);
        }
        catch
        {
            // The previous installation remains on disk for manual recovery.
        }
    }
}

internal static class HubFileSystem
{
    public static void ExtractZip(string archivePath, string destination)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(SafeDestination(destination, entry.FullName));
                continue;
            }

            var path = SafeDestination(destination, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: true);
        }
    }

    public static void CopyDirectory(string source, string destination, bool overwrite = false)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }
    }

    public static string SafeDestination(string root, string archivePath)
    {
        var rootPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(rootPath, FromArchivePath(archivePath)));
        if (!destination.StartsWith(rootPath, PathComparison))
        {
            throw new InvalidDataException($"Update archive path escapes its destination: {archivePath}.");
        }

        return destination;
    }

    public static string FromArchivePath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);

    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    public static void EnsureDirectoryWritable(string directory)
    {
        var probe = Path.Combine(directory, $".lakona-hub-write-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probe);
        Directory.Delete(probe);
    }
}
