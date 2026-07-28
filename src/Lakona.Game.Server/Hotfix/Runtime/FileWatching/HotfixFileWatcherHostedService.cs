using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixFileWatcherHostedService : IHostedService, IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly IHotfixManager _manager;
    private readonly HotfixFileWatcherOptions _options;
    private readonly ILogger<HotfixFileWatcherHostedService> _logger;
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private CancellationTokenSource? _reloadCancellation;
    private readonly HashSet<Task> _reloadTasks = [];
    private bool _running;
    private bool _disposed;
    private long _reloadGeneration;

    public HotfixFileWatcherHostedService(
        IHotfixManager manager,
        IOptions<HotfixFileWatcherOptions> options,
        ILogger<HotfixFileWatcherHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _manager = manager;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running)
            {
                return Task.CompletedTask;
            }

            System.IO.Directory.CreateDirectory(_options.Directory);
            var watcher = new FileSystemWatcher(_options.Directory, _options.Filter)
            {
                IncludeSubdirectories = false
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            _reloadCancellation = new CancellationTokenSource();
            _running = true;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopped = StopWatching();
        stopped.Cancellation?.Cancel();
        try
        {
            await Task.WhenAll(stopped.ReloadTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stopped.Cancellation?.Dispose();
        }
    }

    public void Dispose()
    {
        var stopped = StopWatching(dispose: true);
        stopped.Cancellation?.Cancel();
        stopped.Cancellation?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        var stopped = StopWatching(dispose: true);
        stopped.Cancellation?.Cancel();
        try
        {
            await Task.WhenAll(stopped.ReloadTasks).ConfigureAwait(false);
        }
        finally
        {
            stopped.Cancellation?.Dispose();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        var debounce = _options.Debounce <= TimeSpan.Zero ? DefaultDebounce : _options.Debounce;
        long generation;
        lock (_gate)
        {
            if (!_running || _disposed)
            {
                return;
            }

            generation = ++_reloadGeneration;
            _timer?.Dispose();
            _timer = new Timer(
                static state =>
                {
                    var scheduled = (ScheduledReload)state!;
                    scheduled.Service.StartReload(scheduled.Generation);
                },
                new ScheduledReload(this, generation),
                debounce,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void StartReload(long generation)
    {
        Task reloadTask;
        lock (_gate)
        {
            if (!_running || _disposed || generation != _reloadGeneration)
            {
                return;
            }

            var cancellationToken = _reloadCancellation?.Token
                ?? throw new InvalidOperationException("The Hotfix file watcher has no reload lifetime.");
            reloadTask = ReloadAsync(cancellationToken);
            _reloadTasks.Add(reloadTask);
        }

        _ = reloadTask.ContinueWith(
            static (completed, state) =>
                ((HotfixFileWatcherHostedService)state!).RemoveReload(completed),
            this,
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _manager.ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Hotfix file-watch reload failed: {Error}. Diagnostics: {Diagnostics}",
                    result.ErrorMessage,
                    string.Join(Environment.NewLine, result.Diagnostics));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Hotfix file-watch reload threw an exception.");
        }
    }

    private void RemoveReload(Task reloadTask)
    {
        lock (_gate)
        {
            _reloadTasks.Remove(reloadTask);
        }
    }

    private StoppedWatcher StopWatching(bool dispose = false)
    {
        FileSystemWatcher? watcher;
        Timer? timer;
        CancellationTokenSource? cancellation;
        Task[] reloadTasks;
        lock (_gate)
        {
            if (dispose)
            {
                _disposed = true;
            }

            _running = false;
            _reloadGeneration++;
            watcher = _watcher;
            timer = _timer;
            cancellation = _reloadCancellation;
            reloadTasks = _reloadTasks.ToArray();
            _watcher = null;
            _timer = null;
            _reloadCancellation = null;
            _reloadTasks.Clear();
        }

        if (watcher is not null)
        {
            watcher.Changed -= OnChanged;
            watcher.Created -= OnChanged;
            watcher.Renamed -= OnChanged;
            watcher.Dispose();
        }

        timer?.Dispose();
        return new StoppedWatcher(cancellation, reloadTasks);
    }

    private sealed record ScheduledReload(HotfixFileWatcherHostedService Service, long Generation);

    private sealed record StoppedWatcher(CancellationTokenSource? Cancellation, Task[] ReloadTasks);
}
