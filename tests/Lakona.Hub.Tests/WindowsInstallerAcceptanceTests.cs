using Lakona.Hub.Installation;
using Lakona.Hub.Updater;
using Microsoft.Win32;
using Xunit;

namespace Lakona.Hub.Tests;

// Run only on a clean test host. This test installs into the current Windows account.
public sealed class WindowsInstallerAcceptanceTests(ITestOutputHelper output)
{
    public static bool Enabled => OperatingSystem.IsWindows() &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LAKONA_HUB_ACCEPTANCE_PACKAGE"));

    [Fact(Skip = "Requires a packaged Windows release and a clean installation scope.", SkipUnless = nameof(Enabled))]
    public async Task Native_install_repair_and_uninstall()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var archive = Path.GetFullPath(Environment.GetEnvironmentVariable("LAKONA_HUB_ACCEPTANCE_PACKAGE")!);
        Assert.True(File.Exists(archive), $"LAKONA_HUB_ACCEPTANCE_PACKAGE file does not exist: {archive}");
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
        }
        catch (Exception ex)
        {
            output.WriteLine(ex.ToString());
            throw;
        }
        finally
        {
            await host.StopFailedApplicationAsync();
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
