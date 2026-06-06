using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Diagnostics;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests;

public sealed class ActorRuntimeTests
{
    [Fact]
    public async Task ActorRuntime_supports_typed_actor_base()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();

        var result = await runtime.AskAsync<TypedRoomActor, string>(
            ActorId.From("room/alpha"),
            static (actor, ct) => actor.EchoAsync("joined", ct),
            cancellationToken);

        Assert.Equal("room/alpha:joined", result);
    }

    [Fact]
    public void AddLakonaGameServerActors_registers_LakonaActor_backed_runtime()
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();

        Assert.IsType<LakonaActorRuntime>(provider.GetRequiredService<IActorRuntime>());
    }

    [Fact]
    public void AddLakonaGameServerActors_registers_actor_directory_defaults()
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();

        Assert.IsType<InMemoryActorDirectory>(provider.GetRequiredService<IActorDirectory>());
        Assert.IsType<InMemoryActorDirectoryCache>(provider.GetRequiredService<IActorDirectoryCache>());
    }

    [Fact]
    public void AddLakonaGameServerActors_registers_local_actor_node_identity_default()
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();

        Assert.Equal(new NodeId("local"), provider.GetRequiredService<LocalActorNodeIdentity>().NodeId);
    }

    [Fact]
    public void AddLakonaGameServerActors_preserves_preconfigured_local_actor_node_identity()
    {
        var node = new NodeId("node-a");

        using var provider = new ServiceCollection()
            .AddSingleton(new LocalActorNodeIdentity(node))
            .AddLakonaGameServerActors()
            .BuildServiceProvider();

        Assert.Equal(node, provider.GetRequiredService<LocalActorNodeIdentity>().NodeId);
    }

    [Fact]
    public async Task AskAsync_runs_messages_serially_for_same_actor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("counter/1");

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => runtime.AskAsync<CounterActor, int>(
                id,
                static async (actor, ct) =>
                {
                    await actor.IncrementAsync(ct);
                    return actor.Value;
                },
                cancellationToken).AsTask())
            .ToArray();

        await Task.WhenAll(tasks);

        var value = await runtime.AskAsync<CounterActor, int>(
            id,
            static (actor, _) => ValueTask.FromResult(actor.Value),
            cancellationToken);

        Assert.Equal(100, value);
    }

    [Fact]
    public async Task Same_actor_reentrant_call_executes_without_deadlock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("reentrant/1");

        var value = await runtime.AskAsync<ReentrantActor, int>(
            id,
            static (actor, ct) => actor.CallSelfAsync(ct),
            cancellationToken);

        Assert.Equal(2, value);
    }

    [Fact]
    public async Task Same_actor_id_cannot_be_reused_for_different_actor_type()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("shared/1");

        await runtime.GetOrCreateAsync<CounterActor>(id, cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.GetOrCreateAsync<ReentrantActor>(id, cancellationToken));
    }

    [Fact]
    public async Task Slow_message_diagnostic_maps_LakonaActor_event_to_LakonaGame_actor_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var observed = new TaskCompletionSource<ActorSlowMessageDiagnostic>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var id = ActorId.From("slow/1");

        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors(options =>
            {
                options.SlowMessageThreshold = TimeSpan.FromMilliseconds(1);
                options.SlowMessageHandler = diagnostic => observed.TrySetResult(diagnostic);
            })
            .BuildServiceProvider();

        var runtime = provider.GetRequiredService<IActorRuntime>();

        await runtime.TellAsync<SlowActor>(
            id,
            static (actor, ct) => actor.DelayAsync(TimeSpan.FromMilliseconds(50), ct),
            cancellationToken);

        var diagnostic = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        Assert.Equal(id, diagnostic.ActorId);
        Assert.True(diagnostic.Elapsed >= TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Call_timeout_diagnostic_maps_reason_and_actor_ids()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var observed = new TaskCompletionSource<ActorCallTimeoutDiagnostic>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var id = ActorId.From("timeout/1");

        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors(options =>
            {
                options.CallTimeout = TimeSpan.FromMilliseconds(20);
                options.CallTimeoutHandler = diagnostic => observed.TrySetResult(diagnostic);
            })
            .BuildServiceProvider();

        var runtime = provider.GetRequiredService<IActorRuntime>();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await runtime.AskAsync<SlowActor, int>(
                id,
                static async (actor, ct) =>
                {
                    await actor.DelayAsync(TimeSpan.FromMilliseconds(200), ct);
                    return 1;
                },
                cancellationToken));

        var diagnostic = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        Assert.Equal(id, diagnostic.Target);
        Assert.Equal(ActorCallTimeoutReason.ResponseTimeout, diagnostic.Reason);
    }

    [Fact]
    public async Task TryTell_returns_mailbox_full_without_waiting_for_capacity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = ActorId.From("backpressure/1");

        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors(options => options.MailboxCapacity = 2)
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();

        var blocking = runtime.TellAsync<BlockingActor>(
            id,
            (actor, ct) => actor.BlockAsync(entered, release.Task, ct),
            cancellationToken).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        var first = runtime.TryTell<BlockingActor>(
            id,
            static (actor, _) =>
            {
                actor.Count++;
                return ValueTask.CompletedTask;
            },
            cancellationToken);
        var second = runtime.TryTell<BlockingActor>(
            id,
            static (actor, _) =>
            {
                actor.Count++;
                return ValueTask.CompletedTask;
            },
            cancellationToken);

        release.SetResult();
        await blocking;

        var count = await runtime.AskAsync<BlockingActor, int>(
            id,
            static (actor, _) => ValueTask.FromResult(actor.Count),
            cancellationToken);

        Assert.Equal(ActorTellResult.Accepted, first);
        Assert.Equal(ActorTellResult.MailboxFull, second);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task StopAsync_drains_and_removes_actor_from_runtime_registry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("stop/1");

        await runtime.TellAsync<CounterActor>(
            id,
            static async (actor, ct) =>
            {
                await actor.IncrementAsync(ct);
            },
            cancellationToken);

        await runtime.StopAsync(id);

        var value = await runtime.AskAsync<CounterActor, int>(
            id,
            static (actor, _) => ValueTask.FromResult(actor.Value),
            cancellationToken);

        Assert.Equal(0, value);
    }

    [Fact]
    public async Task StopAsync_with_timeout_returns_timed_out_when_actor_does_not_drain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("stop-timeout/1");

        var blocking = runtime.TellAsync<BlockingActor>(
            id,
            (actor, ct) => actor.BlockAsync(entered, release.Task, ct),
            cancellationToken).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        var outcome = await runtime.StopAsync(id, TimeSpan.FromMilliseconds(20));

        release.SetResult();
        await blocking;

        Assert.Equal(ActorStopOutcome.TimedOut, outcome);
    }

    [Fact]
    public async Task TryGetMailboxMetrics_returns_LakonaGame_owned_metrics_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = ActorId.From("metrics/1");

        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors(options => options.MailboxCapacity = 3)
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();

        Assert.False(runtime.TryGetMailboxMetrics(id, out _));

        var blocking = runtime.TellAsync<BlockingActor>(
            id,
            (actor, ct) => actor.BlockAsync(entered, release.Task, ct),
            cancellationToken).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        var tellResult = runtime.TryTell<BlockingActor>(
            id,
            static (actor, _) =>
            {
                actor.Count++;
                return ValueTask.CompletedTask;
            },
            cancellationToken);

        Assert.True(runtime.TryGetMailboxMetrics(id, out var metrics));
        Assert.Equal(ActorTellResult.Accepted, tellResult);
        Assert.Equal(3, metrics.Capacity);
        Assert.True(metrics.QueuedCount >= 1);
        Assert.True(metrics.EnqueuedCount >= 2);
        Assert.False(metrics.IsCompleted);

        release.SetResult();
        await blocking;
    }

    [Fact]
    public async Task RegisterTimer_dispatches_ticks_through_actor_mailbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("timer/1");

        await using var timer = runtime.RegisterTimer<TimerActor>(
            id,
            TimeSpan.FromMilliseconds(10),
            null,
            static (actor, _) =>
            {
                actor.Ticks++;
                return ValueTask.CompletedTask;
            });

        var ticks = await WaitForAsync(
            async () => await runtime.AskAsync<TimerActor, int>(
                id,
                static (actor, _) => ValueTask.FromResult(actor.Ticks),
                cancellationToken),
            value => value >= 1,
            cancellationToken);

        Assert.True(ticks >= 1);
    }

    [Fact]
    public async Task StopAsync_disposes_registered_timer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("timer-stop/1");

        await using var timer = runtime.RegisterTimer<TimerActor>(
            id,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10),
            static (actor, _) =>
            {
                actor.Ticks++;
                return ValueTask.CompletedTask;
            });

        await WaitForAsync(
            async () => await runtime.AskAsync<TimerActor, int>(
                id,
                static (actor, _) => ValueTask.FromResult(actor.Ticks),
                cancellationToken),
            value => value >= 1,
            cancellationToken);

        await runtime.StopAsync(id);
        await Task.Delay(80, cancellationToken);

        var recreatedTicks = await runtime.AskAsync<TimerActor, int>(
            id,
            static (actor, _) => ValueTask.FromResult(actor.Ticks),
            cancellationToken);

        Assert.Equal(0, recreatedTicks);
    }

    [Fact]
    public async Task StopAsync_prevents_queued_timer_registration_from_surviving_stop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("timer-stop-race/1");

        await using var timer = runtime.RegisterTimer<TimerActor>(
            id,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10),
            static (actor, _) =>
            {
                actor.Ticks++;
                return ValueTask.CompletedTask;
            });

        await runtime.StopAsync(id);
        await Task.Delay(80, cancellationToken);

        var recreatedTicks = await runtime.AskAsync<TimerActor, int>(
            id,
            static (actor, _) => ValueTask.FromResult(actor.Ticks),
            cancellationToken);

        Assert.Equal(0, recreatedTicks);
    }

    [Fact]
    public async Task StopAsync_runs_actor_deactivation_hook()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        DeactivationActor.Deactivations = 0;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("deactivate/1");

        await runtime.GetOrCreateAsync<DeactivationActor>(id, cancellationToken);

        await runtime.StopAsync(id);

        Assert.Equal(1, DeactivationActor.Deactivations);
    }

    [Fact]
    public async Task RuntimeDispose_does_not_run_actor_deactivation_hook()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        DeactivationActor.Deactivations = 0;
        await using (var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider())
        {
            var runtime = provider.GetRequiredService<IActorRuntime>();
            await runtime.GetOrCreateAsync<DeactivationActor>(
                ActorId.From("dispose-no-deactivate/1"),
                cancellationToken);
        }

        Assert.Equal(0, DeactivationActor.Deactivations);
    }

    [Fact]
    public async Task StopAsync_with_timeout_returns_timed_out_when_deactivation_cannot_run()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DeactivationActor.Deactivations = 0;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("deactivate-timeout/1");

        var blocking = runtime.TellAsync<DeactivationActor>(
            id,
            (actor, ct) => actor.BlockAsync(entered, release.Task, ct),
            cancellationToken).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        var outcome = await runtime.StopAsync(id, TimeSpan.FromMilliseconds(20));

        release.SetResult();
        await blocking;

        Assert.Equal(ActorStopOutcome.TimedOut, outcome);
        Assert.Equal(0, DeactivationActor.Deactivations);
    }

    [Fact]
    public async Task Message_recording_interceptor_logs_messages_to_store()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddMessageRecording()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var store = provider.GetRequiredService<IMessageLogStore>();
        var id = ActorId.From("record/1");

        await runtime.AskAsync<CounterActor, int>(
            id,
            static async (actor, ct) =>
            {
                await actor.IncrementAsync(ct);
                return actor.Value;
            },
            cancellationToken);

        var log = await store.GetLogAsync(id, cancellationToken);
        Assert.NotEmpty(log);
        Assert.Null(log[0].Error);
    }

    [Fact]
    public async Task Message_recording_interceptor_logs_errors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddMessageRecording()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var store = provider.GetRequiredService<IMessageLogStore>();
        var id = ActorId.From("record-error/1");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.AskAsync<CounterActor, int>(
                id,
                static (actor, _) => throw new InvalidOperationException("test error"),
                cancellationToken));

        var log = await store.GetLogAsync(id, cancellationToken);
        Assert.NotEmpty(log);
        Assert.NotNull(log[0].Error);
        Assert.Contains("InvalidOperationException", log[0].Error, StringComparison.Ordinal);
    }

    private sealed class CounterActor : GameActor
    {
        public int Value { get; private set; }

        public async ValueTask IncrementAsync(CancellationToken cancellationToken)
        {
            var before = Value;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Value = before + 1;
        }
    }

    private sealed class ReentrantActor : GameActor
    {
        private int _value;

        public async ValueTask<int> CallSelfAsync(CancellationToken cancellationToken)
        {
            _value++;
            await Context.Runtime.TellAsync<ReentrantActor>(
                Context.Id,
                static (actor, _) =>
                {
                    actor._value++;
                    return ValueTask.CompletedTask;
                },
                cancellationToken);

            return _value;
        }
    }

    private sealed class SlowActor : GameActor
    {
        public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    private sealed class BlockingActor : GameActor
    {
        public int Count { get; set; }

        public async ValueTask BlockAsync(
            TaskCompletionSource entered,
            Task release,
            CancellationToken cancellationToken)
        {
            entered.SetResult();
            await release.WaitAsync(cancellationToken);
        }
    }

    private sealed class TimerActor : GameActor
    {
        public int Ticks { get; set; }
    }

    private sealed class DeactivationActor : GameActor
    {
        public static int Deactivations { get; set; }

        protected override ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
        {
            Deactivations++;
            return ValueTask.CompletedTask;
        }

        public async ValueTask BlockAsync(
            TaskCompletionSource entered,
            Task release,
            CancellationToken cancellationToken)
        {
            entered.SetResult();
            await release.WaitAsync(cancellationToken);
        }
    }

    private static async Task<T> WaitForAsync<T>(
        Func<Task<T>> read,
        Func<T, bool> predicate,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        while (true)
        {
            var value = await read();

            if (predicate(value))
            {
                return value;
            }

            await Task.Delay(10, linked.Token);
        }
    }

    private static ServiceProvider CreateProvider()
    {
        return new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
    }
}

public readonly record struct RuntimeRoomId(string Value);

public sealed class TypedRoomActor : Actor<RuntimeRoomId>
{
    public ValueTask<string> EchoAsync(string value, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult($"{Context.Id.Value}:{value}");
    }
}
