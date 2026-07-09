namespace Lakona.Game.Server.Health;

internal sealed class LakonaHealthHttpRequestTracker
{
    private readonly object _lock = new();
    private readonly HashSet<Task> _inFlight = [];

    public Task Track(Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? task = null;
        task = Task.Run(async () =>
        {
            await start.Task.ConfigureAwait(false);
            try
            {
                await handler().ConfigureAwait(false);
            }
            finally
            {
                lock (_lock)
                {
                    if (task is not null)
                    {
                        _inFlight.Remove(task);
                    }
                }
            }
        });

        lock (_lock)
        {
            _inFlight.Add(task);
        }

        start.SetResult();
        return task;
    }

    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task[] pending;
            lock (_lock)
            {
                if (_inFlight.Count == 0)
                {
                    return;
                }

                pending = _inFlight.ToArray();
            }

            await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
