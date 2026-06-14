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
}
