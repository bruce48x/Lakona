namespace Lakona.Hub;

internal sealed class HubUserSettingsPersistence : IDisposable
{
    private readonly Func<HubUserSettings> captureSettings;
    private readonly Action<HubUserSettings> writeSettings;
    private readonly TimeSpan delay;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly object writeGate = new();
    private CancellationTokenSource? pendingSave;
    private bool disposed;

    public HubUserSettingsPersistence(
        Func<HubUserSettings> captureSettings,
        Action<HubUserSettings> writeSettings,
        TimeSpan? delay = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        this.captureSettings = captureSettings;
        this.writeSettings = writeSettings;
        this.delay = delay ?? TimeSpan.FromMilliseconds(450);
        this.delayAsync = delayAsync ?? Task.Delay;
    }

    public event Action<Exception>? SaveFailed;

    public Task ScheduleSave()
    {
        if (disposed)
        {
            return Task.CompletedTask;
        }
        pendingSave?.Cancel();
        pendingSave?.Dispose();
        pendingSave = new CancellationTokenSource();
        return SaveAfterDelayAsync(pendingSave.Token);
    }

    public Exception? SaveNow()
    {
        if (disposed) return new ObjectDisposedException(nameof(HubUserSettingsPersistence));
        CancelPending();
        try
        {
            var settings = captureSettings();
            lock (writeGate)
            {
                writeSettings(settings);
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return ex;
        }
    }

    public void Dispose()
    {
        disposed = true;
        CancelPending();
        // Join only disk I/O: the async task may still need the UI thread to resume.
        lock (writeGate)
        {
            pendingSave?.Dispose();
            pendingSave = null;
        }
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await delayAsync(delay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var settings = captureSettings();
            await Task.Run(() =>
            {
                lock (writeGate)
                {
                    // Cancellation may have happened while waiting behind another save.
                    cancellationToken.ThrowIfCancellationRequested();
                    writeSettings(settings);
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SaveFailed?.Invoke(ex);
        }
    }

    private void CancelPending()
    {
        pendingSave?.Cancel();
    }
}
