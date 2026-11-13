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
            _ => Task.FromResult(exitCode), () => registrations++, () => { }));
        Assert.Equal(0, registrations);
        Assert.True(await WindowsInstallationHost.RemoveLegacyMsiAsync(() => product,
            _ => { product = null; return Task.FromResult(0); }, () => registrations++, () => { }));
        Assert.Equal(1, registrations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3010)]
    public async Task Successful_exit_does_not_claim_migration_complete_while_product_remains(int exitCode)
    {
        var registered = false;
        Assert.False(await WindowsInstallationHost.RemoveLegacyMsiAsync(() => "still-present",
            _ => Task.FromResult(exitCode), () => registered = true, () => { }));
        Assert.True(registered);
    }

    [Fact]
    public async Task Running_legacy_check_blocks_removal_and_registration()
    {
        var removed = false;
        var registered = false;
        await Assert.ThrowsAsync<IOException>(() => WindowsInstallationHost.RemoveLegacyMsiAsync(
            () => "legacy-product", _ => { removed = true; return Task.FromResult(0); },
            () => registered = true, () => throw new IOException("Legacy Hub is running.")));
        Assert.False(removed);
        Assert.False(registered);
    }

    [Fact]
    public async Task Missing_legacy_product_needs_no_process_check_or_removal()
    {
        Assert.True(await WindowsInstallationHost.RemoveLegacyMsiAsync(
            () => null, _ => throw new InvalidOperationException("Unexpected removal"),
            () => throw new InvalidOperationException("Unexpected registration"),
            () => throw new InvalidOperationException("Unexpected process check")));
    }
}
