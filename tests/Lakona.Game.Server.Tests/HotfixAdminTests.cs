using System.Text.Json;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.HotfixAdmin;
using Lakona.Game.Server.Hotfix.BuildTag;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class HotfixAdminTests
{
    [Fact]
    public void Options_reject_non_loopback_binding_configuration()
    {
        var options = new HotfixAdminOptions { Host = "10.0.0.5" };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("loopback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Status_returns_current_pointer_and_loaded_version()
    {
        using var fixture = HotfixAdminFixture.Create();
        await fixture.Store.WritePointerAsync("current.txt", "v20260612-153045Z", TestContext.Current.CancellationToken);
        await fixture.Store.WritePointerAsync("previous.txt", "v20260612-145812Z", TestContext.Current.CancellationToken);
        var admin = fixture.CreateAdmin(new RecordingHotfixManager(HotfixReloadStatus.Succeeded, "loaded-v1"));

        var status = await admin.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal("v20260612-153045Z", status.CurrentPointerVersion);
        Assert.Equal("v20260612-145812Z", status.PreviousPointerVersion);
        Assert.Equal("loaded-v1", status.LoadedVersion);
        Assert.Equal(HotfixBuildTag.Get(typeof(HotfixAdminTests).Assembly), status.BuildTag);
    }

    [Fact]
    public async Task Activate_rejects_build_tag_mismatch()
    {
        using var fixture = HotfixAdminFixture.Create();
        await fixture.WriteVersionAsync("v20260612-153045Z", buildTag: "different");
        var admin = fixture.CreateAdmin(new RecordingHotfixManager(HotfixReloadStatus.Succeeded, "old"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await admin.ActivateAsync(
                new HotfixActivateRequest("v20260612-153045Z", null, "op"),
                TestContext.Current.CancellationToken));

        Assert.Contains("BuildTag", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activate_rejects_checksum_mismatch()
    {
        using var fixture = HotfixAdminFixture.Create();
        await fixture.WriteVersionAsync("v20260612-153045Z", buildTag: HotfixBuildTag.Get(typeof(HotfixAdminTests).Assembly));
        var versionDirectory = Path.Combine(fixture.Root, "versions", "v20260612-153045Z");
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "Server.Hotfix.dll"), "changed", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "checksums.sha256"), "000000 Server.Hotfix.dll", TestContext.Current.CancellationToken);
        var admin = fixture.CreateAdmin(new RecordingHotfixManager(HotfixReloadStatus.Succeeded, "old"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await admin.ActivateAsync(
                new HotfixActivateRequest("v20260612-153045Z", null, "op"),
                TestContext.Current.CancellationToken));

        Assert.Contains("Checksum mismatch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_activation_restores_current_pointer()
    {
        using var fixture = HotfixAdminFixture.Create();
        await fixture.Store.WritePointerAsync("current.txt", "old", TestContext.Current.CancellationToken);
        await fixture.WriteVersionAsync("next", buildTag: HotfixBuildTag.Get(typeof(HotfixAdminTests).Assembly));
        var admin = fixture.CreateAdmin(new RecordingHotfixManager(HotfixReloadStatus.Failed, "old"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await admin.ActivateAsync(
                new HotfixActivateRequest("next", null, "op"),
                TestContext.Current.CancellationToken));

        Assert.Contains("reload failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("old", await fixture.Store.ReadPointerAsync("current.txt", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Failed_dry_load_restores_current_pointer_without_advancing_previous()
    {
        using var fixture = HotfixAdminFixture.Create();
        await fixture.Store.WritePointerAsync("current.txt", "old", TestContext.Current.CancellationToken);
        await fixture.WriteVersionAsync("next", buildTag: HotfixBuildTag.Get(typeof(HotfixAdminTests).Assembly));
        var admin = fixture.CreateAdmin(new RecordingHotfixManager(
            HotfixReloadStatus.Succeeded,
            "old",
            HotfixReloadStatus.Failed));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await admin.ActivateAsync(
                new HotfixActivateRequest("next", null, "op"),
                TestContext.Current.CancellationToken));

        Assert.Contains("validation failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("old", await fixture.Store.ReadPointerAsync("current.txt", TestContext.Current.CancellationToken));
        Assert.Null(await fixture.Store.ReadPointerAsync("previous.txt", TestContext.Current.CancellationToken));
    }

    private sealed class HotfixAdminFixture : IDisposable
    {
        private HotfixAdminFixture(string root)
        {
            Root = root;
            Store = new HotfixVersionStore(root);
        }

        public string Root { get; }

        public HotfixVersionStore Store { get; }

        public static HotfixAdminFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "LakonaHotfixAdminTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new HotfixAdminFixture(root);
        }

        public HotfixAdminController CreateAdmin(IHotfixManager manager)
        {
            return new HotfixAdminController(
                new HotfixAdminOptions { HotfixRoot = Root, BuildTag = HotfixBuildTag.Get(typeof(HotfixAdminTests).Assembly) },
                Store,
                manager);
        }

        public async Task WriteVersionAsync(string version, string buildTag)
        {
            var versionDirectory = Path.Combine(Root, "versions", version);
            Directory.CreateDirectory(versionDirectory);
            var manifest = new HotfixPackageManifest(version, DateTimeOffset.UtcNow, "Server.Hotfix.dll", "net10.0", buildTag, "test");
            await File.WriteAllTextAsync(
                Path.Combine(versionDirectory, "hotfix.json"),
                JsonSerializer.Serialize(manifest, HotfixAdminJson.Options),
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(versionDirectory, "checksums.sha256"), "", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(versionDirectory, "READY"), "", TestContext.Current.CancellationToken);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class RecordingHotfixManager : IHotfixManager
    {
        private readonly HotfixReloadStatus _reloadStatus;
        private readonly HotfixReloadStatus _validateStatus;

        public RecordingHotfixManager(
            HotfixReloadStatus reloadStatus,
            string? loadedVersion,
            HotfixReloadStatus? validateStatus = null)
        {
            _reloadStatus = reloadStatus;
            _validateStatus = validateStatus ?? HotfixReloadStatus.Succeeded;
            Current = Snapshot(loadedVersion, reloadStatus);
        }

        public HotfixSnapshot Current { get; private set; }

        public ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default)
        {
            var result = new HotfixReloadResult(
                _validateStatus,
                Current,
                Current.Version,
                null,
                _validateStatus == HotfixReloadStatus.Succeeded ? [] : ["validation failed"],
                _validateStatus == HotfixReloadStatus.Succeeded ? null : "validation failed");
            return ValueTask.FromResult(result);
        }

        public ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
        {
            Current = Snapshot(Current.Version, _reloadStatus);
            var result = new HotfixReloadResult(
                _reloadStatus,
                Current,
                Current.Version,
                null,
                _reloadStatus == HotfixReloadStatus.Succeeded ? [] : ["reload failed"],
                _reloadStatus == HotfixReloadStatus.Succeeded ? null : "reload failed");
            return ValueTask.FromResult(result);
        }

        private static HotfixSnapshot Snapshot(string? version, HotfixReloadStatus status)
        {
            return new HotfixSnapshot(version, "test", null, DateTimeOffset.UtcNow, 7, [], status, null, null);
        }
    }
}
