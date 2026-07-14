using Lakona.Game.LoadTesting.Internal;
using Xunit;

namespace Lakona.Game.LoadTesting.Tests;

public sealed class LoadRunRecorderTests
{
    [Fact]
    public void CreateSummary_KeepsCountsWhileBoundingBufferedLatencySamples()
    {
        var recorder = new LoadRunRecorder("chat", configuredUsers: 1);
        var total = LoadRunRecorder.MaxLatencySamplesPerOperation + 250;

        for (var index = 0; index < total; index++)
        {
            recorder.RecordSucceededOperation("send", TimeSpan.FromMilliseconds(index + 1));
        }

        var summary = recorder.CreateSummary(TimeSpan.FromSeconds(1));

        Assert.Equal(total, summary.TotalOperations);
        Assert.Equal(total, summary.SucceededOperations);
        Assert.Equal(total, Assert.Single(summary.Latencies).Count);
        Assert.True(recorder.BufferedLatencySampleCount <= LoadRunRecorder.MaxLatencySamplesPerOperation);
    }

    [Fact]
    public void Parallel_recording_publishes_at_most_the_sample_capacity()
    {
        var recorder = new LoadRunRecorder("chat", configuredUsers: 16);

        Parallel.For(0, 100_000, index =>
            recorder.RecordSucceededOperation("send", TimeSpan.FromTicks(index + 1)));
        var summary = recorder.CreateSummary(TimeSpan.FromSeconds(1));

        Assert.Equal(100_000, summary.TotalOperations);
        Assert.Equal(LoadRunRecorder.MaxLatencySamplesPerOperation, recorder.BufferedLatencySampleCount);
        Assert.Single(summary.Latencies);
    }
}
