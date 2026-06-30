using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Diagnostics;
using Microsoft.Extensions.Configuration;
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
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("room/alpha");

        await lifecycle.CreateLocalAsync<TypedRoomActor>(id, cancellationToken: cancellationToken);

        var result = await runtime.AskAsync<TypedRoomActor, string>(
            id,
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
        Assert.Same(
            provider.GetRequiredService<IActorRuntime>(),
            provider.GetRequiredService<IActorLifecycle>());
    }

    [Fact]
    public void AddLakonaGameServerActors_reads_configuration_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Actors:MailboxCapacity"] = "12",
                ["Lakona:Actors:CallTimeoutSeconds"] = "5",
                ["Lakona:Actors:SlowMessageThresholdSeconds"] = "1"
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddLakonaGameServerActors(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<ActorRuntimeOptions>();
        Assert.Equal(12, options.MailboxCapacity);
        Assert.Equal(TimeSpan.FromSeconds(5), options.CallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), options.SlowMessageThreshold);
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
    public void AddLakonaGameServerActors_does_not_register_remote_actor_serializer_without_cluster_serializer()
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();

        Assert.Null(provider.GetService<IRemoteActorSerializer>());
    }

    [Fact]
    public async Task GetActiveActorIds_returns_active_actor_ids_for_requested_type()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();

        await lifecycle.CreateLocalAsync<TestActor>(ActorId.From("a"), cancellationToken: cancellationToken);
        await lifecycle.CreateLocalAsync<TestActor>(ActorId.From("b"), cancellationToken: cancellationToken);

        var ids = runtime.GetActiveActorIds(typeof(TestActor));

        Assert.Equal(new[] { "a", "b" }, ids.Select(id => id.Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Actor_diagnostics_snapshot_aggregates_by_actor_type_without_actor_ids()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors(options => options.MailboxCapacity = 2)
            .BuildServiceProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var firstSecretActorId = "secret-actor-id-a";
        var secondSecretActorId = "secret-actor-id-b";
        var firstId = ActorId.From(firstSecretActorId);
        var secondId = ActorId.From(secondSecretActorId);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await lifecycle.CreateLocalAsync<BlockingActor>(firstId, cancellationToken: cancellationToken);
        await lifecycle.CreateLocalAsync<BlockingActor>(secondId, cancellationToken: cancellationToken);

        var firstBlocking = runtime.TellAsync<BlockingActor>(
            firstId,
            (actor, ct) => actor.BlockAsync(firstEntered, release.Task, ct),
            cancellationToken).AsTask();
        var secondBlocking = runtime.TellAsync<BlockingActor>(
            secondId,
            (actor, ct) => actor.BlockAsync(secondEntered, release.Task, ct),
            cancellationToken).AsTask();
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        var firstAccepted = runtime.TryTell<BlockingActor>(
            firstId,
            static (actor, _) =>
            {
                actor.Count++;
                return ValueTask.CompletedTask;
            },
            cancellationToken);
        var firstRejected = runtime.TryTell<BlockingActor>(
            firstId,
            static (actor, _) =>
            {
                actor.Count++;
                return ValueTask.CompletedTask;
            },
            cancellationToken);
        var secondAccepted = runtime.TryTell<BlockingActor>(
            secondId,
            static (actor, _) =>
            {
                actor.Count++;
                return ValueTask.CompletedTask;
            },
            cancellationToken);
        var secondRejected = runtime.TryTell<BlockingActor>(
            secondId,
            static (actor, _) =>
            {
                actor.Count++;
                return ValueTask.CompletedTask;
            },
            cancellationToken);
        var secondRejectedAgain = runtime.TryTell<BlockingActor>(
            secondId,
            static (actor, _) =>
            {
                actor.Count++;
                return ValueTask.CompletedTask;
            },
            cancellationToken);

        var snapshot = runtime.GetDiagnosticsSnapshot();
        release.SetResult();
        await Task.WhenAll(firstBlocking, secondBlocking);

        var actorType = Assert.Single(snapshot.ActorTypes);

        Assert.Equal(ActorTellResult.Accepted, firstAccepted);
        Assert.Equal(ActorTellResult.MailboxFull, firstRejected);
        Assert.Equal(ActorTellResult.Accepted, secondAccepted);
        Assert.Equal(ActorTellResult.MailboxFull, secondRejected);
        Assert.Equal(ActorTellResult.MailboxFull, secondRejectedAgain);
        Assert.Equal(typeof(BlockingActor).FullName, actorType.ActorType);
        Assert.Equal(2, actorType.ActiveCount);
        Assert.Equal(2, actorType.MailboxQueuedSum);
        Assert.Equal(1, actorType.MailboxQueuedMax);
        Assert.Equal(6, actorType.MailboxEnqueuedCount);
        Assert.Equal(3, actorType.MailboxEnqueuedMax);
        Assert.Equal(2, actorType.MailboxProcessedCount);
        Assert.Equal(1, actorType.MailboxProcessedMax);
        Assert.Equal(3, actorType.MailboxRejectedCount);
        Assert.Equal(2, actorType.MailboxRejectedMax);
        Assert.NotEqual(actorType.MailboxQueuedSum, actorType.MailboxQueuedMax);
        Assert.NotEqual(actorType.MailboxEnqueuedCount, actorType.MailboxEnqueuedMax);
        Assert.NotEqual(actorType.MailboxProcessedCount, actorType.MailboxProcessedMax);
        Assert.NotEqual(actorType.MailboxRejectedCount, actorType.MailboxRejectedMax);
        Assert.DoesNotContain(firstSecretActorId, snapshot.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secondSecretActorId, snapshot.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dynamic_TellAsync_dispatches_to_requested_actor_type()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("dynamic");

        await lifecycle.CreateLocalAsync<TestActor>(id, cancellationToken: cancellationToken);

        await runtime.TellAsync(
            typeof(TestActor),
            id,
            static (actor, _) =>
            {
                ((TestActor)actor).Messages.Add("dynamic");
                return default;
            },
            cancellationToken);

        var typed = await runtime.GetOrCreateAsync<TestActor>(id, cancellationToken);
        Assert.Contains("dynamic", typed.Messages);
    }

    [Fact]
    public async Task Actor_lifecycle_creates_local_actor_explicitly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("explicit");

        var created = await lifecycle.CreateLocalAsync<TestActor>(id, cancellationToken: cancellationToken);
        Assert.Equal(ActorCreateLocalStatus.Created, created.Status);

        var reply = await runtime.AskAsync<TestActor, string>(
            id,
            static (actor, ct) => actor.EchoAsync("ok", ct),
            cancellationToken);

        Assert.Equal("explicit:ok", reply);
    }

    [Fact]
    public async Task Ask_does_not_create_missing_actor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();

        var ex = await Assert.ThrowsAnyAsync<ActorCallException>(async () =>
            await runtime.AskAsync<TestActor, string>(
                ActorId.From("missing"),
                static (actor, ct) => actor.EchoAsync("no", ct),
                cancellationToken));

        Assert.Equal(ActorCallStatus.ActorNotFound, ex.Status);
        Assert.Empty(runtime.GetActiveActorIds(typeof(TestActor)));
    }

    [Fact]
    public async Task Destroy_local_actor_removes_actor_without_recreating_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("destroy-me");

        await lifecycle.CreateLocalAsync<TestActor>(id, cancellationToken: cancellationToken);
        var destroyed = await lifecycle.DestroyLocalAsync<TestActor>(
            id,
            new ActorDestroyOptions { DrainTimeout = TimeSpan.FromSeconds(1) },
            cancellationToken);

        Assert.Equal(ActorDestroyLocalStatus.Destroyed, destroyed.Status);
        Assert.Empty(runtime.GetActiveActorIds(typeof(TestActor)));
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
    public void AddLakonaGameServer_uses_configured_node_id_for_local_actor_identity()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "battle-1"
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();

        Assert.Equal(new NodeId("battle-1"), provider.GetRequiredService<LocalActorNodeIdentity>().NodeId);
    }

    [Fact]
    public void AddLakonaGameServer_uses_factory_registered_runtime_options_for_local_actor_identity()
    {
        using var provider = new ServiceCollection()
            .AddSingleton(_ => new LakonaGameRuntimeOptions
            {
                Node = new LakonaGameNodeOptions
                {
                    Id = "factory-node"
                }
            })
            .AddLakonaGameServer(new LakonaGameHostingOptions())
            .BuildServiceProvider();

        Assert.Equal("factory-node", provider.GetRequiredService<LakonaGameRuntimeOptions>().Node.Id);
        Assert.Equal(new NodeId("factory-node"), provider.GetRequiredService<LocalActorNodeIdentity>().NodeId);
    }

    [Fact]
    public async Task AskAsync_runs_messages_serially_for_same_actor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("counter/1");
        await lifecycle.CreateLocalAsync<CounterActor>(id, cancellationToken: cancellationToken);

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
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("reentrant/1");
        await lifecycle.CreateLocalAsync<ReentrantActor>(id, cancellationToken: cancellationToken);

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

        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        await lifecycle.CreateLocalAsync<SlowActor>(id, cancellationToken: cancellationToken);

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

        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        await lifecycle.CreateLocalAsync<SlowActor>(id, cancellationToken: cancellationToken);

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
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        await lifecycle.CreateLocalAsync<BlockingActor>(id, cancellationToken: cancellationToken);

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
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("stop/1");
        await lifecycle.CreateLocalAsync<CounterActor>(id, cancellationToken: cancellationToken);

        await runtime.TellAsync<CounterActor>(
            id,
            static async (actor, ct) =>
            {
                await actor.IncrementAsync(ct);
            },
            cancellationToken);

        await runtime.StopAsync(id);

        var ex = await Assert.ThrowsAnyAsync<ActorCallException>(async () =>
            await runtime.AskAsync<CounterActor, int>(
                id,
                static (actor, _) => ValueTask.FromResult(actor.Value),
                cancellationToken));

        Assert.Equal(ActorCallStatus.ActorNotFound, ex.Status);
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
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("stop-timeout/1");
        await lifecycle.CreateLocalAsync<BlockingActor>(id, cancellationToken: cancellationToken);

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
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();

        Assert.False(runtime.TryGetMailboxMetrics(id, out _));
        await lifecycle.CreateLocalAsync<BlockingActor>(id, cancellationToken: cancellationToken);

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
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("timer/1");

        await lifecycle.CreateLocalAsync<TimerActor>(id, cancellationToken: cancellationToken);

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
    public void RegisterTimer_does_not_create_missing_actor()
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("timer-missing/1");

        var ex = Assert.Throws<ActorNotFoundException>(() =>
            runtime.RegisterTimer<TimerActor>(
                id,
                TimeSpan.FromMilliseconds(10),
                null,
                static (actor, _) =>
                {
                    actor.Ticks++;
                    return ValueTask.CompletedTask;
                }));

        Assert.Equal(ActorCallStatus.ActorNotFound, ex.Status);
        Assert.Empty(runtime.GetActiveActorIds(typeof(TimerActor)));
    }

    [Fact]
    public async Task StopAsync_disposes_registered_timer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("timer-stop/1");

        await lifecycle.CreateLocalAsync<TimerActor>(id, cancellationToken: cancellationToken);

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

        Assert.Empty(runtime.GetActiveActorIds(typeof(TimerActor)));
    }

    [Fact]
    public async Task StopAsync_prevents_queued_timer_registration_from_surviving_stop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("timer-stop-race/1");

        await lifecycle.CreateLocalAsync<TimerActor>(id, cancellationToken: cancellationToken);

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

        Assert.Empty(runtime.GetActiveActorIds(typeof(TimerActor)));
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
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("deactivate-timeout/1");
        await lifecycle.CreateLocalAsync<DeactivationActor>(id, cancellationToken: cancellationToken);

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
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var store = provider.GetRequiredService<IMessageLogStore>();
        var id = ActorId.From("record/1");
        await lifecycle.CreateLocalAsync<CounterActor>(id, cancellationToken: cancellationToken);

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
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var store = provider.GetRequiredService<IMessageLogStore>();
        var id = ActorId.From("record-error/1");
        await lifecycle.CreateLocalAsync<CounterActor>(id, cancellationToken: cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.AskAsync<CounterActor, int>(
                id,
                static (actor, _) => throw new InvalidOperationException("test error"),
                cancellationToken));

        var log = await store.GetLogAsync(id, cancellationToken);
        Assert.NotEmpty(log);
        var error = Assert.Single(log, entry => entry.Error is not null);
        Assert.Contains("InvalidOperationException", error.Error, StringComparison.Ordinal);
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

    private sealed class TestActor : GameActor
    {
        public List<string> Messages { get; } = [];

        public ValueTask<string> EchoAsync(string value, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult($"{Context.Id.Value}:{value}");
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
