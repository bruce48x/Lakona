using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubUserSettingsPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveNow_WaitsForOldWriterAndPersistsLatestSettings(bool oldWriteFails)
    {
        var path = Path.Combine(Path.GetTempPath(), "Lakona.Hub.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        var store = new HubUserSettingsStore(path);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var current = Settings();
        var oldFinished = false;
        var overlapped = false;
        Exception? observed = null;
        using var persistence = new HubUserSettingsPersistence(() => current, settings =>
        {
            if (settings.Theme == HubThemePreference.System)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Old writer was not released.");
                try
                {
                    if (oldWriteFails) throw new IOException("Old save failed.");
                    store.Save(settings);
                }
                finally { oldFinished = true; }
            }
            else
            {
                overlapped = !oldFinished;
                store.Save(settings);
            }
        }, delayAsync: (_, _) => Task.CompletedTask);
        persistence.SaveFailed += error => observed = error;
        var pending = persistence.ScheduleSave();
        Task immediate = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            current = current with { Theme = HubThemePreference.Dark };
            var call = StartCall(() => Assert.Null(persistence.SaveNow()));
            immediate = call.Completion;
            await WaitForBlockedOrCompletedAsync(call.Thread, immediate);
            Assert.False(immediate.IsCompleted);
            Assert.False(overlapped);
            release.Set();
            await immediate.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(overlapped);
            Assert.Equal(HubThemePreference.Dark, store.Load(HubLanguage.English).Theme);
            if (oldWriteFails) Assert.IsType<IOException>(observed);
            else Assert.Null(observed);
        }
        finally
        {
            release.Set();
            _ = await Record.ExceptionAsync(() => immediate.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
            await pending.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public async Task Dispose_WaitsForRunningWriterAndPreventsFurtherSaves()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var finished = false;
        using var persistence = new HubUserSettingsPersistence(Settings, _ =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Writer was not released.");
            finished = true;
        }, delayAsync: (_, _) => Task.CompletedTask);
        var pending = persistence.ScheduleSave();
        Task disposal = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var call = StartCall(persistence.Dispose);
            disposal = call.Completion;
            await WaitForBlockedOrCompletedAsync(call.Thread, disposal);
            Assert.False(disposal.IsCompleted);
            Assert.False(finished);
            release.Set();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(finished);
            await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await persistence.ScheduleSave();
            Assert.IsType<ObjectDisposedException>(persistence.SaveNow());
        }
        finally
        {
            release.Set();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await pending.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledDelay_DoesNotCaptureOrWriteStaleSettings(bool dispose)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captures = 0;
        var writes = 0;
        using var persistence = new HubUserSettingsPersistence(
            () => { captures++; return Settings(); }, _ => writes++,
            delayAsync: (_, _) => release.Task); // Deliberately completes after cancellation.
        var pending = persistence.ScheduleSave();
        try
        {
            if (dispose) persistence.Dispose();
            else Assert.Null(persistence.SaveNow());
        }
        finally { release.TrySetResult(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(dispose ? 0 : 1, captures);
        Assert.Equal(dispose ? 0 : 1, writes);
    }

    [Fact]
    public void SaveNow_ReportsFailureAndAllowsNextSave()
    {
        var attempts = 0;
        using var persistence = new HubUserSettingsPersistence(Settings, _ =>
        {
            if (++attempts == 1) throw new IOException("Settings unavailable.");
        });
        Assert.IsType<IOException>(persistence.SaveNow());
        Assert.Null(persistence.SaveNow());
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousSaveOrDispose_DoesNotWaitForUiContinuation(bool dispose)
    {
        var context = new DeferredContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var writes = 0;
        using var persistence = new HubUserSettingsPersistence(() =>
        {
            Assert.Same(context, SynchronizationContext.Current);
            return Settings();
        }, _ =>
        {
            if (Interlocked.Increment(ref writes) != 1) return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Writer was not released.");
        }, delayAsync: (_, _) => Task.CompletedTask);
        Task pending = Task.CompletedTask;
        var call = StartCall(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                pending = persistence.ScheduleSave();
                entered.Task.GetAwaiter().GetResult();
                closing.TrySetResult();
                if (dispose) persistence.Dispose();
                else Assert.Null(persistence.SaveNow());
            }
            finally { SynchronizationContext.SetSynchronizationContext(null); }
        });
        try
        {
            await closing.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await WaitForBlockedOrCompletedAsync(call.Thread, call.Completion);
            Assert.False(call.Completion.IsCompleted);
            release.Set();
            await call.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await context.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(pending.IsCompleted);
            Assert.Equal(dispose ? 1 : 2, writes);
        }
        finally
        {
            release.Set();
            await context.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            context.Drain();
            await call.Completion.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await pending.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    private sealed class DeferredContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> callbacks = new();
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            callbacks.Enqueue(() => callback(state));
            Posted.TrySetResult();
        }
        public void Drain()
        {
            while (callbacks.TryDequeue(out var callback)) callback();
        }
    }

    private static (Thread Thread, Task Completion) StartCall(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true };
        thread.Start();
        return (thread, completion.Task);
    }

    private static async Task WaitForBlockedOrCompletedAsync(Thread thread, Task completion)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(5));
        // Observe the synchronous call actually waiting or returning. A "started"
        // signal alone would race the assertion against entry into the write lock.
        while (!completion.IsCompleted && (thread.ThreadState & ThreadState.WaitSleepJoin) == 0)
            await Task.Delay(1, guard.Token);
    }

    [Fact]
    public async Task ScheduleSave_CoalescesRapidChangesIntoOneWrite()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        using var persistence = new HubUserSettingsPersistence(
            Settings,
            _ => writes++,
            TimeSpan.FromSeconds(1),
            (_, cancellationToken) => release.Task.WaitAsync(cancellationToken));

        var first = persistence.ScheduleSave();
        var second = persistence.ScheduleSave();
        var third = persistence.ScheduleSave();
        release.SetResult();
        await Task.WhenAll(first, second, third);

        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task ScheduleSave_ReportsWriteFailures()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? observed = null;
        using var persistence = new HubUserSettingsPersistence(
            Settings,
            _ => throw new IOException("settings unavailable"),
            TimeSpan.FromSeconds(1),
            (_, cancellationToken) => release.Task.WaitAsync(cancellationToken));
        persistence.SaveFailed += exception => observed = exception;

        var save = persistence.ScheduleSave();
        release.SetResult();
        await save;

        Assert.IsType<IOException>(observed);
    }

    private static HubUserSettings Settings() =>
        new(HubLanguage.English, HubThemePreference.System, null, [], [], null, "Projects", null, null);
}
