using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixFileWatcherTests
{
    [Fact]
    public void Options_use_plan_defaults()
    {
        var options = new HotfixFileWatcherOptions();

        Assert.Equal("hotfix", options.Directory);
        Assert.Equal("reload.signal", options.Filter);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Debounce);
    }

    [Fact]
    public void AddLakonaGameHotfixFileWatcher_applies_configured_options()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameHotfix(new FixedAssemblySource(typeof(HotfixFileWatcherTests).Assembly.Location));

        services.AddLakonaGameHotfixFileWatcher(options =>
        {
            options.Directory = "custom/hotfix";
            options.Filter = "Game.*.dll";
            options.Debounce = TimeSpan.FromMilliseconds(250);
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HotfixFileWatcherOptions>>().Value;
        Assert.Equal("custom/hotfix", options.Directory);
        Assert.Equal("Game.*.dll", options.Filter);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.Debounce);
    }

    [Fact]
    public void AddLakonaGameHotfixFileWatcher_registers_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameHotfix(new FixedAssemblySource(typeof(HotfixFileWatcherTests).Assembly.Location));
        services.AddSingleton<ILogger<HotfixFileWatcherHostedService>>(NullLogger<HotfixFileWatcherHostedService>.Instance);

        services.AddLakonaGameHotfixFileWatcher();

        using var provider = services.BuildServiceProvider();
        var hostedService = Assert.Single(provider.GetServices<IHostedService>());
        Assert.IsType<HotfixFileWatcherHostedService>(hostedService);
    }

    [Fact]
    public async Task Hosted_service_reloads_after_reload_signal_change()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var manager = new RecordingHotfixManager(SuccessResult());
            using var service = CreateService(directory, manager, new RecordingLogger<HotfixFileWatcherHostedService>());

            await service.StartAsync(TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Game.Hotfix.dll"),
                "fake build output",
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "reload.signal"),
                "changed",
                TestContext.Current.CancellationToken);

            Assert.True(await manager.WaitForReloadAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Hosted_service_logs_warning_when_reload_fails()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var manager = new RecordingHotfixManager(FailedResult());
            var logger = new RecordingLogger<HotfixFileWatcherHostedService>();
            using var service = CreateService(directory, manager, logger);

            await service.StartAsync(TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "reload.signal"),
                "changed",
                TestContext.Current.CancellationToken);

            Assert.True(await manager.WaitForReloadAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.True(await logger.WaitForWarningAsync(
                "Hotfix file-watch reload failed",
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HotfixFileWatcherHostedService CreateService(
        string directory,
        IHotfixManager manager,
        ILogger<HotfixFileWatcherHostedService> logger)
    {
        return new HotfixFileWatcherHostedService(
            manager,
            Options.Create(new HotfixFileWatcherOptions
            {
                Directory = directory,
                Filter = "reload.signal",
                Debounce = TimeSpan.FromMilliseconds(50)
            }),
            logger);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Lakona.Game.HotfixFileWatcherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static HotfixReloadResult SuccessResult()
    {
        return new HotfixReloadResult(
            HotfixReloadStatus.Succeeded,
            EmptySnapshot(),
            "test",
            null,
            Array.Empty<string>());
    }

    private static HotfixReloadResult FailedResult()
    {
        return new HotfixReloadResult(
            HotfixReloadStatus.Failed,
            EmptySnapshot(),
            "test",
            null,
            ["load failed"],
            "load failed");
    }

    private static HotfixSnapshot EmptySnapshot()
    {
        return new HotfixSnapshot(
            null,
            null,
            null,
            null,
            0,
            Array.Empty<HotfixMethodKey>(),
            null,
            null,
            null);
    }

    private sealed class FixedAssemblySource : IHotfixAssemblySource
    {
        private readonly string _path;

        public FixedAssemblySource(string path)
        {
            _path = path;
        }

        public ValueTask<HotfixAssemblySourceResult> ResolveAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new HotfixAssemblySourceResult(
                "fixed",
                "test",
                _path,
                Path.GetDirectoryName(_path)!));
        }
    }

    private sealed class RecordingHotfixManager : IHotfixManager
    {
        private readonly TaskCompletionSource _reloadCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HotfixReloadResult _result;

        public RecordingHotfixManager(HotfixReloadResult result)
        {
            _result = result;
            Current = result.Current;
        }

        public HotfixSnapshot Current { get; }

        public ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_result);
        }

        public ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
        {
            _reloadCalled.TrySetResult();
            return ValueTask.FromResult(_result);
        }

        public async Task<bool> WaitForReloadAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var completed = await Task.WhenAny(_reloadCalled.Task, Task.Delay(timeout, cancellationToken));
            return completed == _reloadCalled.Task;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly object _gate = new();
        private readonly List<LogEntry> _entries = new();
        private readonly TaskCompletionSource _warningLogged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _entries.ToArray();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var entry = new LogEntry(logLevel, formatter(state, exception), exception);
            lock (_gate)
            {
                _entries.Add(entry);
            }

            if (entry.Level == LogLevel.Warning)
            {
                _warningLogged.TrySetResult();
            }
        }

        public async Task<bool> WaitForWarningAsync(
            string messageFragment,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var completed = await Task.WhenAny(_warningLogged.Task, Task.Delay(timeout, cancellationToken));
            return completed == _warningLogged.Task &&
                Entries.Any(entry =>
                    entry.Level == LogLevel.Warning &&
                    entry.Message.Contains(messageFragment, StringComparison.Ordinal));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
