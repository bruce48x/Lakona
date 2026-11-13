using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lakona.Hub.Installation;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Lakona.Hub.Updater;

internal sealed partial class WindowsInstallationHost(InstallationPaths paths,
    Func<IEnumerable<string>>? runningHubExecutables = null) : IInstallationHost
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lakona.Hub.User";
    private Process? launched;

    public async Task<bool> LaunchAndConfirmAsync(string executable, string version, string token)
    {
        File.Delete(paths.Health);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = paths.Application };
        start.ArgumentList.Add("--hub-startup-token");
        start.ArgumentList.Add(token);
        launched = Process.Start(start) ?? throw new IOException("Hub could not start.");
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(45) && !launched.HasExited)
        {
            if (File.Exists(paths.Health))
            {
                if (new FileInfo(paths.Health).Length > 4096) return false;
                var health = JsonSerializer.Deserialize(File.ReadAllText(paths.Health), InstallationJson.Default.HealthConfirmation);
                if (health?.Version == version && health.Token == token) return true;
                return false;
            }
            await Task.Delay(100);
        }
        return false;
    }

    public async Task StopFailedApplicationAsync()
    {
        if (launched is { } process)
        {
            if (!process.HasExited)
            {
                process.Kill();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await process.WaitForExitAsync(timeout.Token);
            }
            process.Dispose();
            launched = null;
        }
        // On crash recovery a process may belong to the previous updater. Never
        // kill a reused PID or an unrelated Hub; require the user to close it.
        EnsureClosed();
        // Windows can signal process exit before its native image mappings are
        // released. Wait for actual file availability before rollback deletes them.
        var releaseWait = Stopwatch.StartNew();
        while (Directory.Exists(paths.Application))
        {
            try
            {
                InstallationPaths.RejectLinks(paths.Application);
                foreach (var file in Directory.EnumerateFiles(paths.Application, "*", SearchOption.AllDirectories))
                {
                    InstallationPaths.RejectLinks(file);
                    using var probe = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                break;
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33 && releaseWait.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(50);
            }
        }
    }

    public void EnsureClosed() => EnsureExecutableClosed(paths.Executable,
        (runningHubExecutables ?? EnumerateHubExecutables)());

    public static void EnsureLegacyClosed() => EnsureExecutableClosed(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lakona Hub", "Lakona.Hub.exe"),
        EnumerateHubExecutables());

    private static void EnsureExecutableClosed(string target, IEnumerable<string> executables)
    {
        if (executables.Any(executable => string.Equals(executable, target, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Close Lakona Hub before continuing.");
    }

    private static IEnumerable<string> EnumerateHubExecutables()
    {
        foreach (var process in Process.GetProcessesByName("Lakona.Hub"))
        {
            using (process)
            {
                var executable = process.HasExited ? null : process.MainModule?.FileName;
                if (executable is not null) yield return executable;
            }
        }
    }

    public void Register(string version)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "Lakona Hub");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "Lakona");
        key.SetValue("InstallLocation", paths.Application);
        key.SetValue("DisplayIcon", paths.Executable);
        key.SetValue("UninstallString", $"\"{paths.Updater}\" --uninstall");
        key.SetValue("NoModify", 1);
        key.SetValue("NoRepair", 1);
        var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lakona Hub.lnk");
        // Windows Script Host supplies the OS shortcut writer. This isolated,
        // hidden command has no user-provided code and works with NativeAOT.
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference='Stop'; $s=(New-Object -ComObject WScript.Shell).CreateShortcut(" + Quote(link) + "); $s.TargetPath=" + Quote(paths.Executable) + "; $s.WorkingDirectory=" + Quote(paths.Application) + "; $s.Save()";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("The Start menu shortcut could not be created.");
        if (!process.WaitForExit(15000)) { process.Kill(); throw new IOException("Start menu registration timed out."); }
        if (process.ExitCode != 0) throw new IOException("The Start menu shortcut could not be created.");
    }

    public void Unregister()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lakona Hub.lnk"));
    }

    public void LaunchPrevious(string executable) => Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = paths.Application })?.Dispose();

    public static bool HasLegacyMsi() => LegacyProduct() is not null;

    public Task<bool> RemoveLegacyMsiAsync() => RemoveLegacyMsiAsync(LegacyProduct, UninstallLegacyMsiAsync,
        () => Register(SignedPackage.Release().ReadInstalledManifest(paths.Application).Version), EnsureLegacyClosed);

    internal static async Task<bool> RemoveLegacyMsiAsync(Func<string?> findProduct, Func<string, Task<int>> uninstall, Action register,
        Action ensureLegacyClosed)
    {
        var product = findProduct();
        if (product is null) return true;
        ensureLegacyClosed();
        if (await uninstall(product) is not 0 and not 3010) return false;
        register();
        return findProduct() is null;
    }

    private static async Task<int> UninstallLegacyMsiAsync(string product)
    {
        var start = new ProcessStartInfo("msiexec.exe") { UseShellExecute = true };
        foreach (var argument in new[] { "/x", product, "/passive", "/norestart" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Windows Installer could not start.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static unsafe string? LegacyProduct()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var buffer = stackalloc char[39];
        var result = MsiEnumRelatedProductsW("{D781E216-AC16-4F40-A74B-51AB25034019}", 0, 0, buffer);
        if (result == 259) return null;
        if (result != 0) throw new IOException("Windows Installer registration could not be inspected.");
        return new string(buffer);
    }

    [LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint MsiEnumRelatedProductsW(string upgradeCode, uint reserved, uint index, char* productCode);
}
