using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lakona.Hub.Installation;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class UserInstallationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lakona-install-test-" + Guid.NewGuid().ToString("N"));
    private readonly RSA key = RSA.Create(2048);
    private readonly InstallationPaths paths;
    private readonly TestHost host = new();
    private SignedPackage Verifier => new(key.ExportSubjectPublicKeyInfoPem());
    public UserInstallationTests() { Directory.CreateDirectory(root); paths = new InstallationPaths(root); }

    [Fact]
    public async Task Install_update_and_uninstall_preserve_user_data()
    {
        var settings = Path.Combine(root, "Lakona", "Hub", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, "my projects");
        var transaction = new InstallationTransaction(paths, Verifier, host);
        await transaction.InstallAsync(Package("1.0.0"), false, cancellationToken: TestContext.Current.CancellationToken);
        await transaction.InstallAsync(Package("1.1.0"), false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("1.1.0", Verifier.VerifyDirectory(paths.Application).Version);
        Assert.Equal("1.1.0", host.RegisteredVersion);
        Assert.False(Directory.Exists(paths.Backup));
        Assert.False(File.Exists(paths.Journal));
        await transaction.UninstallAsync();
        Assert.False(Directory.Exists(paths.Application));
        Assert.Null(host.RegisteredVersion);
        Assert.Equal("my projects", File.ReadAllText(settings));
    }

    [Fact]
    public async Task Failed_startup_restores_and_relaunches_verified_previous_version()
    {
        var transaction = new InstallationTransaction(paths, Verifier, host);
        await transaction.InstallAsync(Package("1.0.0"), false, cancellationToken: TestContext.Current.CancellationToken);
        host.Healthy = false;
        await Assert.ThrowsAsync<IOException>(() => transaction.InstallAsync(Package("1.1.0"), false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("1.0.0", Verifier.VerifyDirectory(paths.Application).Version);
        Assert.Equal(1, host.Relaunches);
        Assert.False(File.Exists(paths.Journal));
    }

    [Fact]
    public async Task Failed_first_start_removes_only_the_new_application()
    {
        host.Healthy = false;
        await Assert.ThrowsAsync<IOException>(() => new InstallationTransaction(paths, Verifier, host).InstallAsync(Package("1.0.0"), false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(paths.Application));
        Assert.False(File.Exists(paths.Journal));
    }

    [Theory]
    [InlineData("prepared", false, false)]
    [InlineData("prepared", true, false)]
    [InlineData("backed-up", true, false)]
    [InlineData("backed-up", true, true)]
    [InlineData("activated", true, true)]
    [InlineData("restoring", true, false)]
    [InlineData("restoring", false, false)]
    public async Task Recovery_handles_crashes_between_journal_and_directory_moves(string stage, bool backup, bool activated)
    {
        var transaction = new InstallationTransaction(paths, Verifier, host);
        await transaction.InstallAsync(Package("1.0.0"), false, cancellationToken: TestContext.Current.CancellationToken);
        if (backup) Directory.Move(paths.Application, paths.Backup);
        if (activated) Verifier.Extract(Package("1.1.0"), paths.Application, TestContext.Current.CancellationToken);
        WriteJournal(stage, true);
        await transaction.RecoverAsync();
        Assert.Equal("1.0.0", Verifier.VerifyDirectory(paths.Application).Version);
        Assert.False(File.Exists(paths.Journal));
    }

    [Fact]
    public async Task Confirmed_recovery_finishes_registration_without_rolling_back()
    {
        paths.Prepare();
        Verifier.Extract(Package("1.0.0"), paths.Backup, TestContext.Current.CancellationToken);
        Verifier.Extract(Package("1.1.0"), paths.Application, TestContext.Current.CancellationToken);
        WriteJournal("confirmed", true);
        await new InstallationTransaction(paths, Verifier, host).RecoverAsync();
        Assert.Equal("1.1.0", host.RegisteredVersion);
        Assert.False(Directory.Exists(paths.Backup));
        Assert.Equal(0, host.Relaunches);
    }

    [Fact]
    public async Task Setup_does_not_replace_the_previous_process_it_just_recovered()
    {
        paths.Prepare();
        Verifier.Extract(Package("1.0.0"), paths.Backup, TestContext.Current.CancellationToken);
        WriteJournal("backed-up", true);
        await Assert.ThrowsAsync<IOException>(() => new InstallationTransaction(paths, Verifier, host).InstallAsync(
            Package("1.2.0"), false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("1.0.0", Verifier.VerifyDirectory(paths.Application).Version);
        Assert.Equal(1, host.Relaunches);
    }

    [Fact]
    public async Task Unverifiable_backup_is_preserved_for_repair()
    {
        paths.Prepare();
        Verifier.Extract(Package("1.0.0"), paths.Backup, TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(paths.Backup, "Lakona.Hub.exe"), "damaged");
        Verifier.Extract(Package("1.1.0"), paths.Application, TestContext.Current.CancellationToken);
        WriteJournal("activated", true);
        await Assert.ThrowsAsync<InvalidDataException>(() => new InstallationTransaction(paths, Verifier, host).RecoverAsync());
        Assert.True(Directory.Exists(paths.Backup));
        Assert.Equal("1.1.0", Verifier.VerifyDirectory(paths.Application).Version);
        Assert.True(File.Exists(paths.Journal));
    }

    [Fact]
    public async Task Repair_restores_corrupted_payload_from_signed_package()
    {
        var archive = Package("1.0.0");
        var transaction = new InstallationTransaction(paths, Verifier, host);
        await transaction.InstallAsync(archive, false, cancellationToken: TestContext.Current.CancellationToken);
        File.WriteAllText(paths.Executable, "corrupted");
        await transaction.InstallAsync(archive, true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("1.0.0", Verifier.VerifyDirectory(paths.Application).Version);
    }

    [Theory]
    [InlineData("0.9.0", true)]
    [InlineData("1.0.0", false)]
    public async Task Downgrade_or_implicit_reinstall_leaves_current_version_intact(string version, bool repair)
    {
        var transaction = new InstallationTransaction(paths, Verifier, host);
        await transaction.InstallAsync(Package("1.0.0"), false, cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.InstallAsync(Package(version), repair, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("1.0.0", Verifier.VerifyDirectory(paths.Application).Version);
    }

    [Fact]
    public async Task Lock_rejects_concurrent_operation_before_mutation()
    {
        using var operation = paths.AcquireLock();
        await Assert.ThrowsAsync<IOException>(() => new InstallationTransaction(paths, Verifier, host).InstallAsync(Package("1.0.0"), false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(paths.Application));
    }

    [Fact]
    public async Task Cancellation_before_activation_preserves_previous_installation()
    {
        var transaction = new InstallationTransaction(paths, Verifier, host);
        await transaction.InstallAsync(Package("1.0.0"), false, cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transaction.InstallAsync(Package("1.1.0"), false, cancellationToken: new CancellationToken(true)));
        Assert.Equal("1.0.0", Verifier.VerifyDirectory(paths.Application).Version);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("C:/escape")]
    [InlineData("/escape")]
    [InlineData("dir/../escape")]
    [InlineData("dir\\escape")]
    [InlineData("CON.txt")]
    [InlineData("payload:stream")]
    [InlineData("payload.")]
    public void Unsafe_signed_paths_are_rejected(string name)
    {
        var archive = Package("1.0.0", extra: name);
        Assert.Throws<InvalidDataException>(() => Verifier.Extract(archive, paths.Staging, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(paths.Staging));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("link")]
    [InlineData("unsigned")]
    [InlineData("tamper")]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("architecture")]
    [InlineData("protocol")]
    public void Invalid_packages_are_rejected(string defect)
    {
        var archive = Package("1.0.0", defect: defect);
        Assert.ThrowsAny<Exception>(() => Verifier.Extract(archive, paths.Staging, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(paths.Application));
    }

    [Fact]
    public void Cleanup_retains_live_work_and_bounds_abandoned_directories()
    {
        paths.Prepare();
        var active = Path.Combine(paths.State, "handoff-active");
        Directory.CreateDirectory(active);
        Directory.SetLastWriteTimeUtc(active, DateTime.UtcNow.AddDays(-9));
        using var lease = paths.HoldWork(active);
        File.WriteAllText(Path.Combine(active, "completed"), "completed");
        for (var index = 0; index < 5; index++)
        {
            var abandoned = Path.Combine(paths.State, "setup-" + index);
            Directory.CreateDirectory(abandoned);
            File.WriteAllText(Path.Combine(abandoned, "payload"), "test");
            Directory.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddDays(-9));
        }
        paths.CleanupCompletedWork();
        Assert.True(Directory.Exists(active));
        Assert.Empty(Directory.EnumerateDirectories(paths.State, "setup-*"));
    }

    [Fact]
    public void Diagnostics_are_bounded_and_do_not_record_exception_payloads()
    {
        paths.Prepare();
        var log = Path.Combine(paths.State, "installation.log");
        File.WriteAllText(log, new string('x', 70000));
        InstallationLog.Write(paths, "failed", new IOException("secret profile path and token"));
        var text = File.ReadAllText(log);
        Assert.Contains("IOException", text);
        Assert.DoesNotContain("secret", text);
        Assert.True(new FileInfo(log).Length < 1024);
    }

    private void WriteJournal(string stage, bool previous) => File.WriteAllText(paths.Journal,
        JsonSerializer.Serialize(new InstallationJournal(stage, "1.1.0", "token", previous), InstallationJson.Default.InstallationJournal));

    private string Package(string version, string? extra = null, string? defect = null)
    {
        var files = new Dictionary<string, byte[]> { ["Lakona.Hub.exe"] = System.Text.Encoding.UTF8.GetBytes("hub " + version), ["Lakona.Hub.Updater.exe"] = [1, 2, 3] };
        if (extra is not null) files[extra] = [4, 5, 6];
        var manifest = new PackageManifest(defect == "protocol" ? 2 : 1, version, defect == "architecture" ? "win-arm64" : "win-x64",
            files.ToDictionary(pair => pair.Key, pair => new PackageFile(pair.Value.Length, Convert.ToHexString(SHA256.HashData(pair.Value)))));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, InstallationJson.Default.PackageManifest);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, byte[] value, bool link = false)
        {
            var entry = zip.CreateEntry(name);
            if (link) entry.ExternalAttributes = unchecked((int)0xa0000000);
            using var stream = entry.Open(); stream.Write(value);
        }
        Add(SignedPackage.ManifestName, bytes);
        Add(SignedPackage.SignatureName, defect == "unsigned" ? new byte[256] : key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        foreach (var (name, data) in files)
        {
            if (defect == "missing" && name == "Lakona.Hub.exe") continue;
            Add(name, defect == "tamper" ? new byte[data.Length] : data, defect == "link");
        }
        if (defect == "duplicate") Add("LAKONA.HUB.exe", [1]);
        if (defect == "unknown") Add("unrecognized.dll", [1]);
        return path;
    }

    public void Dispose() { key.Dispose(); Directory.Delete(root, true); }
    private sealed class TestHost : IInstallationHost
    {
        internal bool Healthy = true;
        internal string? RegisteredVersion;
        internal int Relaunches;
        public Task<bool> LaunchAndConfirmAsync(string executable, string version, string token) => Task.FromResult(Healthy);
        public Task StopFailedApplicationAsync() => Task.CompletedTask;
        public void Register(string version) => RegisteredVersion = version;
        public void Unregister() => RegisteredVersion = null;
        public void LaunchPrevious(string executable) => Relaunches++;
    }
}
