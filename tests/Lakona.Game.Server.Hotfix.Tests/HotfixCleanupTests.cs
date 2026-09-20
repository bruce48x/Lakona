using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Scanning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Table_owned_modules_all_clean_up_without_masking_activation_failure(bool constructorFails)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-table-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        HotfixAssemblyLoadContext? context = null;
        try
        {
            Emit(directory, constructorFails, true, true, secondModule: true);
            var path = Path.Combine(directory, "Hotfix.dll");
            context = new HotfixAssemblyLoadContext(path, []);
            var scan = HotfixBehaviorScanner.Scan(context.LoadMainAssemblyFromBytes(path));
            Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
            var table = new HotfixDispatchTable(1, scan.Methods, scan.Services, scan.ActorMethods,
                scan.ActorLifecycles, scan.TimerMethods, scan.HttpEndpoints);
            var unloaded = false;
            context.Unloading += _ => unloaded = true;
            if (constructorFails)
            {
                var error = Assert.Throws<AggregateException>(() => table.ValidateModuleActivation(new EmptyProvider()));
                Assert.Contains("CONSTRUCTOR_FAILURE", error.Message);
                Assert.Contains("CLEANUP_FAILURE", error.Message);
            }
            else table.ValidateModuleActivation(new EmptyProvider());
            var provider = new FailingProvider();
            var failures = await HotfixResourceCleanup.RunAsync(table, provider, context);
            Assert.True(unloaded);
            Assert.Equal(1, provider.DisposeCalls);
            Assert.Contains(failures, failure => failure.Message.Contains("PROVIDER_FAILURE"));
            if (!constructorFails) Assert.Contains(failures, failure => failure.Message.Contains("CLEANUP_FAILURE"));
            await table.DisposeAsync();
            var disposals = await File.ReadAllLinesAsync(Path.Combine(directory, "disposals.txt"), TestContext.Current.CancellationToken);
            Assert.Equal(["BService", "AService"], disposals);
        }
        finally
        {
            context?.Unload();
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Candidate_cleanup_failure_is_reported_and_later_reload_recovers(bool constructorFails, bool asyncDispose)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var observer = new CandidateObserver();
        var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(directory, "Hotfix.dll"), participants: [observer]);
        try
        {
            Emit(directory, false, false, false);
            var initial = await manager.ReloadAsync(TestContext.Current.CancellationToken);
            Assert.True(initial.Succeeded, initial.ErrorMessage);
            Emit(directory, constructorFails, asyncDispose, true);
            var validation = await manager.ValidateAsync(TestContext.Current.CancellationToken);
            Assert.False(validation.Succeeded);
            Assert.Contains(validation.Diagnostics, text => text.Contains("CLEANUP_FAILURE"));
            if (constructorFails) Assert.Contains("CONSTRUCTOR_FAILURE", validation.ErrorMessage);
            else Assert.True(observer.Unloaded);
            Assert.Equal(initial.Current.DispatchTableVersion, manager.Current.DispatchTableVersion);
            if (constructorFails)
            {
                var failed = await manager.ReloadAsync(TestContext.Current.CancellationToken);
                Assert.False(failed.Succeeded);
                Assert.Contains("CONSTRUCTOR_FAILURE", failed.ErrorMessage);
                Assert.Contains(failed.Diagnostics, text => text.Contains("CLEANUP_FAILURE"));
            }
            var lines = await File.ReadAllLinesAsync(Path.Combine(directory, "disposals.txt"), TestContext.Current.CancellationToken);
            Assert.Equal(constructorFails ? 2 : 1, lines.Length);
            Emit(directory, false, false, false);
            Assert.True((await manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
        }
        finally
        {
            await manager.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Cancellation_keeps_cancellation_and_cleanup_failure_and_unloads_context()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var observer = new CandidateObserver { Cancel = true };
        var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(directory, "Hotfix.dll"), participants: [observer]);
        try
        {
            Emit(directory, false, true, true);
            var error = await Assert.ThrowsAsync<AggregateException>(async () => await manager.ValidateAsync(TestContext.Current.CancellationToken));
            Assert.Contains(error.InnerExceptions, exception => exception is OperationCanceledException);
            Assert.Contains(error.InnerExceptions, exception => exception.Message.Contains("CLEANUP_FAILURE"));
            Assert.True(observer.Unloaded);
            observer.Cancel = false;
            Emit(directory, false, false, false);
            Assert.True((await manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
        }
        finally
        {
            await manager.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_failure_keeps_new_generation_and_is_reported(bool deferred)
    {
        var logger = new CleanupLogger();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logger));
        using var root = new ServiceCollection().AddSingleton<ILoggerFactory>(loggerFactory).BuildServiceProvider();
        var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(Path.GetTempPath(), "unused.dll"), rootServices: root);
        var oldProvider = new FailingProvider();
        var old = Runtime("old", oldProvider);
        await manager.PublishCandidateAsync(old, Snapshot("old"), TestContext.Current.CancellationToken);
        var lease = deferred ? old.AcquireLease() : null;
        try
        {
            var next = Runtime("next", new EmptyProvider());
            var result = await manager.PublishCandidateAsync(next, Snapshot("next"), TestContext.Current.CancellationToken);
            Assert.Equal("next", manager.Current.Version);
            Assert.Equal(deferred ? HotfixReloadStatus.Succeeded : HotfixReloadStatus.SucceededWithWarnings, result.Status);
            if (deferred)
            {
                Assert.Equal(0, oldProvider.DisposeCalls);
                lease!.Dispose();
                await logger.FailureLogged.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }
            else Assert.Contains(result.Diagnostics, text => text.Contains("PROVIDER_FAILURE"));
            Assert.Equal(1, oldProvider.DisposeCalls);
            Assert.Contains("old", await logger.FailureLogged.Task);
        }
        finally
        {
            lease?.Dispose();
            await manager.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_success_logs_unloaded_generation(bool deferred)
    {
        var logger = new CleanupLogger();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logger));
        using var root = new ServiceCollection().AddSingleton<ILoggerFactory>(loggerFactory).BuildServiceProvider();
        var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(Path.GetTempPath(), "unused.dll"), rootServices: root);
        var old = Runtime("old", new EmptyProvider());
        await manager.PublishCandidateAsync(old, Snapshot("old"), TestContext.Current.CancellationToken);
        var lease = deferred ? old.AcquireLease() : null;
        try
        {
            var result = await manager.PublishCandidateAsync(
                Runtime("next", new EmptyProvider()),
                Snapshot("next"),
                TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded);
            Assert.Equal(HotfixReloadStatus.Succeeded, result.Status);
            if (deferred)
            {
                lease!.Dispose();
            }

            var text = await logger.UnloadLogged.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Contains("unloaded", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("retired", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("old", text, StringComparison.Ordinal);
        }
        finally
        {
            lease?.Dispose();
            await manager.DisposeAsync();
        }
    }

    [Fact]
    public async Task Publish_and_shutdown_do_not_retire_the_shared_unpublished_runtime()
    {
        var manager = new HotfixManager(
            new CurrentDirectoryHotfixAssemblySource(Path.GetTempPath(), "unused.dll"));
        await manager.PublishCandidateAsync(
            Runtime("published", new EmptyProvider()),
            Snapshot("published"),
            TestContext.Current.CancellationToken);

        await manager.DisposeAsync();

        // The unpublished runtime is one process-wide shared singleton. Retiring
        // it is irreversible, so publishing or shutting down must never retire
        // it or every manager that has not published yet loses its lease.
        using (HotfixPublicationState.Empty.Runtime.AcquireLease())
        {
        }
    }

    [Fact]
    public async Task Shutdown_waits_for_already_retiring_async_cleanup()
    {
        var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(Path.GetTempPath(), "unused.dll"));
        var provider = new DeferredProvider();
        var old = Runtime("old", provider);
        await manager.PublishCandidateAsync(old, Snapshot("old"), TestContext.Current.CancellationToken);
        try
        {
            var result = await manager.PublishCandidateAsync(Runtime("next", new EmptyProvider()), Snapshot("next"), TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded);
            var shutdown = manager.DisposeAsync().AsTask();
            Assert.False(shutdown.IsCompleted);
            provider.Release.SetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Contains(await old.RetirementCompletion, error => error.Message.Contains("ASYNC_CLEANUP_FAILURE"));
        }
        finally
        {
            provider.Release.TrySetResult();
            await manager.DisposeAsync();
        }
    }

    private sealed class DeferredProvider : IServiceProvider, IAsyncDisposable
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public object? GetService(Type serviceType) => null;
        public async ValueTask DisposeAsync()
        {
            await Release.Task;
            throw new InvalidOperationException("ASYNC_CLEANUP_FAILURE");
        }
    }

    [Fact]
    public async Task Failed_publication_preserves_original_and_runtime_cleanup_failures()
    {
        var observer = new CandidateObserver { FailPrepare = true };
        var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(Path.GetTempPath(), "unused.dll"), participants: [observer]);
        var provider = new FailingProvider();
        var result = await manager.PublishCandidateAsync(Runtime("bad", provider), Snapshot("bad"), TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, text => text.Contains("PREPARE_FAILURE"));
        Assert.Contains(result.Diagnostics, text => text.Contains("PROVIDER_FAILURE"));
        Assert.Equal(1, provider.DisposeCalls);
        observer.FailPrepare = false;
        Assert.True((await manager.PublishCandidateAsync(Runtime("next", new EmptyProvider()), Snapshot("next"), TestContext.Current.CancellationToken)).Succeeded);
        await manager.DisposeAsync();
    }

    private static HotfixRuntimeSnapshot Runtime(string version, IServiceProvider provider) =>
        new(new HotfixServiceInvoker(), provider, null, provider, null, null, version, null, true, null);

    private static HotfixSnapshot Snapshot(string version) => new(version, null, DateTimeOffset.UtcNow, 1, [], null, null, null);

    private sealed class EmptyProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FailingProvider : IServiceProvider, IDisposable
    {
        public int DisposeCalls;
        public object? GetService(Type serviceType) => null;
        public void Dispose() { DisposeCalls++; throw new ArgumentException("PROVIDER_FAILURE"); }
    }

    private sealed class CleanupLogger : ILogger<HotfixManager>, ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public TaskCompletionSource<string> FailureLogged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> UnloadLogged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (text.Contains("PROVIDER_FAILURE")) FailureLogged.TrySetResult(text);
            if (logLevel == LogLevel.Information && text.Contains("unloaded", StringComparison.OrdinalIgnoreCase))
                UnloadLogged.TrySetResult(text);
        }
    }

    private sealed class CandidateObserver : IHotfixRuntimePublicationParticipant
    {
        public bool Unloaded;
        public bool Cancel;
        public bool FailPrepare;
        public ValueTask ValidateAsync(HotfixRuntimeSnapshot previous, HotfixRuntimeSnapshot candidate, CancellationToken cancellationToken = default)
        {
            candidate.LoadContext!.Unloading += _ => Unloaded = true;
            if (Cancel) throw new OperationCanceledException(cancellationToken);
            return default;
        }
        public ValueTask<IHotfixRuntimePublicationTransaction> PrepareAsync(HotfixRuntimeSnapshot previous, HotfixRuntimeSnapshot candidate, CancellationToken cancellationToken = default)
        {
            if (FailPrepare) throw new InvalidOperationException("PREPARE_FAILURE");
            return new(NoopHotfixRuntimePublicationTransaction.Instance);
        }
    }

    private static void Emit(string directory, bool constructorFails, bool asyncDispose, bool cleanupFails, bool secondModule = false)
    {
        var path = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(Path.Combine(directory, "disposals.txt"), true);
        var cleanup = cleanupFails ? $"File.AppendAllText({path}, GetType().Name + \"\\n\"); throw new {(asyncDispose ? "ArgumentException" : "InvalidOperationException")}(\"CLEANUP_FAILURE\");" : "";
        var dispose = asyncDispose ? $"public async ValueTask DisposeAsync() {{ await Task.Yield(); {cleanup} }}" : $"public void Dispose() {{ {cleanup} }}";
        var source = $$"""
            using System;
            using System.IO;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Http;
            [LakonaHttpService("a")] public sealed class AService : {{(asyncDispose ? "IAsyncDisposable" : "IDisposable")}}
            {
                {{dispose}}
                [LakonaHttpEndpoint("GET", "/a")] public ValueTask<LakonaHttpResponse> Get(LakonaHttpCall call) => default;
            }
            [LakonaHttpService("z")] public sealed class ZService
            {
                public ZService() { {{(constructorFails ? "throw new InvalidOperationException(\"CONSTRUCTOR_FAILURE\");" : "")}} }
                [LakonaHttpEndpoint("GET", "/z")] public ValueTask<LakonaHttpResponse> Get(LakonaHttpCall call) => default;
            }
            """;
        if (secondModule)
            source += $$"""
                [LakonaHttpService("b")] public sealed class BService : {{(asyncDispose ? "IAsyncDisposable" : "IDisposable")}}
                {
                    {{dispose}}
                    [LakonaHttpEndpoint("GET", "/b")] public ValueTask<LakonaHttpResponse> Get(LakonaHttpCall call) => default;
                }
                """;
        var compilation = CSharpCompilation.Create("CleanupHotfix", [CSharpSyntaxTree.ParseText(source)],
            HotfixTestMetadataReferences.CreateDefaultReferences(typeof(HotfixManager), typeof(IServiceCollection)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = compilation.Emit(Path.Combine(directory, "Hotfix.dll"));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }
}
