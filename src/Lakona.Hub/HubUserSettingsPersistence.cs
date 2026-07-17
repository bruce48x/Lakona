namespace Lakona.Hub;

internal sealed class HubUserSettingsPersistence : IDisposable
{
    private readonly Func<HubUserSettings> captureSettings;
    private readonly Action<HubUserSettings> writeSettings;
    private readonly TimeSpan delay;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
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
        CancelPending();
        try
        {
            writeSettings(captureSettings());
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
        pendingSave?.Dispose();
        pendingSave = null;
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await delayAsync(delay, cancellationToken);
            var settings = captureSettings();
            await Task.Run(() => writeSettings(settings), cancellationToken);
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
