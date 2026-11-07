using Lakona.Rpc.Client;

namespace Lakona.Rpc.Tests;

public sealed class RpcDispatchSynchronizationContextTests
{
    [Fact]
    public async Task Single_producer_does_not_lose_wakeup_when_dispatcher_becomes_idle()
    {
        var context = new RpcDispatchSynchronizationContext();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        // Each completion wakes a producer on another thread while Drain returns
        // to idle. A lost handoff strands the next callback without any later
        // producer to wake the queue accidentally.
        await Task.Run(async () =>
        {
            for (var i = 0; i < 1_000_000; i++)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.Post(static state => ((TaskCompletionSource)state!).SetResult(), completion);
                await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
        }).WaitAsync(deadline.Token);
    }
}
