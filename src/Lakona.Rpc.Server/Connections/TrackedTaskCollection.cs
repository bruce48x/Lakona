namespace Lakona.Rpc.Server;

internal sealed class TrackedTaskCollection
{
    private readonly object _sync = new();
    private int _activeCount;
    private TaskCompletionSource<object?> _drained = CreateCompletedSignal();

    public void Track(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_sync)
        {
            if (_activeCount++ == 0)
                _drained = CreatePendingSignal();
        }

        _ = task.ContinueWith(
            static (_, state) => ((TrackedTaskCollection)state!).OnTaskCompleted(),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async ValueTask WaitAsync()
    {
        while (true)
        {
            Task waitTask;
            lock (_sync)
            {
                if (_activeCount == 0)
                    return;

                waitTask = _drained.Task;
            }

            try
            {
                await waitTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private void OnTaskCompleted()
    {
        TaskCompletionSource<object?>? drainedToComplete = null;
        lock (_sync)
        {
            if (--_activeCount == 0)
                drainedToComplete = _drained;
        }

        drainedToComplete?.TrySetResult(null);
    }

    private static TaskCompletionSource<object?> CreatePendingSignal()
    {
        return new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static TaskCompletionSource<object?> CreateCompletedSignal()
    {
        var completed = CreatePendingSignal();
        completed.TrySetResult(null);
        return completed;
    }
}
