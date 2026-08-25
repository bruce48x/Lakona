using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            Participant("startup-actors", LakonaNodeLifecycleStage.StartupActors, events),
            Participant("actor-directory", LakonaNodeLifecycleStage.ActorDirectory, events),
            Participant("rpc", LakonaNodeLifecycleStage.ClusterTransport, events),
            Participant("modules", LakonaNodeLifecycleStage.ApplicationModules, events),
            Participant("membership", LakonaNodeLifecycleStage.Membership, events),
            Participant("hotfix", LakonaNodeLifecycleStage.Hotfix, events));

        await lifecycle.StartAsync(TestContext.Current.CancellationToken);
        await lifecycle.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "start:modules", "start:hotfix", "start:rpc", "start:membership",
                "start:actor-directory", "start:startup-actors", "start:admission",
                "stop:admission", "stop:startup-actors", "stop:actor-directory",
                "stop:membership", "stop:rpc", "stop:hotfix", "stop:modules"
            ],
            events);
    }

    [Theory]
    [InlineData((int)LakonaNodeLifecycleStage.ApplicationModules)]
    [InlineData((int)LakonaNodeLifecycleStage.Hotfix)]
    [InlineData((int)LakonaNodeLifecycleStage.ClusterTransport)]
    [InlineData((int)LakonaNodeLifecycleStage.Membership)]
    [InlineData((int)LakonaNodeLifecycleStage.ActorDirectory)]
    [InlineData((int)LakonaNodeLifecycleStage.StartupActors)]
    [InlineData((int)LakonaNodeLifecycleStage.Admission)]
    public async Task Start_failure_at_each_stage_starts_no_later_stage_and_stops_every_entered_stage(
        int failedStageValue)
    {
        var failedStage = (LakonaNodeLifecycleStage)failedStageValue;
        var events = new List<string>();
        var failure = new InvalidOperationException($"{failedStage} failed");
        var stages = AllStages();
        var lifecycle = Create(stages
            .Select(item => Participant(
                item.Name,
                item.Stage,
                events,
                startFailure: item.Stage == failedStage ? failure : null))
            .ToArray());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.StartAsync(TestContext.Current.CancellationToken));

        Assert.Same(failure, exception);
        var entered = stages.TakeWhile(item => item.Stage <= failedStage).ToArray();
        Assert.Equal(
            entered.Select(static item => $"start:{item.Name}")
                .Concat(entered.Reverse().Select(static item => $"stop:{item.Name}")),
            events);
    }

    [Fact]
    public async Task Failed_start_stops_the_failing_participant_and_prior_participants_and_preserves_failure()
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
            [
                "start:modules", "start:hotfix", "start:membership",
                "stop:membership", "stop:hotfix", "stop:modules"
            ],
            events);
    }

    [Fact]
    public async Task Failed_start_preserves_the_start_failure_when_rollback_also_fails_and_cannot_retry()
    {
        var events = new List<string>();
        var startFailure = new InvalidOperationException("membership start failed");
        var stopFailure = new ArgumentException("membership rollback failed");
        var lifecycle = Create(
            Participant("modules", LakonaNodeLifecycleStage.ApplicationModules, events),
            Participant(
                "membership",
                LakonaNodeLifecycleStage.Membership,
                events,
                startFailure: startFailure,
                stopFailure: stopFailure));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.StartAsync(TestContext.Current.CancellationToken));

        Assert.Same(startFailure, exception);
        Assert.Equal(
            ["start:modules", "start:membership", "stop:membership", "stop:modules"],
            events);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.StartAsync(TestContext.Current.CancellationToken));
        await lifecycle.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public async Task Canceled_stop_still_attempts_every_started_participant_in_reverse_order()
    {
        var events = new List<string>();
        var lifecycle = Create(
            Participant("modules", LakonaNodeLifecycleStage.ApplicationModules, events, stopOnCancellation: true),
            Participant("membership", LakonaNodeLifecycleStage.Membership, events, stopOnCancellation: true),
            Participant("admission", LakonaNodeLifecycleStage.Admission, events, stopOnCancellation: true));
        await lifecycle.StartAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            lifecycle.StopAsync(cancellation.Token));

        Assert.Equal(
            [
                "start:modules", "start:membership", "start:admission",
                "stop:admission", "stop:membership", "stop:modules"
            ],
            events);
    }

    [Fact]
    public async Task Stop_reports_every_failure_after_attempting_every_started_participant()
    {
        var events = new List<string>();
        var moduleFailure = new InvalidOperationException("module stop failed");
        var admissionFailure = new ArgumentException("admission stop failed");
        var lifecycle = Create(
            Participant(
                "modules",
                LakonaNodeLifecycleStage.ApplicationModules,
                events,
                stopFailure: moduleFailure),
            Participant("membership", LakonaNodeLifecycleStage.Membership, events),
            Participant(
                "admission",
                LakonaNodeLifecycleStage.Admission,
                events,
                stopFailure: admissionFailure));
        await lifecycle.StartAsync(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            lifecycle.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal([admissionFailure, moduleFailure], exception.InnerExceptions);
        Assert.Equal(
            [
                "start:modules", "start:membership", "start:admission",
                "stop:admission", "stop:membership", "stop:modules"
            ],
            events);
    }

    [Fact]
    public async Task Stop_is_idempotent_after_cleanup_failure()
    {
        var events = new List<string>();
        var lifecycle = Create(
            Participant(
                "modules",
                LakonaNodeLifecycleStage.ApplicationModules,
                events,
                stopFailure: new InvalidOperationException("stop failed")));
        await lifecycle.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AggregateException>(() =>
            lifecycle.StopAsync(TestContext.Current.CancellationToken));
        await lifecycle.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["start:modules", "stop:modules"], events);
    }

    [Theory]
    [InlineData((int)LakonaNodeLifecycleStage.ApplicationModules)]
    [InlineData((int)LakonaNodeLifecycleStage.Hotfix)]
    [InlineData((int)LakonaNodeLifecycleStage.ClusterTransport)]
    [InlineData((int)LakonaNodeLifecycleStage.Membership)]
    [InlineData((int)LakonaNodeLifecycleStage.ActorDirectory)]
    [InlineData((int)LakonaNodeLifecycleStage.StartupActors)]
    [InlineData((int)LakonaNodeLifecycleStage.Admission)]
    public async Task Stop_failure_at_each_stage_does_not_skip_any_other_stage(
        int failedStageValue)
    {
        var failedStage = (LakonaNodeLifecycleStage)failedStageValue;
        var events = new List<string>();
        var failure = new InvalidOperationException($"{failedStage} failed");
        var stages = AllStages();
        var lifecycle = Create(stages
            .Select(item => Participant(
                item.Name,
                item.Stage,
                events,
                stopFailure: item.Stage == failedStage ? failure : null))
            .ToArray());
        await lifecycle.StartAsync(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            lifecycle.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal([failure], exception.InnerExceptions);
        Assert.Equal(
            stages.Select(static item => $"start:{item.Name}")
                .Concat(stages.Reverse().Select(static item => $"stop:{item.Name}")),
            events);
    }

    [Fact]
    public async Task Start_can_only_be_called_once()
    {
        var events = new List<string>();
        var lifecycle = Create(
            Participant("modules", LakonaNodeLifecycleStage.ApplicationModules, events));
        await lifecycle.StartAsync(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("already started", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["start:modules"], events);
        await lifecycle.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Stop_before_start_permanently_closes_the_single_use_lifecycle()
    {
        var events = new List<string>();
        var lifecycle = Create(
            Participant("modules", LakonaNodeLifecycleStage.ApplicationModules, events));
        await lifecycle.StopAsync(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("stopped", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(events);
    }

    [Fact]
    public void Duplicate_stage_is_rejected_instead_of_creating_registration_order_dependencies()
    {
        var events = new List<string>();

        var exception = Assert.Throws<InvalidOperationException>(() => Create(
            Participant("membership-a", LakonaNodeLifecycleStage.Membership, events),
            Participant("membership-b", LakonaNodeLifecycleStage.Membership, events)));

        Assert.Contains(nameof(LakonaNodeLifecycleStage.Membership), exception.Message, StringComparison.Ordinal);
        Assert.Contains("membership-a", exception.Message, StringComparison.Ordinal);
        Assert.Contains("membership-b", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_cancellation_rolls_back_the_canceled_participant_and_prior_participants()
    {
        var events = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var lifecycle = Create(
            Participant("modules", LakonaNodeLifecycleStage.ApplicationModules, events),
            new DelegateParticipant(
                "hotfix",
                LakonaNodeLifecycleStage.Hotfix,
                async token =>
                {
                    events.Add("start:hotfix");
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                },
                _ =>
                {
                    events.Add("stop:hotfix");
                    return Task.CompletedTask;
                }));

        var start = lifecycle.StartAsync(cancellation.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Equal(
            ["start:modules", "start:hotfix", "stop:hotfix", "stop:modules"],
            events);
    }

    [Fact]
    public async Task Stop_waits_for_in_progress_start_before_cleaning_up()
    {
        var events = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = Create(
            new DelegateParticipant(
                "modules",
                LakonaNodeLifecycleStage.ApplicationModules,
                async _ =>
                {
                    events.Add("start:modules");
                    entered.SetResult();
                    await release.Task;
                },
                _ =>
                {
                    events.Add("stop:modules");
                    return Task.CompletedTask;
                }));

        var start = lifecycle.StartAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var stop = lifecycle.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(stop.IsCompleted);

        release.SetResult();
        await start;
        await stop;

        Assert.Equal(["start:modules", "stop:modules"], events);
    }

    [Fact]
    public async Task Host_bridge_uses_the_same_lifecycle_rollback_when_startup_fails()
    {
        var events = new List<string>();
        var failure = new InvalidOperationException("membership failed");
        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(static logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddSingleton<ILakonaNodeLifecycleParticipant>(
                    Participant("modules", LakonaNodeLifecycleStage.ApplicationModules, events));
                services.AddSingleton<ILakonaNodeLifecycleParticipant>(
                    Participant(
                        "membership",
                        LakonaNodeLifecycleStage.Membership,
                        events,
                        startFailure: failure));
                services.AddSingleton<ILakonaNodeLifecycleParticipant>(
                    Participant("admission", LakonaNodeLifecycleStage.Admission, events));
                services.AddSingleton<LakonaNodeLifecycle>();
                services.AddSingleton<IHostedService, LakonaNodeHostedService>();
            })
            .Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Same(failure, exception);
        Assert.Equal(
            ["start:modules", "start:membership", "stop:membership", "stop:modules"],
            events);
    }

    private static (string Name, LakonaNodeLifecycleStage Stage)[] AllStages() =>
    [
        ("modules", LakonaNodeLifecycleStage.ApplicationModules),
        ("hotfix", LakonaNodeLifecycleStage.Hotfix),
        ("rpc", LakonaNodeLifecycleStage.ClusterTransport),
        ("membership", LakonaNodeLifecycleStage.Membership),
        ("actor-directory", LakonaNodeLifecycleStage.ActorDirectory),
        ("startup-actors", LakonaNodeLifecycleStage.StartupActors),
        ("admission", LakonaNodeLifecycleStage.Admission)
    ];

    private static LakonaNodeLifecycle Create(params ILakonaNodeLifecycleParticipant[] participants) =>
        new(participants, NullLogger<LakonaNodeLifecycle>.Instance);

    private static ILakonaNodeLifecycleParticipant Participant(
        string name,
        LakonaNodeLifecycleStage stage,
        List<string> events,
        Exception? startFailure = null,
        Exception? stopFailure = null,
        bool stopOnCancellation = false) =>
        new RecordingParticipant(name, stage, events, startFailure, stopFailure, stopOnCancellation);

    private sealed class RecordingParticipant(
        string name,
        LakonaNodeLifecycleStage stage,
        List<string> events,
        Exception? startFailure,
        Exception? stopFailure,
        bool stopOnCancellation) : ILakonaNodeLifecycleParticipant
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
            if (stopOnCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return stopFailure is null ? Task.CompletedTask : Task.FromException(stopFailure);
        }
    }

    private sealed class DelegateParticipant(
        string name,
        LakonaNodeLifecycleStage stage,
        Func<CancellationToken, Task> start,
        Func<CancellationToken, Task> stop) : ILakonaNodeLifecycleParticipant
    {
        public string Name => name;
        public LakonaNodeLifecycleStage Stage => stage;
        public Task StartAsync(CancellationToken cancellationToken) => start(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) => stop(cancellationToken);
    }
}
