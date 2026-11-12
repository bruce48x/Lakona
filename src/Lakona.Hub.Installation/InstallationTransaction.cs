using System.Security.Cryptography;
using System.Text.Json;

namespace Lakona.Hub.Installation;

public interface IInstallationHost
{
    Task<bool> LaunchAndConfirmAsync(string executable, string version, string token);
    Task StopFailedApplicationAsync();
    void Register(string version);
    void Unregister();
    void LaunchPrevious(string executable);
}

public sealed class InstallationTransaction(InstallationPaths paths, SignedPackage package, IInstallationHost host)
{
    public async Task InstallAsync(string archive, bool repair, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        using var operation = paths.AcquireLock();
        var interrupted = File.Exists(paths.Journal);
        await RecoverCoreAsync(progress);
        if (interrupted) throw new IOException("The interrupted installation was recovered. Close Hub and run setup again to continue.");
        paths.RemoveTree(paths.Staging);
        progress?.Report("verifying");
        PackageManifest target;
        try { target = package.Extract(archive, paths.Staging, cancellationToken); }
        catch { paths.RemoveTree(paths.Staging); throw; }
        var hadPrevious = Directory.Exists(paths.Application);
        if (hadPrevious)
        {
            // Repair may replace damaged files, but still requires signed ownership.
            // If startup then fails and the backup is damaged, recovery preserves both
            // copies for offline repair instead of deleting the only complete payload.
            var previous = repair ? package.ReadInstalledManifest(paths.Application) : package.VerifyDirectory(paths.Application);
            var comparison = Version.Parse(target.Version).CompareTo(Version.Parse(previous.Version));
            if (comparison < 0 || (comparison == 0 && !repair))
                throw new InvalidOperationException("This package is not newer. Use Repair for the installed version.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var journal = new InstallationJournal("prepared", target.Version, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), hadPrevious);
        WriteJournal(journal);
        try
        {
            progress?.Report("installing");
            paths.RemoveTree(paths.Backup);
            if (hadPrevious) Directory.Move(paths.Application, paths.Backup);
            WriteJournal(journal with { Stage = "backed-up" });
            Directory.Move(paths.Staging, paths.Application);
            WriteJournal(journal with { Stage = "activated" });
            progress?.Report("launching");
            if (!await host.LaunchAndConfirmAsync(paths.Executable, target.Version, journal.Token))
                throw new IOException("The new Hub did not confirm a successful startup.");
            // The commit record precedes ancillary registration and cleanup. Recovery
            // finishes these idempotent operations rather than rolling back a healthy app.
            WriteJournal(journal with { Stage = "confirmed" });
            host.Register(target.Version);
            paths.RemoveTree(paths.Backup);
            File.Delete(paths.Journal);
            progress?.Report("completed");
        }
        catch
        {
            await RecoverCoreAsync(progress);
            throw;
        }
    }

    public async Task RecoverAsync(IProgress<string>? progress = null)
    {
        using var operation = paths.AcquireLock();
        await RecoverCoreAsync(progress);
    }

    private async Task RecoverCoreAsync(IProgress<string>? progress)
    {
        if (!File.Exists(paths.Journal))
        {
            if (Directory.Exists(paths.Backup))
                throw new IOException("An unrecognized backup exists. Preserve it and repair the installation.");
            return;
        }
        var journal = ReadJournal(paths);
        if (journal.Stage == "confirmed")
        {
            var current = package.VerifyDirectory(paths.Application);
            if (current.Version != journal.Version) throw new InvalidDataException("The confirmed installation version is inconsistent.");
            host.Register(current.Version);
            paths.RemoveTree(paths.Backup);
        }
        else if (journal.Stage == "uninstalling")
        {
            paths.RemoveTree(paths.Application);
            host.Unregister();
        }
        else if (journal.Stage is "prepared" or "backed-up" or "activated" or "restoring")
        {
            progress?.Report("recovering");
            await host.StopFailedApplicationAsync();
            if (Directory.Exists(paths.Backup))
            {
                var previous = package.VerifyDirectory(paths.Backup);
                WriteJournal(journal with { Stage = "restoring" });
                paths.RemoveTree(paths.Application);
                Directory.Move(paths.Backup, paths.Application);
                host.Register(previous.Version);
            }
            else if (journal.HadPrevious)
            {
                // A crash before backup, or after restoring its directory, leaves the
                // previous version at the stable path. Validate before trusting it.
                var previous = package.VerifyDirectory(paths.Application);
                if (journal.Stage != "prepared" && journal.Stage != "restoring")
                    throw new IOException("The recovery backup is missing. The installation was preserved.");
                host.Register(previous.Version);
            }
            else
            {
                paths.RemoveTree(paths.Application);
                host.Unregister();
            }
            paths.RemoveTree(paths.Staging);
            File.Delete(paths.Journal);
            if (journal.HadPrevious) host.LaunchPrevious(paths.Executable);
            return;
        }
        else throw new InvalidDataException("The installation journal has an unsupported stage.");
        File.Delete(paths.Journal);
    }

    public async Task UninstallAsync()
    {
        using var operation = paths.AcquireLock();
        var interrupted = File.Exists(paths.Journal);
        await RecoverCoreAsync(null);
        if (interrupted) throw new IOException("The interrupted installation was recovered. Close Hub before uninstalling.");
        // Ownership is established by a signed manifest, even if a payload file is
        // damaged. User data is outside this exact application root and is retained.
        package.ReadInstalledManifest(paths.Application);
        WriteJournal(new InstallationJournal("uninstalling", "0.0.0", "", false));
        paths.RemoveTree(paths.Application);
        host.Unregister();
        paths.RemoveTree(paths.Staging);
        File.Delete(paths.Journal);
    }

    public static InstallationJournal ReadJournal(InstallationPaths paths)
    {
        InstallationPaths.RejectLinks(paths.Journal);
        if (new FileInfo(paths.Journal).Length > 4096) throw new InvalidDataException("The installation journal is invalid.");
        return JsonSerializer.Deserialize(File.ReadAllText(paths.Journal), InstallationJson.Default.InstallationJournal)
            ?? throw new InvalidDataException("The installation journal is empty.");
    }

    private void WriteJournal(InstallationJournal journal)
    {
        WriteAtomic(paths.Journal, JsonSerializer.SerializeToUtf8Bytes(journal, InstallationJson.Default.InstallationJournal));
        InstallationLog.Write(paths, journal.Stage);
    }

    internal static void WriteAtomic(string path, byte[] bytes)
    {
        InstallationPaths.RejectLinks(path);
        var temporary = path + ".tmp";
        InstallationPaths.RejectLinks(temporary);
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    public static void ConfirmStartup(string token, string version)
    {
        var paths = InstallationPaths.Current;
        if (!OperatingSystem.IsWindows() || !paths.Owns(AppContext.BaseDirectory))
            throw new InvalidOperationException("Startup confirmation requires the per-user installation.");
        var journal = ReadJournal(paths);
        if (journal.Stage != "activated" || journal.Version != version || journal.Token != token || token.Length != 64)
            throw new InvalidDataException("The startup confirmation does not match the pending update.");
        WriteAtomic(paths.Health, JsonSerializer.SerializeToUtf8Bytes(new HealthConfirmation(version, token), InstallationJson.Default.HealthConfirmation));
    }
}
