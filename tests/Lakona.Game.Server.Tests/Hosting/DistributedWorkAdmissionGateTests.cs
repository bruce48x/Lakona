using Lakona.Game.Server.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class DistributedWorkAdmissionGateTests
{
    [Fact]
    public void GateStartsClosedAndRejectsDistributedWork()
    {
        var gate = new DistributedWorkAdmissionGate();

        Assert.False(gate.IsOpen);
        Assert.False(gate.TryEnter(out var admission));
        Assert.False(admission.IsAdmitted);
    }

    [Fact]
    public async Task ClosingRejectsNewWorkAndCompletesOnlyAfterAdmittedWorkDrains()
    {
        var gate = new DistributedWorkAdmissionGate();
        gate.Open();
        Assert.True(gate.TryEnter(out var first));
        Assert.True(gate.TryEnter(out var second));

        var drain = gate.CloseAndDrainAsync(
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken).AsTask();

        Assert.False(gate.IsOpen);
        Assert.False(gate.TryEnter(out _));
        Assert.False(drain.IsCompleted);

        gate.Exit(first);
        Assert.False(drain.IsCompleted);

        gate.Exit(second);
        Assert.True(await drain);
    }

    [Fact]
    public async Task AdmissionTokensCannotBeExitedTwiceOrAcrossGateGenerations()
    {
        var gate = new DistributedWorkAdmissionGate();
        gate.Open();
        Assert.True(gate.TryEnter(out var admission));
        gate.Exit(admission);

        Assert.Throws<InvalidOperationException>(() => gate.Exit(admission));

        await gate.CloseAndDrainAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        gate.Open();
        Assert.Throws<InvalidOperationException>(() => gate.Exit(admission));
    }

    [Fact]
    public async Task RepeatingOneTokenCannotDrainAnotherAdmissionInTheSameGeneration()
    {
        var gate = new DistributedWorkAdmissionGate();
        gate.Open();
        Assert.True(gate.TryEnter(out var first));
        Assert.True(gate.TryEnter(out var second));

        var drain = gate.CloseAndDrainAsync(
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken).AsTask();

        gate.Exit(first);
        Assert.Throws<InvalidOperationException>(() => gate.Exit(first));
        Assert.False(drain.IsCompleted);

        gate.Exit(second);
        Assert.True(await drain);
    }
}
