using Lakona.Game.LoadTesting;
using Lakona.Game.LoadTesting.Internal;
using Xunit;

namespace Lakona.Game.LoadTesting.Tests;

public sealed class LoadUserContextTests
{
    [Fact]
    public async Task MeasureAsync_Success_RecordsSucceededOperation()
    {
        var recorder = new LoadRunRecorder("chat", configuredUsers: 1);
        var context = new LoadUserContext(0, "user-1", recorder);

        await context.MeasureAsync("connect", _ => default, CancellationToken.None);

        var summary = recorder.CreateSummary(TimeSpan.FromMilliseconds(10));
        Assert.Equal(1, summary.TotalOperations);
        Assert.Equal(1, summary.SucceededOperations);
        Assert.Equal(0, summary.FailedOperations);
        Assert.Equal("connect", Assert.Single(summary.Latencies).OperationName);
    }

    [Fact]
    public async Task MeasureAsync_Failure_RecordsFailureAndRethrows()
    {
        var recorder = new LoadRunRecorder("chat", configuredUsers: 1);
        var context = new LoadUserContext(0, "user-1", recorder);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await context.MeasureAsync("login", _ => throw new InvalidOperationException("login rejected"), CancellationToken.None);
        });

        Assert.Equal("login rejected", ex.Message);
        var summary = recorder.CreateSummary(TimeSpan.FromMilliseconds(10));
        Assert.Equal(1, summary.TotalOperations);
        Assert.Equal(0, summary.SucceededOperations);
        Assert.Equal(1, summary.FailedOperations);
        var error = Assert.Single(summary.Errors);
        Assert.Equal("login", error.OperationName);
        Assert.Equal(nameof(InvalidOperationException), error.ExceptionType);
        Assert.Equal("login rejected", error.Message);
        Assert.Equal(1, error.Count);
    }

    [Fact]
    public async Task MeasureAsync_Canceled_RecordsCanceledAndRethrows()
    {
        var recorder = new LoadRunRecorder("chat", configuredUsers: 1);
        var context = new LoadUserContext(0, "user-1", recorder);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await context.MeasureAsync("send", token => throw new OperationCanceledException(token), cts.Token);
        });

        var summary = recorder.CreateSummary(TimeSpan.FromMilliseconds(10));
        Assert.Equal(1, summary.TotalOperations);
        Assert.Equal(0, summary.SucceededOperations);
        Assert.Equal(0, summary.FailedOperations);
        Assert.Equal(1, summary.CanceledOperations);
    }
}
