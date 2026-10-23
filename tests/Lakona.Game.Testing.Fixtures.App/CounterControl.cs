namespace Lakona.Game.Testing.Fixtures.App;

// Node-owned synchronization for in-flight-call tests, not Hotfix business state.
public sealed class CounterControl
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => entered.Task;
    private int starts;
    public int Starts => Volatile.Read(ref starts);
    public void RecordStart() => Interlocked.Increment(ref starts);
    public Task Completed => completed.Task;
    public void Release() => released.TrySetResult();

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        entered.TrySetResult();
        await released.Task.WaitAsync(cancellationToken);
    }

    public async Task WaitIgnoringCancellationAsync()
    {
        entered.TrySetResult();
        await released.Task;
    }

    public void MarkCompleted() => completed.TrySetResult();
}
