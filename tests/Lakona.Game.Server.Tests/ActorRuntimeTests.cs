using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
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
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("room/alpha");

        await hosting.CreateAsync<TypedRoomActor>(id, cancellationToken);

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
        Assert.NotNull(provider.GetRequiredService<ActorHosting>());
    }

    [Fact]
    public void AddLakonaGameServerActors_registers_only_process_local_actor_services()
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();

        Assert.Null(provider.GetService<IClusterMembership>());
        Assert.Null(provider.GetService<ClusterRpcChannel>());
        Assert.Null(provider.GetService<IClusterNodeSender>());
        Assert.Null(provider.GetService<IExactClusterNodeSender>());
        Assert.Null(provider.GetService<IClusterActorTransport>());
        Assert.NotNull(provider.GetRequiredService<IActorRuntime>());
        Assert.IsType<InMemoryActorDirectory>(provider.GetRequiredService<IActorDirectory>());
        Assert.IsType<LocalActorPlacementService>(provider.GetRequiredService<IActorPlacementService>());
    }

    [Fact]
    public async Task Local_actor_placement_create_rejects_an_existing_actor()
    {
        await using var provider = CreateProvider();
        var placement = provider.GetRequiredService<IActorPlacementService>();
        var actorId = ActorId.From("local-existing-create");

        await placement.PlaceAsync<TestActor, ActorId>(
            actorId,
            ActorPlacementCreateMode.Create,
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<ActorPlacementException>(async () =>
            await placement.PlaceAsync<TestActor, ActorId>(
                actorId,
                ActorPlacementCreateMode.Create,
                TestContext.Current.CancellationToken));

        Assert.Equal(actorId, exception.ActorId);
        Assert.Contains("already has an activation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Local_actor_placement_ensure_returns_an_existing_actor()
    {
        await using var provider = CreateProvider();
        var placement = provider.GetRequiredService<IActorPlacementService>();
        var actorId = ActorId.From("local-existing-ensure");
        var created = await placement.PlaceAsync<TestActor, ActorId>(
            actorId,
            ActorPlacementCreateMode.Create,
            TestContext.Current.CancellationToken);

        var existing = await placement.PlaceAsync<TestActor, ActorId>(
            actorId,
            ActorPlacementCreateMode.Ensure,
            TestContext.Current.CancellationToken);

        Assert.Equal(created.ActorId, existing.ActorId);
        Assert.NotNull(existing.Activation);
        Assert.Equal(new NodeId("local"), existing.Owner);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ActorRuntime_rejects_non_positive_mailbox_capacity(int capacity)
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameServerActors(options => options.MailboxCapacity = capacity)
            .BuildServiceProvider();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => provider.GetRequiredService<IActorRuntime>());

        Assert.Contains("MailboxCapacity", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ActorRuntime_rejects_non_positive_slow_message_threshold(int milliseconds)
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameServerActors(
                options => options.SlowMessageThreshold = TimeSpan.FromMilliseconds(milliseconds))
            .BuildServiceProvider();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => provider.GetRequiredService<IActorRuntime>());

        Assert.Contains("SlowMessageThreshold", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ActorRuntime_rejects_non_positive_call_timeout(int milliseconds)
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameServerActors(
                options => options.CallTimeout = TimeSpan.FromMilliseconds(milliseconds))
            .BuildServiceProvider();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => provider.GetRequiredService<IActorRuntime>());

        Assert.Contains("CallTimeout", exception.Message, StringComparison.Ordinal);
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
    public async Task GetActiveActorIds_returns_active_actor_ids_for_requested_type()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();

        await hosting.CreateAsync<TestActor>(ActorId.From("a"), cancellationToken);
        await hosting.CreateAsync<TestActor>(ActorId.From("b"), cancellationToken);

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
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var firstSecretActorId = "secret-actor-id-a";
        var secondSecretActorId = "secret-actor-id-b";
        var firstId = ActorId.From(firstSecretActorId);
        var secondId = ActorId.From(secondSecretActorId);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await hosting.CreateAsync<BlockingActor>(firstId, cancellationToken);
        await hosting.CreateAsync<BlockingActor>(secondId, cancellationToken);

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
                return default;
            },
            cancellationToken);
        var firstRejected = runtime.TryTell<BlockingActor>(
            firstId,
            static (actor, _) =>
            {
                actor.Count++;
                return default;
            },
            cancellationToken);
        var secondAccepted = runtime.TryTell<BlockingActor>(
            secondId,
            static (actor, _) =>
            {
                actor.Count++;
                return default;
            },
            cancellationToken);
        var secondRejected = runtime.TryTell<BlockingActor>(
            secondId,
            static (actor, _) =>
            {
                actor.Count++;
                return default;
            },
            cancellationToken);
        var secondRejectedAgain = runtime.TryTell<BlockingActor>(
            secondId,
            static (actor, _) =>
            {
                actor.Count++;
                return default;
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
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("dynamic");

        await hosting.CreateAsync<TestActor>(id, cancellationToken);

        await runtime.TellAsync(
            typeof(TestActor),
            id,
            static (actor, _) =>
            {
                ((TestActor)actor).Messages.Add("dynamic");
                return default;
            },
            cancellationToken);

        var messages = await runtime.AskAsync<TestActor, string[]>(
            id,
            static async (actor, _) => await actor.GetMessagesAsync(),
            cancellationToken);
        Assert.Contains("dynamic", messages);
    }

    [Fact]
    public async Task Dynamic_AskAsync_dispatches_to_requested_actor_type_and_returns_object_result()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("dynamic-ask");

        await hosting.CreateAsync<TestActor>(id, cancellationToken);

        var result = await runtime.AskAsync(
            typeof(TestActor),
            id,
            static async (actor, ct) => await ((TestActor)actor).EchoAsync("asked", ct).ConfigureAwait(false),
            cancellationToken);

        Assert.Equal("dynamic-ask:asked", result);
    }

    [Fact]
    public async Task Dynamic_AskAsync_enters_actor_mailbox_and_preserves_ordering()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("dynamic-ask-mailbox");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await hosting.CreateAsync<BlockingActor>(id, cancellationToken);

        var blocking = runtime.TellAsync<BlockingActor>(
            id,
            (actor, ct) => actor.BlockAsync(entered, release.Task, ct),
            cancellationToken).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        var ask = runtime.AskAsync(
            typeof(BlockingActor),
            id,
            static (actor, _) =>
            {
                var blockingActor = (BlockingActor)actor;
                blockingActor.Count++;
                return new ValueTask<object?>(blockingActor.Count);
            },
            cancellationToken).AsTask();

        await Task.Delay(50, cancellationToken);
        Assert.False(ask.IsCompleted);

        release.SetResult();
        await blocking;

        var result = await ask;

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Actor_lifecycle_creates_local_actor_explicitly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("explicit");

        await hosting.CreateAsync<TestActor>(id, cancellationToken);

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
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("destroy-me");

        await hosting.CreateAsync<TestActor>(id, cancellationToken);
        await hosting.DestroyAsync<TestActor>(id, cancellationToken);

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
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("counter/1");
        await hosting.CreateAsync<CounterActor>(id, cancellationToken);

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
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("reentrant/1");
        await hosting.CreateAsync<ReentrantActor>(id, cancellationToken);

        var value = await runtime.AskAsync<ReentrantActor, int>(
            id,
            static (actor, ct) => actor.CallSelfAsync(ct),
            cancellationToken);

        Assert.Equal(2, value);
    }

    [Fact]
    public async Task Background_self_call_captured_from_completed_turn_waits_for_mailbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("reentrant/background");
        var startBackground = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? backgroundCall = null;
        await hosting.CreateAsync<EscapedSelfCallActor>(id, cancellationToken);

        try
        {
            await runtime.TellAsync<EscapedSelfCallActor>(
                    id,
                    (actor, _) =>
                    {
                        backgroundCall = actor.StartBackgroundSelfCall(startBackground.Task);
                        return default;
                    },
                    cancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

            var blocking = runtime.TellAsync<EscapedSelfCallActor>(
                id,
                (actor, ct) => actor.BlockAsync(blockingEntered, releaseBlocking.Task, ct),
                cancellationToken).AsTask();
            await blockingEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

            startBackground.SetResult();
            var pendingBackground = Assert.IsAssignableFrom<Task>(backgroundCall);
            await Assert.ThrowsAsync<TimeoutException>(
                () => pendingBackground.WaitAsync(TimeSpan.FromMilliseconds(100), cancellationToken));

            releaseBlocking.SetResult();
            await Task.WhenAll(blocking, pendingBackground)
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

            var value = await runtime.AskAsync<EscapedSelfCallActor, int>(
                id,
                static (actor, _) => new ValueTask<int>(actor.Value),
                cancellationToken);
            Assert.Equal(1, value);
        }
        finally
        {
            startBackground.TrySetResult();
            releaseBlocking.TrySetResult();
        }
    }

    [Fact]
    public async Task Same_actor_id_cannot_be_reused_for_different_actor_type()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var id = ActorId.From("shared/1");

        await hosting.CreateAsync<CounterActor>(id, cancellationToken);

        await Assert.ThrowsAsync<ActorHostingTypeMismatchException>(async () =>
            await hosting.EnsureAsync<ReentrantActor>(id, cancellationToken));
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

        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        await hosting.CreateAsync<SlowActor>(id, cancellationToken);

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
                options.CallTimeoutHandler = diagnostic => observed.TrySetResult(diagnostic);
            })
            .BuildServiceProvider();

        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        await hosting.CreateAsync<SlowActor>(id, cancellationToken);
        provider.GetRequiredService<ActorRuntimeOptions>().CallTimeout = TimeSpan.FromMilliseconds(20);

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
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        await hosting.CreateAsync<BlockingActor>(id, cancellationToken);

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
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("stop/1");
        await hosting.CreateAsync<CounterActor>(id, cancellationToken);

        await runtime.TellAsync<CounterActor>(
            id,
            static async (actor, ct) =>
            {
                await actor.IncrementAsync(ct);
            },
            cancellationToken);

        await hosting.DestroyAsync<CounterActor>(id, cancellationToken);

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
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("stop-timeout/1");
        await hosting.CreateAsync<BlockingActor>(id, cancellationToken);

        var blocking = runtime.TellAsync<BlockingActor>(
            id,
            (actor, ct) => actor.BlockAsync(entered, release.Task, ct),
            cancellationToken).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        try
        {
            await Assert.ThrowsAsync<ActorHostingStopException>(async () =>
                await hosting.DestroyAsync<BlockingActor>(id, cancellationToken));
        }
        finally
        {
            release.TrySetResult();
        }

        await blocking;

        await WaitForAsync(
            () => Task.FromResult(runtime.TryGetMailboxMetrics(id, out _)),
            static exists => !exists,
            cancellationToken);
        await hosting.CreateAsync<BlockingActor>(id, cancellationToken);

        Assert.Contains(id, runtime.GetActiveActorIds(typeof(BlockingActor)));
    }

    [Fact]
    public async Task Stop_rejects_new_calls_without_reactivation_and_reports_TryTell_dead_letter()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var gate = new DeactivationGate();
        var deadLetter = new TaskCompletionSource<ActorDeadLetterDiagnostic>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = new ServiceCollection()
            .AddSingleton(gate)
            .AddLakonaGameServerActors(options => options.DeadLetterHandler = diagnostic =>
            {
                if (diagnostic.Target == ActorId.From("stop/admission"))
                {
                    deadLetter.TrySetResult(diagnostic);
                }
            })
            .BuildServiceProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("stop/admission");
        await hosting.CreateAsync<BlockingDeactivationActor>(id, cancellationToken);

        var destroy = hosting.DestroyAsync<BlockingDeactivationActor>(id, cancellationToken).AsTask();
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            Assert.True(runtime.TryGetMailboxMetrics(id, out var before));

            var tellResult = runtime.TryTell<BlockingDeactivationActor>(
                id,
                static (actor, _) =>
                {
                    actor.Value++;
                    return default;
                },
                cancellationToken);

            Assert.Equal(ActorTellResult.ActorUnavailable, tellResult);
            Assert.True(runtime.TryGetMailboxMetrics(id, out var after));
            Assert.Equal(before.RejectedCount + 1, after.RejectedCount);
            var diagnostic = await deadLetter.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            Assert.Equal(id, diagnostic.Target);
            Assert.Equal("Actor is stopping.", diagnostic.Reason);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.TellAsync<BlockingDeactivationActor>(
                    id,
                    static (actor, _) =>
                    {
                        actor.Value++;
                        return default;
                    },
                    cancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.AskAsync<BlockingDeactivationActor, int>(
                    id,
                    static (actor, _) => new ValueTask<int>(actor.Value),
                    cancellationToken));
        }
        finally
        {
            gate.Release.TrySetResult();
        }

        await destroy;
        Assert.Equal(1, gate.ActivationCount);
    }

    [Fact]
    public async Task Runtime_rejects_operations_after_disposal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var runtime = provider.GetRequiredService<LakonaActorRuntime>();
        var hosting = provider.GetRequiredService<ActorHosting>();
        await runtime.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => runtime.GetState(ActorId.From("disposed/state")));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await hosting.CreateAsync<CounterActor>(ActorId.From("disposed/create"), cancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await runtime.TellAsync<CounterActor>(
                ActorId.From("disposed/tell"),
                static (_, _) => default,
                cancellationToken));
    }

    [Fact]
    public async Task Runtime_disposal_racing_actor_construction_does_not_leak_actor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var gate = new ConstructionGate();
        await using var provider = new ServiceCollection()
            .AddSingleton(gate)
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<LakonaActorRuntime>();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var id = ActorId.From("disposed/race");

        var create = Task.Run(
            async () => await hosting.CreateAsync<ConstructionBlockedActor>(id, cancellationToken),
            cancellationToken);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        try
        {
            await runtime.DisposeAsync();
        }
        finally
        {
            gate.Release.TrySetResult();
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(() => create);
        Assert.Throws<ObjectDisposedException>(() => runtime.TryGetMailboxMetrics(id, out _));
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
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();

        Assert.False(runtime.TryGetMailboxMetrics(id, out _));
        await hosting.CreateAsync<BlockingActor>(id, cancellationToken);

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
    public async Task StopAsync_runs_actor_deactivation_hook()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        DeactivationActor.Deactivations = 0;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var id = ActorId.From("deactivate/1");

        await hosting.CreateAsync<DeactivationActor>(id, cancellationToken);

        await hosting.DestroyAsync<DeactivationActor>(id, cancellationToken);

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
            var hosting = provider.GetRequiredService<ActorHosting>();
            await hosting.CreateAsync<DeactivationActor>(
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
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var id = ActorId.From("deactivate-timeout/1");
        await hosting.CreateAsync<DeactivationActor>(id, cancellationToken);

        var blocking = runtime.TellAsync<DeactivationActor>(
            id,
            (actor, ct) => actor.BlockAsync(entered, release.Task, ct),
            cancellationToken).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        await Assert.ThrowsAsync<ActorHostingStopException>(async () =>
            await hosting.DestroyAsync<DeactivationActor>(id, cancellationToken));

        release.SetResult();
        await blocking;

        Assert.Empty(runtime.GetActiveActorIds(typeof(DeactivationActor)));
        Assert.Equal(0, DeactivationActor.Deactivations);
    }

    [Fact]
    public async Task Cross_actor_circular_call_is_rejected_by_public_actor_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var firstId = ActorId.From("circle/a");
        var secondId = ActorId.From("circle/b");
        await hosting.CreateAsync<CircularActorA>(firstId, cancellationToken);
        await hosting.CreateAsync<CircularActorB>(secondId, cancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.AskAsync<CircularActorA, int>(
                firstId,
                (actor, ct) => actor.CallAsync(secondId, ct),
                cancellationToken));

        Assert.Contains(firstId.Value, exception.Message, StringComparison.Ordinal);
        Assert.Contains(secondId.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_waiting_for_mailbox_capacity_reports_queue_timeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource<ActorCallTimeoutDiagnostic>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var id = ActorId.From("queue-timeout/1");

        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors(options =>
            {
                options.MailboxCapacity = 1;
                options.CallTimeoutHandler = diagnostic => observed.TrySetResult(diagnostic);
            })
            .BuildServiceProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        await hosting.CreateAsync<BlockingActor>(id, cancellationToken);

        ActorTellResult blocking;
        do
        {
            blocking = runtime.TryTell<BlockingActor>(
                id,
                (actor, ct) => actor.BlockAsync(entered, release.Task, ct),
                cancellationToken);
            if (blocking == ActorTellResult.MailboxFull)
            {
                await Task.Delay(10, cancellationToken);
            }
        }
        while (blocking == ActorTellResult.MailboxFull);

        Assert.Equal(ActorTellResult.Accepted, blocking);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        provider.GetRequiredService<ActorRuntimeOptions>().CallTimeout = TimeSpan.FromMilliseconds(50);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await runtime.AskAsync<BlockingActor, int>(
                id,
                static (actor, _) => new ValueTask<int>(actor.Count),
                cancellationToken));
        var diagnostic = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        release.SetResult();
        await Task.Delay(20, cancellationToken);

        Assert.Equal(id, diagnostic.Target);
        Assert.Equal(ActorCallTimeoutReason.QueueTimeout, diagnostic.Reason);
        Assert.Equal(TimeSpan.FromMilliseconds(50), diagnostic.Timeout);
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

    private sealed class CircularActorA : GameActor
    {
        public ValueTask<int> CallAsync(ActorId target, CancellationToken cancellationToken)
        {
            return Context.Runtime.AskAsync<CircularActorB, int>(
                target,
                static (actor, ct) => actor.CallBackAsync(ActorId.From("circle/a"), ct),
                cancellationToken);
        }
    }

    private sealed class CircularActorB : GameActor
    {
        public ValueTask<int> CallBackAsync(ActorId target, CancellationToken cancellationToken)
        {
            return Context.Runtime.AskAsync<CircularActorA, int>(
                target,
                static (_, _) => new ValueTask<int>(1),
                cancellationToken);
        }
    }

    private sealed class TestActor : GameActor
    {
        public List<string> Messages { get; } = [];

        public ValueTask<string> EchoAsync(string value, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult($"{Context.Id.Value}:{value}");
        }

        public async ValueTask<string[]> GetMessagesAsync()
        {
            await Task.Yield();
            return Messages.ToArray();
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

    private sealed class EscapedSelfCallActor : GameActor
    {
        public int Value { get; private set; }

        public Task StartBackgroundSelfCall(Task start)
        {
            return Task.Run(async () =>
            {
                await start;
                await Context.Runtime.TellAsync<EscapedSelfCallActor>(
                    Context.Id,
                    static (actor, _) =>
                    {
                        actor.Value++;
                        return default;
                    });
            });
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

    private sealed class BlockingDeactivationActor(DeactivationGate gate) : GameActor
    {
        public int Value { get; set; }

        protected override ValueTask OnActivateAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref gate.ActivationCount);
            return default;
        }

        protected override async ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
        {
            gate.Entered.TrySetResult();
            await gate.Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class DeactivationGate
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ActivationCount;
    }

    private sealed class ConstructionBlockedActor : GameActor
    {
        public ConstructionBlockedActor(ConstructionGate gate)
        {
            gate.Entered.TrySetResult();
            gate.Release.Task.GetAwaiter().GetResult();
        }
    }

    private sealed class ConstructionGate
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
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
