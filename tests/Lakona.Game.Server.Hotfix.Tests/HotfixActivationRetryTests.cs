using System.Security.Cryptography;
using System.Text.Json;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.HotfixAdmin;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixActivationRetryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("same-operation")]
    [InlineData("another-operation")]
    public async Task RepeatedActivationPreservesGenerationAndRollback(string? retryOperation)
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Admin.ActivateAsync(new("v2", "v1", "same-operation"), Token);
        var retry = await fixture.Admin.ActivateAsync(new("v2", null, retryOperation), Token);
        Assert.Equal(first.DispatchTableVersion, retry.DispatchTableVersion);
        Assert.Equal("v1", retry.PreviousPointerVersion);
        Assert.Equal(1, fixture.Manager.Reloads);
        Assert.Equal(1, fixture.Manager.Validations);
        await fixture.AssertStateAsync("v2", "v1", "v2");

        await fixture.Admin.RollbackAsync(Token);
        await fixture.AssertStateAsync("v1", "v2", "v1");
        var rolledBack = fixture.Runtime.Current.DispatchTableVersion;
        await fixture.Admin.ActivateAsync(new("v1", null, null), Token);
        Assert.Equal(rolledBack, fixture.Runtime.Current.DispatchTableVersion);
        await fixture.AssertStateAsync("v1", "v2", "v1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("v0")]
    public async Task FirstVersionRepeatPreservesMissingOrExistingRollback(string? previous)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.WritePointerAsync("previous.txt", previous, Token);
        var generation = fixture.Runtime.Current.DispatchTableVersion;
        await fixture.Admin.ActivateAsync(new("v1", null, null), Token);
        Assert.Equal(generation, fixture.Runtime.Current.DispatchTableVersion);
        Assert.Equal(0, fixture.Manager.Reloads);
        await fixture.AssertStateAsync("v1", previous, "v1");
    }

    [Fact]
    public async Task PointerAheadOfRuntimeStillLoadsTargetWithoutLosingPrevious()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.WritePointerAsync("current.txt", "v2", Token);
        await fixture.Store.WritePointerAsync("previous.txt", "v1", Token);
        await fixture.Admin.ActivateAsync(new("v2", "v2", null), Token);
        Assert.Equal(1, fixture.Manager.Validations);
        Assert.Equal(1, fixture.Manager.Reloads);
        await fixture.AssertStateAsync("v2", "v1", "v2");
        await fixture.Admin.RollbackAsync(Token);
        await fixture.AssertStateAsync("v1", "v2", "v1");
    }

    [Fact]
    public async Task RuntimeAheadOfPointerDoesNotSkipPointerRepair()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Admin.ActivateAsync(new("v2", null, null), Token);
        await fixture.Store.WritePointerAsync("current.txt", "v1", Token);
        await fixture.Store.WritePointerAsync("previous.txt", "v0", Token);
        await fixture.Admin.ActivateAsync(new("v2", "v1", null), Token);
        await fixture.AssertStateAsync("v2", "v1", "v2");
    }

    [Theory]
    [InlineData("conflict")]
    [InlineData("checksum")]
    [InlineData("tag")]
    public async Task RepeatedActivationStillEnforcesPreconditionsAndPackageValidation(string failure)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Admin.ActivateAsync(new("v2", null, null), Token);
        var generation = fixture.Runtime.Current.DispatchTableVersion;
        if (failure == "checksum")
            await File.AppendAllTextAsync(Path.Combine(fixture.Store.VersionDirectory("v2"), "Server.Hotfix.dll"), "tampered", Token);
        if (failure == "tag") await fixture.WriteVersionAsync("v2", "other-build");
        var exception = await Assert.ThrowsAsync<HotfixAdminException>(() =>
            fixture.Admin.ActivateAsync(new("v2", failure == "conflict" ? "v1" : null, null), Token));
        Assert.Contains(failure switch
        {
            "conflict" => "expected 'v1'",
            "checksum" => "Checksum mismatch",
            _ => "BuildTag"
        }, exception.Message);
        Assert.Equal(generation, fixture.Runtime.Current.DispatchTableVersion);
        await fixture.AssertStateAsync("v2", "v1", "v2");
    }

    [Theory]
    [InlineData("activate", "validation", false)]
    [InlineData("activate", "reload", false)]
    [InlineData("repair", "validation", false)]
    [InlineData("repair", "reload", false)]
    [InlineData("rollback", "validation", false)]
    [InlineData("rollback", "reload", false)]
    [InlineData("activate", "validation", true)]
    [InlineData("activate", "reload", true)]
    [InlineData("repair", "validation", true)]
    [InlineData("repair", "reload", true)]
    [InlineData("rollback", "validation", true)]
    [InlineData("rollback", "reload", true)]
    public async Task FailedOrCanceledOperationPreservesPointersAndRuntime(string operation, string stage, bool cancel)
    {
        await using var fixture = await Fixture.CreateAsync();
        var current = "v1";
        string? previous = null;
        var loaded = "v1";
        if (operation == "rollback")
        {
            await fixture.Admin.ActivateAsync(new("v2", null, null), Token);
            current = loaded = "v2";
            previous = "v1";
        }
        else if (operation == "repair")
        {
            current = "v2";
            previous = "v1";
            await fixture.Store.WritePointerAsync("current.txt", current, Token);
            await fixture.Store.WritePointerAsync("previous.txt", previous, Token);
        }
        var generation = fixture.Runtime.Current.DispatchTableVersion;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        async Task Fail(CancellationToken token)
        {
            // Observe the actual mutation boundary rather than relying on scheduling delays.
            var expectedCurrent = stage == "validation" ? current : operation == "rollback" ? "v1" : "v2";
            var expectedPrevious = stage == "validation" || operation == "repair" ? previous : current;
            Assert.Equal(expectedCurrent, await fixture.Store.ReadPointerAsync("current.txt", Token));
            Assert.Equal(expectedPrevious, await fixture.Store.ReadPointerAsync("previous.txt", Token));
            if (cancel)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            throw new IOException("Injected before publication.");
        }
        if (stage == "validation") fixture.Manager.BeforeValidation = Fail;
        else fixture.Manager.BeforeReload = Fail;
        Task Invoke() => operation == "rollback"
            ? fixture.Admin.RollbackAsync(cancellation.Token)
            : fixture.Admin.ActivateAsync(new("v2", null, null), cancellation.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(Invoke);
        else await Assert.ThrowsAsync<IOException>(Invoke);
        Assert.Equal(generation, fixture.Runtime.Current.DispatchTableVersion);
        await fixture.AssertStateAsync(current, previous, loaded);

        // The lock and previous target must remain usable after failure/cancellation.
        fixture.Manager.BeforeValidation = fixture.Manager.BeforeReload = null;
        if (operation == "rollback")
        {
            await fixture.Admin.RollbackAsync(Token);
            await fixture.AssertStateAsync("v1", "v2", "v1");
        }
        else
        {
            await fixture.Admin.ActivateAsync(new("v2", null, null), Token);
            await fixture.AssertStateAsync("v2", "v1", "v2");
            await fixture.Admin.RollbackAsync(Token);
            await fixture.AssertStateAsync("v1", "v2", "v1");
        }
    }

    [Fact]
    public async Task CancellationAfterPublicationKeepsNewVersionAndRetryKeepsRollback()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        fixture.Manager.AfterReload = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Admin.ActivateAsync(new("v2", null, null), cancellation.Token));
        await fixture.AssertStateAsync("v2", "v1", "v2");
        var generation = fixture.Runtime.Current.DispatchTableVersion;
        fixture.Manager.AfterReload = null;
        await fixture.Admin.ActivateAsync(new("v2", null, null), Token);
        Assert.Equal(generation, fixture.Runtime.Current.DispatchTableVersion);
        await fixture.Admin.RollbackAsync(Token);
        await fixture.AssertStateAsync("v1", "v2", "v1");
    }

    [Fact]
    public async Task RepeatRetainsPublicationWarningsAndClearsAnEarlierAdminFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Runtime.Reloaded += (_, _) => throw new InvalidOperationException("observer warning");
        var first = await fixture.Admin.ActivateAsync(new("v2", null, null), Token);
        Assert.NotEmpty(first.LastOperationWarnings);
        var repeated = await fixture.Admin.ActivateAsync(new("v2", null, null), Token);
        Assert.Equal(first.LastOperationWarnings, repeated.LastOperationWarnings);
        Assert.Equal(first.DispatchTableVersion, repeated.DispatchTableVersion);

        await Assert.ThrowsAsync<HotfixAdminException>(() =>
            fixture.Admin.ActivateAsync(new("v2", "v1", null), Token));
        Assert.NotNull((await fixture.Admin.GetStatusAsync(Token)).LastOperationFailure);
        var recovered = await fixture.Admin.ActivateAsync(new("v2", null, null), Token);
        Assert.Null(recovered.LastOperationFailure);
        Assert.Equal(first.DispatchTableVersion, recovered.DispatchTableVersion);
        Assert.Equal(1, fixture.Manager.Reloads);
        await fixture.AssertStateAsync("v2", "v1", "v2");
    }

    [Fact]
    public async Task ConcurrentSameVersionActivationsPublishOnceAndExplicitReloadStillReloads()
    {
        await using var fixture = await Fixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Manager.BeforeReload = async token => { entered.TrySetResult(); await resume.Task.WaitAsync(token); };
        var first = fixture.Admin.ActivateAsync(new("v2", null, null), Token);
        Task<HotfixStatusResponse>? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            second = fixture.Admin.ActivateAsync(new("v2", null, null), Token);
            Assert.False(second.IsCompleted);
        }
        finally
        {
            resume.TrySetResult();
            await first;
            if (second is not null) await second;
        }
        Assert.Equal(1, fixture.Manager.Reloads);
        await fixture.AssertStateAsync("v2", "v1", "v2");
        var generation = fixture.Runtime.Current.DispatchTableVersion;
        await fixture.Admin.ReloadAsync(Token);
        Assert.True(fixture.Runtime.Current.DispatchTableVersion > generation);
        await fixture.AssertStateAsync("v2", "v1", "v2");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "LakonaHotfixActivationTests", Guid.NewGuid().ToString("N"));
        public HotfixVersionStore Store { get; }
        public HotfixManager Runtime { get; }
        public ControlledManager Manager { get; }
        public HotfixAdminController Admin { get; }

        private Fixture()
        {
            Store = new(Root);
            Runtime = new(new VersionPointerHotfixAssemblySource(Root, "current.txt", "Server.Hotfix.dll"));
            Manager = new(Runtime);
            Admin = new(new HotfixAdminOptions { HotfixRoot = Root, BuildTag = "test" }, Store, Manager);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            try
            {
                await fixture.WriteVersionAsync("v1");
                await fixture.WriteVersionAsync("v2");
                await fixture.Store.WritePointerAsync("current.txt", "v1", Token);
                var result = await fixture.Runtime.ReloadAsync(Token);
                Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics));
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task WriteVersionAsync(string version, string buildTag = "test")
        {
            var directory = Store.VersionDirectory(version);
            Directory.CreateDirectory(directory);
            var compilation = CSharpCompilation.Create("ActivationTest" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText($"public static class Marker {{ public static string Value() => \"{version}\"; }}")],
                HotfixTestMetadataReferences.CreateDefaultReferences(typeof(object)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (var dll = File.Create(Path.Combine(directory, "Server.Hotfix.dll")))
            {
                var emitted = compilation.Emit(dll, cancellationToken: Token);
                Assert.True(emitted.Success, string.Join("; ", emitted.Diagnostics));
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "hotfix.json"), JsonSerializer.Serialize(
                new HotfixPackageManifest(version, DateTimeOffset.UtcNow, "Server.Hotfix.dll", "net10.0", buildTag, "test"),
                HotfixAdminJson.Options), Token);
            var lines = new List<string>();
            foreach (var name in new[] { "Server.Hotfix.dll", "hotfix.json" })
                lines.Add(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, name), Token))) + " " + name);
            await File.WriteAllLinesAsync(Path.Combine(directory, "checksums.sha256"), lines, Token);
            await File.WriteAllTextAsync(Path.Combine(directory, "READY"), "", Token);
        }

        public async Task AssertStateAsync(string current, string? previous, string loaded)
        {
            var status = await Admin.GetStatusAsync(Token);
            Assert.Equal(current, status.CurrentPointerVersion);
            Assert.Equal(previous, status.PreviousPointerVersion);
            Assert.Equal(loaded, status.LoadedVersion);
            var runtime = ((IHotfixRuntimeAccessor)Runtime).Current;
            Assert.Equal(loaded, runtime.MainAssembly!.GetType("Marker")!.GetMethod("Value")!.Invoke(null, null));
        }

        public async ValueTask DisposeAsync()
        {
            try { await Runtime.DisposeAsync(); }
            finally { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
        }
    }

    private sealed class ControlledManager(HotfixManager inner) : IHotfixManager
    {
        public int Validations { get; private set; }
        public int Reloads { get; private set; }
        public Func<CancellationToken, Task>? BeforeValidation { get; set; }
        public Func<CancellationToken, Task>? BeforeReload { get; set; }
        public Action? AfterReload { get; set; }
        public HotfixSnapshot Current => inner.Current;
        public event EventHandler<HotfixReloadResult>? Reloaded { add => inner.Reloaded += value; remove => inner.Reloaded -= value; }
        public ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default) => inner.ValidateAsync(cancellationToken);
        public async ValueTask<HotfixReloadResult> ValidateAsync(IHotfixAssemblySource source, CancellationToken cancellationToken = default)
        {
            Validations++;
            if (BeforeValidation is not null) await BeforeValidation(cancellationToken);
            return await inner.ValidateAsync(source, cancellationToken);
        }
        public async ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
        {
            Reloads++;
            if (BeforeReload is not null) await BeforeReload(cancellationToken);
            var result = await inner.ReloadAsync(cancellationToken);
            AfterReload?.Invoke();
            return result;
        }
    }
}
