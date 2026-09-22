using Lakona.Rpc.Server;
using Xunit.Abstractions;

namespace Lakona.Rpc.Tests;

public sealed class TrackedTaskCollectionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task WaitingAllocation_DoesNotGrowWithTheNumberOfActiveTasks()
    {
        // Only the synchronous entry to WaitAsync is measured on the current thread.
        // Tracking, cancellation and continuation scheduling are outside that window.
        await MeasureWaitAllocation(1, 32);
        await MeasureWaitAllocation(1024, 32);
        var small = await MeasureWaitAllocation(1, 64);
        var large = await MeasureWaitAllocation(1024, 64);
        output.WriteLine($"Runtime={Environment.Version}; OS={Environment.OSVersion}; CPUs={Environment.ProcessorCount}; serverGC={System.Runtime.GCSettings.IsServerGC}; warmup=32; samples=64; active=1/1024; bytes/wait={small}/{large}");
        // Allow fixed bookkeeping variance, but reject an array proportional to active tasks.
        Assert.True(large <= small + 512, $"Waiting allocation grew with active tasks: {small} -> {large} bytes.");
    }

    private static async Task<long> MeasureWaitAllocation(int count, int samples)
    {
        var tasks = new TrackedTaskCollection();
        var pending = Enumerable.Range(0, count)
            .Select(_ => new TaskCompletionSource()).ToArray();
        foreach (var task in pending) tasks.Track(task.Task);
        long allocated = 0;
        try
        {
            for (var i = 0; i < samples; i++)
            {
                using var cancellation = new CancellationTokenSource();
                var before = GC.GetAllocatedBytesForCurrentThread();
                var wait = tasks.WaitAsync(cancellation.Token);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.False(wait.IsCompleted);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.AsTask());
            }
        }
        finally
        {
            foreach (var task in pending) task.TrySetResult();
            await tasks.WaitAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        return allocated / samples;
    }
}
