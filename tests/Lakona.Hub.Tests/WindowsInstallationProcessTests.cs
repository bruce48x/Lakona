using Lakona.Hub.Installation;
using Lakona.Hub.Updater;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class WindowsInstallationProcessTests
{
    [Fact]
    public async Task Legacy_installation_in_process_snapshot_does_not_block_isolated_recovery()
    {
        var paths = new InstallationPaths(Path.Combine(Path.GetTempPath(), "lakona-install-scope-" + Guid.NewGuid().ToString("N")));
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lakona Hub", "Lakona.Hub.exe");
        var host = new WindowsInstallationHost(paths, () => [legacy]);
        host.EnsureClosed();
        await host.StopFailedApplicationAsync();

        var runningTarget = new WindowsInstallationHost(paths, () => [legacy, paths.Executable.ToUpperInvariant()]);
        Assert.Throws<IOException>(runningTarget.EnsureClosed);
        await Assert.ThrowsAsync<IOException>(runningTarget.StopFailedApplicationAsync);
    }

    [Fact]
    public async Task Recovery_only_stops_its_own_child_and_leaves_other_installations_running()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "lakona-install-isolation-" + Guid.NewGuid().ToString("N"));
        var currentPaths = new InstallationPaths(Path.Combine(root, "current"));
        var otherPaths = new InstallationPaths(Path.Combine(root, "other"));
        var current = new WindowsInstallationHost(currentPaths);
        var other = new WindowsInstallationHost(otherPaths);
        try
        {
            DeployFixture(currentPaths, "healthy");
            DeployFixture(otherPaths, "healthy");
            Assert.True(await other.LaunchAndConfirmAsync(otherPaths.Executable, "1.0.0", new string('a', 64)));
            current.EnsureClosed();
            await current.StopFailedApplicationAsync();
            Assert.Throws<IOException>(other.EnsureClosed);

            Assert.True(await current.LaunchAndConfirmAsync(currentPaths.Executable, "1.0.0", new string('b', 64)));
            Assert.Throws<IOException>(current.EnsureClosed);
            await current.StopFailedApplicationAsync();
            current.EnsureClosed();
            Assert.Throws<IOException>(other.EnsureClosed);

            var removed = false;
            await Assert.ThrowsAsync<IOException>(() => WindowsInstallationHost.RemoveLegacyMsiAsync(
                () => "fixture-product", _ => { removed = true; return Task.FromResult(0); },
                () => throw new InvalidOperationException("Unexpected registration"), other.EnsureClosed));
            Assert.False(removed);
            Assert.Throws<IOException>(other.EnsureClosed);
        }
        finally
        {
            try { await current.StopFailedApplicationAsync(); }
            finally
            {
                await other.StopFailedApplicationAsync();
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Recovery_waits_for_an_exited_applications_remaining_file_lease()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "lakona-install-lease-" + Guid.NewGuid().ToString("N"));
        var paths = new InstallationPaths(root);
        Directory.CreateDirectory(paths.Application);
        try
        {
            File.WriteAllText(paths.Executable, "fixture");
            var host = new WindowsInstallationHost(paths);
            Task stopping;
            using (var lease = File.OpenRead(paths.Executable))
            {
                stopping = host.StopFailedApplicationAsync();
                Assert.False(stopping.IsCompleted);
            }
            await stopping.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            File.Delete(paths.Executable);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("healthy", true)]
    [InlineData("wrong-token", false)]
    [InlineData("exit", false)]
    public async Task Startup_confirmation_requires_a_live_child_with_matching_version_and_token(string mode, bool expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "lakona-install-process-" + Guid.NewGuid().ToString("N"));
        var paths = new InstallationPaths(root);
        var host = new WindowsInstallationHost(paths);
        try
        {
            DeployFixture(paths, mode);
            Assert.Equal(expected, await host.LaunchAndConfirmAsync(paths.Executable, "1.0.0", new string('a', 64)));
        }
        finally
        {
            await host.StopFailedApplicationAsync();
            Directory.Delete(root, true);
        }
    }

    private static void DeployFixture(InstallationPaths paths, string mode)
    {
        paths.Prepare();
        Directory.CreateDirectory(paths.Application);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "Lakona.Hub.Installation.Fixture*"))
            File.Copy(file, Path.Combine(paths.Application, Path.GetFileName(file)));
        File.Copy(Path.Combine(paths.Application, "Lakona.Hub.Installation.Fixture.exe"), paths.Executable);
        File.WriteAllText(Path.Combine(paths.Application, "fixture-mode.txt"), mode);
    }
}
