using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixFileWatcherHostedService : IHostedService, IDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly IHotfixManager _manager;
    private readonly HotfixFileWatcherOptions _options;
    private readonly ILogger<HotfixFileWatcherHostedService> _logger;
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
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
            _running = true;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopWatching();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopWatching(dispose: true);
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
                    _ = scheduled.Service.ReloadAsync(scheduled.Generation);
                },
                new ScheduledReload(this, generation),
                debounce,
                Timeout.InfiniteTimeSpan);
        }
    }

    private async Task ReloadAsync(long generation)
    {
        lock (_gate)
        {
            if (!_running || _disposed || generation != _reloadGeneration)
            {
                return;
            }
        }

        try
        {
            var result = await _manager.ReloadAsync().ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Hotfix file-watch reload failed: {Error}. Diagnostics: {Diagnostics}",
                    result.ErrorMessage,
                    string.Join(Environment.NewLine, result.Diagnostics));
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Hotfix file-watch reload threw an exception.");
        }
    }

    private void StopWatching(bool dispose = false)
    {
        FileSystemWatcher? watcher;
        Timer? timer;
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
            _watcher = null;
            _timer = null;
        }

        if (watcher is not null)
        {
            watcher.Changed -= OnChanged;
            watcher.Created -= OnChanged;
            watcher.Renamed -= OnChanged;
            watcher.Dispose();
        }

        timer?.Dispose();
    }

    private sealed record ScheduledReload(HotfixFileWatcherHostedService Service, long Generation);
}
