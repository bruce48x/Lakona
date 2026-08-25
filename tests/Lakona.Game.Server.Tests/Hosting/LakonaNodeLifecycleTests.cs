using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaNodeLifecycleTests
{
    [Fact]
    public async Task Start_runs_stages_in_order_and_stop_runs_started_participants_in_reverse()
    {
        var events = new List<string>();
        var lifecycle = Create(
            Participant("admission", LakonaNodeLifecycleStage.Admission, events),
            Participant("modules", LakonaNodeLifecycleStage.ApplicationModules, events),
            Participant("membership", LakonaNodeLifecycleStage.Membership, events),
            Participant("hotfix", LakonaNodeLifecycleStage.Hotfix, events));

        await lifecycle.StartAsync(TestContext.Current.CancellationToken);
        await lifecycle.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "start:modules", "start:hotfix", "start:membership", "start:admission",
                "stop:admission", "stop:membership", "stop:hotfix", "stop:modules"
            ],
            events);
    }

    [Fact]
    public async Task Failed_start_rolls_back_only_started_participants_and_preserves_failure()
    {
        var events = new List<string>();
        var failure = new InvalidOperationException("membership failed");
        var lifecycle = Create(
            Participant("modules", LakonaNodeLifecycleStage.ApplicationModules, events),
            Participant("hotfix", LakonaNodeLifecycleStage.Hotfix, events),
            Participant("membership", LakonaNodeLifecycleStage.Membership, events, failure),
            Participant("admission", LakonaNodeLifecycleStage.Admission, events));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.StartAsync(TestContext.Current.CancellationToken));

        Assert.Same(failure, exception);
        Assert.Equal(
            ["start:modules", "start:hotfix", "start:membership", "stop:hotfix", "stop:modules"],
            events);
    }

    private static LakonaNodeLifecycle Create(params ILakonaNodeLifecycleParticipant[] participants) =>
        new(participants, NullLogger<LakonaNodeLifecycle>.Instance);

    private static ILakonaNodeLifecycleParticipant Participant(
        string name,
        LakonaNodeLifecycleStage stage,
        List<string> events,
        Exception? startFailure = null) =>
        new RecordingParticipant(name, stage, events, startFailure);

    private sealed class RecordingParticipant(
        string name,
        LakonaNodeLifecycleStage stage,
        List<string> events,
        Exception? startFailure) : ILakonaNodeLifecycleParticipant
    {
        public string Name => name;
        public LakonaNodeLifecycleStage Stage => stage;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add($"start:{name}");
            return startFailure is null ? Task.CompletedTask : Task.FromException(startFailure);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Add($"stop:{name}");
            return Task.CompletedTask;
        }
    }
}
