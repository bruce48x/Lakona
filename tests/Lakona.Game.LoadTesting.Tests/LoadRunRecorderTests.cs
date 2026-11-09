using Lakona.Game.LoadTesting.Internal;
using Xunit;

namespace Lakona.Game.LoadTesting.Tests;

public sealed class LoadRunRecorderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Percentiles_cover_both_phases_regardless_of_order(bool slowFirst)
    {
        var recorder = new LoadRunRecorder("chat", 1);
        foreach (var milliseconds in slowFirst ? new[] { 100, 1 } : new[] { 1, 100 })
        {
            for (var index = 0; index < 1024; index++)
            {
                recorder.RecordSucceededOperation("send", TimeSpan.FromMilliseconds(milliseconds));
            }
        }

        var summary = recorder.CreateSummary(TimeSpan.FromSeconds(1));
        var latency = Assert.Single(summary.Latencies);
        Assert.Equal(2048, summary.TotalOperations);
        Assert.Equal(2048, latency.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(50.5), latency.Average);
        AssertApproximate(TimeSpan.FromMilliseconds(1), latency.P50);
        AssertApproximate(TimeSpan.FromMilliseconds(100), latency.P95);
        AssertApproximate(TimeSpan.FromMilliseconds(100), latency.P99);
    }

    [Fact]
    public void Late_extreme_requests_are_included_in_the_tail_and_exact_average()
    {
        var recorder = new LoadRunRecorder("chat", 1);
        for (var index = 0; index < 9800; index++)
        {
            recorder.RecordSucceededOperation("send", TimeSpan.FromMilliseconds(1));
        }
        for (var index = 0; index < 200; index++)
        {
            recorder.RecordSucceededOperation("send", TimeSpan.FromHours(1));
        }

        var latency = Assert.Single(recorder.CreateSummary(TimeSpan.Zero).Latencies);
        Assert.Equal(TimeSpan.FromTicks((9800L * TimeSpan.TicksPerMillisecond + 200L * TimeSpan.TicksPerHour) / 10000), latency.Average);
        AssertApproximate(TimeSpan.FromMilliseconds(1), latency.P50);
        AssertApproximate(TimeSpan.FromMilliseconds(1), latency.P95);
        AssertApproximate(TimeSpan.FromHours(1), latency.P99);
    }

    [Theory]
    [InlineData(99, 1, false)]
    [InlineData(98, 2, true)]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, true)]
    public void Percentiles_use_the_nearest_rank_at_the_tail_boundary(int fast, int slow, bool slowP99)
    {
        var recorder = new LoadRunRecorder("chat", 1);
        for (var index = 0; index < fast; index++)
        {
            recorder.RecordSucceededOperation("send", TimeSpan.FromTicks(1));
        }
        for (var index = 0; index < slow; index++)
        {
            recorder.RecordSucceededOperation("send", TimeSpan.MaxValue);
        }
        var latency = Assert.Single(recorder.CreateSummary(TimeSpan.Zero).Latencies);
        Assert.Equal(slowP99 ? TimeSpan.MaxValue : TimeSpan.FromTicks(1), latency.P99);
    }

    [Fact]
    public void Boundaries_cover_zero_every_exponent_and_maximum_without_overflow()
    {
        var boundaries = new HashSet<long> { 0, 1, 31, 32, 63, 64, long.MaxValue };
        // Both ends of every bucket, including the change of resolution.
        for (var shift = 0; shift <= 57; shift++)
        {
            for (var significand = 32; significand < 64; significand++)
            {
                var lower = (long)((ulong)significand << shift);
                var upper = (long)((((ulong)significand + 1) << shift) - 1);
                boundaries.Add(lower);
                boundaries.Add(upper);
            }
        }
        foreach (var ticks in boundaries)
        {
            var recorder = new LoadRunRecorder("chat", 1);
            var value = TimeSpan.FromTicks(ticks);
            // Multiple maximum samples force both signed overflow and unsigned carry.
            for (var index = 0; index < 3; index++)
            {
                recorder.RecordSucceededOperation("send", value);
            }
            var latency = Assert.Single(recorder.CreateSummary(TimeSpan.Zero).Latencies);
            Assert.Equal(value, latency.Average);
            AssertApproximate(value, latency.P50);
            AssertApproximate(value, latency.P95);
            AssertApproximate(value, latency.P99);
        }
    }

    [Fact]
    public void Negative_duration_is_rejected_without_recording_an_operation()
    {
        var recorder = new LoadRunRecorder("chat", 1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            recorder.RecordSucceededOperation("send", TimeSpan.FromTicks(-1)));
        var summary = recorder.CreateSummary(TimeSpan.Zero);
        Assert.Equal(0, summary.TotalOperations);
        Assert.Empty(summary.Latencies);
    }

    [Fact]
    public void Parallel_recording_preserves_counts_distribution_and_overflow_carries()
    {
        var recorder = new LoadRunRecorder("chat", 16);
        var failure = new InvalidOperationException("failed");
        var cancellation = new OperationCanceledException();
        Parallel.For(0, 100_000, index =>
        {
            recorder.RecordSucceededOperation("send", index % 2 == 0 ? TimeSpan.Zero : TimeSpan.MaxValue);
            recorder.RecordSucceededOperation("other", TimeSpan.FromTicks(7));
            recorder.RecordFailedOperation("send", TimeSpan.Zero, failure);
            recorder.RecordCanceledOperation("send", TimeSpan.Zero, cancellation);
        });

        var summary = recorder.CreateSummary(TimeSpan.Zero);
        Assert.Equal(400_000, summary.TotalOperations);
        Assert.Equal(200_000, summary.SucceededOperations);
        Assert.Equal(100_000, summary.FailedOperations);
        Assert.Equal(100_000, summary.CanceledOperations);
        var latency = Assert.Single(summary.Latencies, value => value.OperationName == "send");
        Assert.Equal(100_000, latency.Count);
        Assert.Equal(TimeSpan.FromTicks(long.MaxValue / 2), latency.Average);
        Assert.Equal(TimeSpan.Zero, latency.P50);
        Assert.Equal(TimeSpan.MaxValue, latency.P95);
        Assert.Equal(TimeSpan.MaxValue, latency.P99);
        var other = Assert.Single(summary.Latencies, value => value.OperationName == "other");
        Assert.Equal(TimeSpan.FromTicks(7), other.Average);
        Assert.Equal(TimeSpan.FromTicks(7), other.P99);
        Assert.Equal(summary.Latencies, recorder.CreateSummary(TimeSpan.Zero).Latencies);
    }

    [Fact]
    public void Empty_failed_and_canceled_operations_do_not_invent_latency_samples()
    {
        var recorder = new LoadRunRecorder("chat", 1);
        Assert.Empty(recorder.CreateSummary(TimeSpan.Zero).Latencies);
        recorder.RecordFailedOperation("send", TimeSpan.MaxValue, new InvalidOperationException());
        recorder.RecordCanceledOperation("send", TimeSpan.MaxValue, new OperationCanceledException());
        var summary = recorder.CreateSummary(TimeSpan.Zero);
        Assert.Equal(2, summary.TotalOperations);
        Assert.Empty(summary.Latencies);
    }

    [Fact]
    public void Recording_uses_fixed_memory_without_per_request_allocations()
    {
        var recorder = new LoadRunRecorder("chat", 1);
        recorder.RecordSucceededOperation("warmup", TimeSpan.Zero);
        var beforeOperation = GC.GetAllocatedBytesForCurrentThread();
        recorder.RecordSucceededOperation("send", TimeSpan.Zero);
        var operationBytes = GC.GetAllocatedBytesForCurrentThread() - beforeOperation;
        Assert.InRange(operationBytes, 1, 20 * 1024);

        var beforeRecording = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000_000; index++)
        {
            recorder.RecordSucceededOperation("send", TimeSpan.FromTicks(index));
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - beforeRecording);
        Assert.Equal(1_000_002, recorder.CreateSummary(TimeSpan.Zero).SucceededOperations);
    }

    private static void AssertApproximate(TimeSpan expected, TimeSpan actual)
    {
        Assert.True(actual >= expected, $"{actual.Ticks} underestimates {expected.Ticks}");
        // Integer arithmetic stays exact at TimeSpan.MaxValue.
        Assert.True((UInt128)(ulong)(actual.Ticks - expected.Ticks) * 32 <= (ulong)expected.Ticks,
            $"{actual.Ticks} exceeds the 1/32 error bound for {expected.Ticks}");
        if (expected.Ticks < 64)
        {
            Assert.Equal(expected, actual);
        }
    }
}
