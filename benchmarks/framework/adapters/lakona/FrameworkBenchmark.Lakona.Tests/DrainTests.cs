using FrameworkBenchmark.Contracts;
using FrameworkBenchmark.Lakona.Contracts;
using Xunit;

namespace FrameworkBenchmark.Lakona.Tests;

public sealed class DrainTests
{
    [Theory]
    [InlineData(10000, 50, true)]
    [InlineData(50, 10000, false)]
    public async Task OutstandingCallsObserveTheEarlierDeadline(int requestTimeout, int drainTimeout, bool drain)
    {
        var canceled = 0;
        async ValueTask<EchoResponse> Hold(EchoRequest request, CancellationToken token)
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { Interlocked.Increment(ref canceled); }
            throw new InvalidOperationException();
        }

        var result = await DriverProgram.MeasureAsync(Command(requestTimeout, drainTimeout), [Hold, Hold])
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, canceled);
        Assert.Equal(2, result.Outcomes.Started);
        Assert.Equal(drain ? 2 : 0, result.Outcomes.CanceledAtDrain);
        Assert.Equal(drain ? 0 : 2, result.Outcomes.TimedOut);
        Assert.Equal(0, result.Outcomes.Disconnected);
        Assert.Equal(0, result.Outcomes.Completed);
        Assert.Equal(0, result.Histogram.TotalCount);
    }

    [Fact]
    public async Task ResponseDuringDrainStillCompletesNormally()
    {
        async ValueTask<EchoResponse> Respond(EchoRequest request, CancellationToken token)
        {
            await Task.Delay(250, token);
            return new EchoResponse { RequestId = request.RequestId, Payload = request.Payload, TerminalNode = "frontdoor-1" };
        }

        var result = await DriverProgram.MeasureAsync(Command(10000, 10000), [Respond])
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, result.Outcomes.Started);
        Assert.Equal(1, result.Outcomes.Succeeded);
        Assert.Equal(1, result.Histogram.TotalCount);
        Assert.Equal(0, result.Outcomes.CanceledAtDrain);
    }

    [Fact]
    public async Task UnrelatedCancellationIsNotReportedAsDrainCancellation()
    {
        ValueTask<EchoResponse> Disconnect(EchoRequest request, CancellationToken token) =>
            throw new OperationCanceledException("connection stopped");
        var result = await DriverProgram.MeasureAsync(Command(10000, 10000), [Disconnect]);
        Assert.Equal(1, result.Outcomes.Disconnected);
        Assert.Equal(0, result.Outcomes.CanceledAtDrain);
        Assert.Equal(0, result.Outcomes.TimedOut);
    }

    private static CaseCommand Command(int requestTimeout, int drainTimeout) => new(
        "1", "test", "test", "lakona", "frontdoor.echo", 32, 2, 2, 7,
        new SuiteTiming(10000, 10000, 0, 100, requestTimeout, drainTimeout, 1000),
        new HistogramConfiguration("microseconds", 1, 60000000, 3), new Dictionary<string, string>());
}
