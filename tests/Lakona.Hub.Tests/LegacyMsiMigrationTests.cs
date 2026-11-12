using Lakona.Hub.Updater;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class LegacyMsiMigrationTests
{
    [Theory]
    [InlineData(1602)] // User canceled Windows Installer.
    [InlineData(1603)] // Installation failure.
    public async Task Failed_removal_keeps_migration_pending_and_can_be_retried(int exitCode)
    {
        string? product = "legacy-product";
        var registrations = 0;
        Assert.False(await WindowsInstallationHost.RemoveLegacyMsiAsync(() => product,
            _ => Task.FromResult(exitCode), () => registrations++));
        Assert.Equal(0, registrations);
        Assert.True(await WindowsInstallationHost.RemoveLegacyMsiAsync(() => product,
            _ => { product = null; return Task.FromResult(0); }, () => registrations++));
        Assert.Equal(1, registrations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3010)]
    public async Task Successful_exit_does_not_claim_migration_complete_while_product_remains(int exitCode)
    {
        var registered = false;
        Assert.False(await WindowsInstallationHost.RemoveLegacyMsiAsync(() => "still-present",
            _ => Task.FromResult(exitCode), () => registered = true));
        Assert.True(registered);
    }
}
