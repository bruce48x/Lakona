using Lakona.Hub.Installation;
using Lakona.Hub.Updater;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class WindowsInstallationProcessTests
{
    [Theory]
    [InlineData("healthy", true)]
    [InlineData("wrong-token", false)]
    [InlineData("exit", false)]
    public async Task Startup_confirmation_requires_a_live_child_with_matching_version_and_token(string mode, bool expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "lakona-install-process-" + Guid.NewGuid().ToString("N"));
        var paths = new InstallationPaths(root);
        paths.Prepare();
        Directory.CreateDirectory(paths.Application);
        var host = new WindowsInstallationHost(paths);
        try
        {
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "Lakona.Hub.Installation.Fixture*"))
                File.Copy(file, Path.Combine(paths.Application, Path.GetFileName(file)));
            File.Copy(Path.Combine(paths.Application, "Lakona.Hub.Installation.Fixture.exe"), paths.Executable);
            File.WriteAllText(Path.Combine(paths.Application, "fixture-mode.txt"), mode);
            Assert.Equal(expected, await host.LaunchAndConfirmAsync(paths.Executable, "1.0.0", new string('a', 64)));
        }
        finally
        {
            await host.StopFailedApplicationAsync();
            Directory.Delete(root, true);
        }
    }
}
