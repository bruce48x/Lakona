using System.Diagnostics;
using Lakona.Hub.Installation;
using Lakona.Hub.Updater;
using Microsoft.Win32;
using Xunit;

namespace Lakona.Hub.Tests;

// Opt-in: this test installs into the current Windows account and installs/removes
// the historical machine-wide MSI. Run only on an otherwise uninstalled test host.
public sealed class WindowsInstallerAcceptanceTests(ITestOutputHelper output)
{
    public static bool Enabled => OperatingSystem.IsWindows() &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LAKONA_HUB_ACCEPTANCE_PACKAGE"));

    [Fact(Skip = "Requires a packaged Windows release and a clean installation scope.", SkipUnless = nameof(Enabled))]
    public async Task Native_install_repair_uninstall_and_legacy_migration()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var archive = Path.GetFullPath(Environment.GetEnvironmentVariable("LAKONA_HUB_ACCEPTANCE_PACKAGE")!);
        var msi = Path.GetFullPath(Environment.GetEnvironmentVariable("LAKONA_HUB_ACCEPTANCE_MSI")
            ?? throw new InvalidOperationException("LAKONA_HUB_ACCEPTANCE_MSI is required."));
        Assert.True(File.Exists(archive), $"LAKONA_HUB_ACCEPTANCE_PACKAGE file does not exist: {archive}");
        Assert.True(File.Exists(msi), $"LAKONA_HUB_ACCEPTANCE_MSI file does not exist: {msi}");
        var paths = InstallationPaths.Current;
        Assert.False(Directory.Exists(paths.Application), "Refusing to replace an existing user installation.");
        Assert.False(WindowsInstallationHost.HasLegacyMsi(), "Refusing to change an existing MSI installation.");
        var host = new WindowsInstallationHost(paths);
        host.EnsureClosed();
        var package = SignedPackage.Release();
        var transaction = new InstallationTransaction(paths, package, host);
        var marker = Path.Combine(Path.GetDirectoryName(paths.State)!, $"acceptance-{Guid.NewGuid():N}.txt");
        paths.Prepare();
        File.WriteAllText(marker, "user-owned data");
        try
        {
            await transaction.InstallAsync(archive, repair: false, cancellationToken: TestContext.Current.CancellationToken);
            output.WriteLine("Installed native payload.");
            VerifyInstalled();
            await host.StopFailedApplicationAsync();
            // Repair actually replaces the installed native payload and starts Hub again.
            File.AppendAllText(paths.Executable, "damage");
            output.WriteLine("Stopped Hub and damaged executable for repair.");
            await transaction.InstallAsync(archive, repair: true, cancellationToken: TestContext.Current.CancellationToken);
            VerifyInstalled();
            await host.StopFailedApplicationAsync();
            await transaction.UninstallAsync();
            Assert.False(Directory.Exists(paths.Application));
            Assert.False(File.Exists(Shortcut()));
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lakona.Hub.User"))
                Assert.Null(key);
            Assert.Equal("user-owned data", File.ReadAllText(marker));

            var start = new ProcessStartInfo("msiexec.exe") { UseShellExecute = true };
            foreach (var argument in new[] { "/i", msi, "/passive", "/norestart" }) start.ArgumentList.Add(argument);
            using var installer = Process.Start(start) ?? throw new IOException("MSI could not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            await installer.WaitForExitAsync(timeout.Token);
            Assert.Contains(installer.ExitCode, new[] { 0, 3010 });
            Assert.True(WindowsInstallationHost.HasLegacyMsi());
            await transaction.InstallAsync(archive, repair: false, cancellationToken: TestContext.Current.CancellationToken);
            VerifyInstalled();
            Assert.True(WindowsInstallationHost.HasLegacyMsi());
            Assert.True(await host.RemoveLegacyMsiAsync());
            Assert.False(WindowsInstallationHost.HasLegacyMsi());
            VerifyInstalled();
        }
        catch (Exception ex)
        {
            output.WriteLine(ex.ToString());
            throw;
        }
        finally
        {
            await host.StopFailedApplicationAsync();
            // Only registrations created after the initial empty-scope assertions
            // are removed. Remove MSI before the user copy because migration reads it.
            if (WindowsInstallationHost.HasLegacyMsi()) await host.RemoveLegacyMsiAsync();
            if (Directory.Exists(paths.Application)) await transaction.UninstallAsync();
            File.Delete(marker);
        }

        void VerifyInstalled()
        {
            var manifest = package.VerifyDirectory(paths.Application);
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lakona.Hub.User");
            Assert.Equal(manifest.Version, key?.GetValue("DisplayVersion"));
            Assert.Equal(paths.Application, key?.GetValue("InstallLocation"));
            Assert.True(File.Exists(Shortcut()));
            Assert.False(File.Exists(paths.Journal));
            Assert.False(Directory.Exists(paths.Backup));
            Assert.Equal("user-owned data", File.ReadAllText(marker));
        }
    }

    private static string Shortcut() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lakona Hub.lnk");
}
