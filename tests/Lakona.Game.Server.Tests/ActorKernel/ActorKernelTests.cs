using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;
using Lakona.Game.Server.Internal.ActorKernel;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;
using Lakona.Game.Server.Internal.ActorKernel.Mailbox;

namespace Lakona.Game.Server.Tests.ActorKernel;

public sealed class ActorSystemTests
{
    private static readonly ActorCallOptions DefaultCallOptions = CallOptions(TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Send_dispatches_message()
    {
        await using ActorSystem system = new();
        ProbeActor actor = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(actor)).Ref;

        await actorRef.Send("hello");

        Assert.Equal("hello", await actor.NextMessage());
    }

    [Fact]
    public async Task Call_returns_actor_response()
    {
        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new EchoActor())).Ref;

        string response = await actorRef.Call<string>("ping", DefaultCallOptions);

        Assert.Equal("ping", response);
    }

    [Fact]
    public void Call_requires_distinct_queue_and_response_timeouts()
    {
        System.Reflection.MethodInfo call = Assert.Single(
            typeof(ActorRef<object>).GetMethods().Where(static method => method.Name == nameof(ActorRef<object>.Call)));

        Type[] parameterTypes = call.GetParameters().Select(static parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(ActorCallOptions), parameterTypes);
        Assert.DoesNotContain(typeof(TimeSpan), parameterTypes);
    }

    [Fact]
    public async Task Call_times_out_when_actor_does_not_respond()
    {
        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new IgnoringActor())).Ref;

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await actorRef.Call<string>("ping", CallOptions(TimeSpan.FromMilliseconds(20))));
    }

    [Fact]
    public async Task Call_timeout_publishes_root_cause_diagnostic_for_unanswered_call()
    {
        await using ActorSystem system = new();
        TaskCompletionSource<ActorCallTimeout> timedOut = new(TaskCreationOptions.RunContinuationsAsynchronously);
        system.CallTimedOut += timeout => timedOut.TrySetResult(timeout);
        ActorRef<object> actorRef = (await system.SpawnAsync(new IgnoringActor())).Ref;
        ActorCallOptions options = new(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20));

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await actorRef.Call<string>("ping", options));

        ActorCallTimeout timeout = await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Null(timeout.Caller);
        Assert.Equal(actorRef.Id, timeout.Target);
        Assert.Equal(typeof(string).FullName, timeout.RequestType);
        Assert.Equal(options.QueueTimeout, timeout.QueueTimeout);
        Assert.Equal(options.ResponseTimeout, timeout.ResponseTimeout);
        Assert.True(timeout.Elapsed > TimeSpan.Zero);
        Assert.Equal(ActorCallTimeoutReason.ResponseTimeout, timeout.Reason);
        Assert.Empty(timeout.CallChain);
        Assert.Contains($"Target={actorRef.Id.Value}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_timeout_diagnostic_identifies_queue_timeout_before_request_is_accepted()
    {
        await using ActorSystem system = new(new ActorSystemOptions { MailboxCapacity = 1 });
        TaskCompletionSource<ActorCallTimeout> timedOut = new(TaskCreationOptions.RunContinuationsAsynchronously);
        system.CallTimedOut += timeout => timedOut.TrySetResult(timeout);
        BlockingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;
        ActorCallOptions options = new(TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(5));

        try
        {
            await actorRef.Send("first");

            TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
                await actorRef.Call<string>("queued", options));

            ActorCallTimeout timeout = await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Null(timeout.Caller);
            Assert.Equal(actorRef.Id, timeout.Target);
            Assert.Equal(typeof(string).FullName, timeout.RequestType);
            Assert.Equal(options.QueueTimeout, timeout.QueueTimeout);
            Assert.Equal(options.ResponseTimeout, timeout.ResponseTimeout);
            Assert.True(timeout.Elapsed > TimeSpan.Zero);
            Assert.Equal(ActorCallTimeoutReason.QueueTimeout, timeout.Reason);
            Assert.Empty(timeout.CallChain);
            Assert.Contains("before it could be queued", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            actor.Release();
        }

        await Eventually(() => actorHandle.GetMailboxMetrics().ProcessedCount == 1);
    }

    [Fact]
    public async Task Call_allows_zero_queue_timeout_when_mailbox_has_capacity()
    {
        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new EchoActor())).Ref;
        ActorCallOptions options = new(TimeSpan.Zero, TimeSpan.FromSeconds(1));

        string response = await actorRef.Call<string>("ping", options);

        Assert.Equal("ping", response);
    }

    [Fact]
    public async Task Call_honors_cancellation_before_zero_queue_timeout_enqueue()
    {
        await using ActorSystem system = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(new ProbeActor());
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await actorHandle.Ref.Call<string>(
                "ping",
                new ActorCallOptions(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                cancellation.Token));

        await Task.Delay(50);

        Assert.Equal(0, actorHandle.GetMailboxMetrics().ProcessedCount);
    }


    [Fact]
    public async Task Call_immediately_throws_on_circular_call_chain()
    {
        await using ActorSystem system = new();
        List<ActorCallTimeout> timeouts = new();
        system.CallTimedOut += timeouts.Add;
        ActorRef<object> actorRef = (await system.SpawnAsync(new SelfCallingActor())).Ref;

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actorRef.Call<string>(new StartSelfCall(), DefaultCallOptions));

        Assert.Contains("Circular actor call detected", exception.Message, StringComparison.Ordinal);
        Assert.Contains(actorRef.Id.Value.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Empty(timeouts);
    }

    [Fact]
    public async Task Actor_call_context_does_not_flow_to_background_work()
    {
        await using ActorSystem system = new();
        BackgroundSelfCallingActor actor = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(actor)).Ref;

        string started = await actorRef.Call<string>(new StartBackgroundSelfCall(), DefaultCallOptions);

        Assert.Equal("started", started);
        actor.ReleaseBackgroundWork();
        Assert.Equal("background", await actor.BackgroundResult.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Call_timeout_diagnostic_preserves_downstream_call_chain()
    {
        await using ActorSystem system = new();
        TaskCompletionSource<ActorCallTimeout> timedOut = new(TaskCreationOptions.RunContinuationsAsynchronously);
        system.CallTimedOut += timeout => timedOut.TrySetResult(timeout);
        ActorRef<object> downstream = (await system.SpawnAsync(new IgnoringActor())).Ref;
        ActorRef<object> upstream = (await system.SpawnAsync(new DownstreamCallingActor(downstream))).Ref;

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await upstream.Call<string>(new StartDownstreamCall(), DefaultCallOptions));

        ActorCallTimeout timeout = await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(upstream.Id, timeout.Caller);
        Assert.Equal(downstream.Id, timeout.Target);
        Assert.Equal(typeof(DownstreamRequest).FullName, timeout.RequestType);
        Assert.Equal(ActorCallTimeoutReason.ResponseTimeout, timeout.Reason);
        Assert.Equal(new[] { upstream.Id }, timeout.CallChain);
    }

    [Fact]
    public async Task Mailbox_processes_messages_in_send_order()
    {
        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new OrderingActor())).Ref;

        for (int i = 0; i < 64; i++)
        {
            await actorRef.Send(i);
        }

        int[] values = await actorRef.Call<int[]>(new GetValues(), DefaultCallOptions);

        Assert.Equal(Enumerable.Range(0, 64), values);
    }

    [Fact]
    public async Task Mailbox_never_executes_same_actor_concurrently()
    {
        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new ConcurrencyProbeActor())).Ref;

        Task[] sends = Enumerable.Range(0, 32)
            .Select(i => actorRef.Send(i).AsTask())
            .ToArray();

        await Task.WhenAll(sends);

        int maxConcurrency = await actorRef.Call<int>(new GetMaxConcurrency(), DefaultCallOptions);

        Assert.Equal(1, maxConcurrency);
    }

    [Fact]
    public async Task Timer_messages_are_dispatched_through_mailbox()
    {
        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new TimerActor())).Ref;

        await actorRef.Send(new StartTimer());
        await Task.Delay(80);

        int ticks = await actorRef.Call<int>(new GetTickCount(), DefaultCallOptions);

        Assert.True(ticks > 0);
    }

    [Fact]
    public async Task Timer_drops_ticks_when_mailbox_is_full()
    {
        await using ActorSystem system = new(new ActorSystemOptions { MailboxCapacity = 1 });
        BlockingTimerActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;
        int fullTickDeadLetters = 0;

        system.DeadLetterPublished += deadLetter =>
        {
            if (deadLetter.MessageType == typeof(Tick).FullName &&
                deadLetter.Reason == "Actor mailbox is full.")
            {
                Interlocked.Increment(ref fullTickDeadLetters);
            }
        };

        try
        {
            await actorRef.Send(new StartTimer());
            await actor.TickStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

            await Eventually(() => Volatile.Read(ref fullTickDeadLetters) > 0);
        }
        finally
        {
            actor.Release();
            await actorHandle.Stop(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task Bounded_mailbox_applies_backpressure()
    {
        await using ActorSystem system = new(new ActorSystemOptions { MailboxCapacity = 1 });
        BlockingActor actor = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(actor)).Ref;

        await actorRef.Send("first");

        Task secondSend = actorRef.Send("second").AsTask();
        Task completed = await Task.WhenAny(secondSend, Task.Delay(50));

        Assert.NotSame(secondSend, completed);

        actor.Release();
        await secondSend.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Spawn_options_can_override_mailbox_capacity()
    {
        await using ActorSystem system = new(new ActorSystemOptions { MailboxCapacity = 1 });
        BlockingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(
            actor,
            new ActorSpawnOptions { MailboxCapacity = 2 });
        ActorRef<object> actorRef = actorHandle.Ref;

        try
        {
            await actorRef.Send("first").AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            await actorRef.Send("second").AsTask().WaitAsync(TimeSpan.FromSeconds(1));

            Task thirdSend = actorRef.Send("third").AsTask();
            Task completed = await Task.WhenAny(thirdSend, Task.Delay(50));

            Assert.NotSame(thirdSend, completed);
            Assert.Equal(2, actorHandle.GetMailboxMetrics().Capacity);
        }
        finally
        {
            actor.Release();
        }

        await Eventually(() => actorHandle.GetMailboxMetrics().ProcessedCount == 3);
    }

    [Fact]
    public async Task Mailbox_metrics_report_capacity_queue_and_counts()
    {
        await using ActorSystem system = new(new ActorSystemOptions { MailboxCapacity = 2 });
        DisposeGaugeBlockingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        try
        {
            await actorRef.Send("first").AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            await actor.MessageStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await actorRef.Send("second").AsTask().WaitAsync(TimeSpan.FromSeconds(1));

            MailboxMetrics queuedMetrics = actorHandle.GetMailboxMetrics();

            Assert.Equal(2, queuedMetrics.Capacity);
            Assert.Equal(1, queuedMetrics.QueuedCount);
            Assert.Equal(2, queuedMetrics.EnqueuedCount);
            Assert.Equal(0, queuedMetrics.ProcessedCount);
            Assert.Equal(0, queuedMetrics.RejectedCount);
            Assert.False(queuedMetrics.IsCompleted);
        }
        finally
        {
            actor.Release();
        }

        await Eventually(() => actorHandle.GetMailboxMetrics().ProcessedCount == 2);
    }

    [Fact]
    public async Task TrySend_returns_mailbox_full_and_publishes_dead_letter_when_capacity_is_exhausted()
    {
        await using ActorSystem system = new(new ActorSystemOptions { MailboxCapacity = 1 });
        List<DeadLetter> deadLetters = new();
        system.DeadLetterPublished += deadLetters.Add;
        BlockingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("first"));
        Assert.Equal(ActorSendResult.MailboxFull, actorRef.TrySend("second"));

        MailboxMetrics metrics = actorHandle.GetMailboxMetrics();
        DeadLetter deadLetter = Assert.Single(deadLetters);

        Assert.Equal(1, metrics.Capacity);
        Assert.Equal(1, metrics.EnqueuedCount);
        Assert.Equal(1, metrics.RejectedCount);
        Assert.Equal(actorRef.Id, deadLetter.Target);
        Assert.Equal(typeof(string).FullName, deadLetter.MessageType);
        Assert.Equal("Actor mailbox is full.", deadLetter.Reason);

        actor.Release();
        await Eventually(() => actorHandle.GetMailboxMetrics().ProcessedCount == 1);
    }

    [Fact]
    public async Task TrySend_to_stopped_actor_returns_unavailable_and_publishes_dead_letter()
    {
        await using ActorSystem system = new();
        List<DeadLetter> deadLetters = new();
        system.DeadLetterPublished += deadLetters.Add;
        ActorHandle<object> actorHandle = await system.SpawnAsync(new IgnoringActor());
        ActorRef<object> actorRef = actorHandle.Ref;

        await actorHandle.Stop();

        ActorSendResult result = actorRef.TrySend("late-message");

        DeadLetter deadLetter = Assert.Single(deadLetters);
        Assert.Equal(ActorSendResult.ActorUnavailable, result);
        Assert.Equal(actorRef.Id, deadLetter.Target);
        Assert.Equal(typeof(string).FullName, deadLetter.MessageType);
        Assert.Equal("Actor does not exist.", deadLetter.Reason);
    }

    [Fact]
    public async Task Diagnostic_handler_errors_publish_observer_error()
    {
        await using ActorSystem system = new();
        TaskCompletionSource<ActorObserverError> observerError = new(TaskCreationOptions.RunContinuationsAsynchronously);
        system.DeadLetterPublished += _ => throw new InvalidOperationException("dead letter handler failed");
        system.ObserverErrorPublished += error => observerError.TrySetResult(error);
        ActorHandle<object> actorHandle = await system.SpawnAsync(new IgnoringActor());
        ActorRef<object> actorRef = actorHandle.Ref;

        await actorHandle.Stop();
        ActorSendResult result = actorRef.TrySend("late-message");
        ActorObserverError error = await observerError.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ActorSendResult.ActorUnavailable, result);
        Assert.Equal(ActorObserverErrorSource.DeadLetterHandler, error.Source);
        Assert.Equal(actorRef.Id, error.ActorId);
        Assert.Equal(typeof(string).FullName, error.MessageType);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task Slow_message_detection_publishes_event_when_threshold_is_exceeded()
    {
        await using ActorSystem system = new(new ActorSystemOptions
        {
            SlowMessageThreshold = TimeSpan.FromMilliseconds(10)
        });

        TaskCompletionSource<SlowMessage> detected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        system.SlowMessageDetected += message => detected.TrySetResult(message);
        ActorRef<object> actorRef = (await system.SpawnAsync(new SlowActor(TimeSpan.FromMilliseconds(30)))).Ref;

        await actorRef.Send("slow");

        SlowMessage slowMessage = await detected.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(actorRef.Id, slowMessage.ActorId);
        Assert.Equal(typeof(string).FullName, slowMessage.MessageType);
        Assert.True(slowMessage.Elapsed >= TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public void Diagnostic_events_do_not_expose_message_payloads()
    {
        Assert.Null(typeof(DeadLetter).GetProperty("Message"));
        Assert.NotNull(typeof(DeadLetter).GetProperty("MessageType"));
        Assert.Null(typeof(SlowMessage).GetProperty("Message"));
        Assert.NotNull(typeof(SlowMessage).GetProperty("MessageType"));
        Assert.Null(typeof(ActorCallTimeout).GetProperty("Request"));
        Assert.NotNull(typeof(ActorCallTimeout).GetProperty("RequestType"));
        Assert.Null(typeof(ActorCallTimeout).GetProperty("Timeout"));
        Assert.NotNull(typeof(ActorCallTimeout).GetProperty("QueueTimeout"));
        Assert.NotNull(typeof(ActorCallTimeout).GetProperty("ResponseTimeout"));
        Assert.NotNull(typeof(ActorCallTimeout).GetProperty("Elapsed"));
    }

    [Fact]
    public void Actor_context_does_not_expose_actor_system()
    {
        Assert.Null(typeof(ActorContext<object>).GetProperty("System"));
    }

    [Fact]
    public async Task Slow_message_detection_adds_trace_event_to_dispatch_activity()
    {
        TaskCompletionSource<Activity> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == LakonaActorDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "Lakona.Actor.Actor.Dispatch" &&
                    (Equals(activity.GetTagItem("lakona-actor.slow_message"), true) ||
                     activity.Events.Any(evt => evt.Name == "Lakona.Actor.Actor.SlowMessage")))
                {
                    stopped.TrySetResult(activity);
                }
            }
        };

        ActivitySource.AddActivityListener(listener);

        await using ActorSystem system = new(new ActorSystemOptions
        {
            SlowMessageThreshold = TimeSpan.FromMilliseconds(10)
        });
        ActorRef<object> actorRef = (await system.SpawnAsync(new SlowActor(TimeSpan.FromMilliseconds(30)))).Ref;

        await actorRef.Send("slow");

        Activity activity = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(true, activity.GetTagItem("lakona-actor.slow_message"));
        Assert.Contains(activity.Events, evt => evt.Name == "Lakona.Actor.Actor.SlowMessage");
    }

    [Fact]
    public async Task Typed_actor_ref_sends_and_calls_typed_messages()
    {
        await using ActorSystem system = new();
        ActorRef<CounterMessage> counter = (await system.SpawnAsync<CounterMessage>(new CounterActor())).Ref;

        await counter.Send(new Add(2));
        int value = await counter.Call<int>(new GetCounter(), DefaultCallOptions);

        Assert.Equal(2, value);
    }

    [Fact]
    public async Task Typed_actor_handle_exposes_runtime_operations()
    {
        await using ActorSystem system = new();
        ActorHandle<CounterMessage> counter = await system.SpawnAsync<CounterMessage>(
            new CounterActor(),
            new ActorSpawnOptions { MailboxCapacity = 4 });

        Assert.Equal(4, counter.GetMailboxMetrics().Capacity);

        await counter.Stop();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await counter.Ref.Send(new Add(1)));
    }

    [Fact]
    public void Actor_handle_does_not_convert_to_actor_ref()
    {
        Assert.DoesNotContain(
            typeof(ActorHandle<object>).GetMethods(),
            static method => method.IsSpecialName && method.Name is "op_Implicit" or "op_Explicit");
    }

    [Fact]
    public async Task Actor_start_hook_runs_when_actor_is_spawned()
    {
        await using ActorSystem system = new();
        LifecycleActor actor = new();

        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        string[] events = await actorRef.Call<string[]>(new GetLifecycleEvents(), DefaultCallOptions);

        Assert.Equal(["started", "started-message", "get"], events);
        await actorHandle.Stop();
    }

    [Fact]
    public async Task Actor_stop_hook_runs_before_mailbox_is_completed()
    {
        await using ActorSystem system = new();
        LifecycleActor actor = new();

        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);

        ActorStopResult result = await actorHandle.Stop(TimeSpan.FromSeconds(1));

        Assert.Equal(ActorStopResult.Drained, result);

        string[] events = actor.Events.ToArray();
        int stoppingIndex = Array.IndexOf(events, "stopping");

        Assert.Equal(3, events.Length);
        Assert.Equal("started", events[0]);
        Assert.Contains("started-message", events);
        Assert.True(stoppingIndex >= 0);
    }

    [Fact]
    public async Task Actor_stop_hook_runs_after_current_message_without_concurrency()
    {
        await using ActorSystem system = new();
        SerializedStoppingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        await actorRef.Send("block");
        await actor.MessageStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Task<ActorStopResult> stopTask = actorHandle.Stop(TimeSpan.FromSeconds(1)).AsTask();
        try
        {
            await Task.Delay(50);

            Assert.False(actor.StoppingStarted.Task.IsCompleted);
        }
        finally
        {
            actor.Release();
        }

        ActorStopResult result = await stopTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ActorStopResult.Drained, result);
        Assert.Equal(1, actor.MaxConcurrency);
        Assert.Equal(new[] { "message-start", "message-end", "stopping" }, actor.Events);
    }

    [Fact]
    public async Task Actor_stop_hook_failure_completes_and_removes_actor()
    {
        await using ActorSystem system = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync<object>("bad-stop", new FailingStopActor());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actorHandle.Stop(TimeSpan.FromSeconds(1)));

        Assert.Equal(ActorState.Dead, system.GetActorState(actorHandle.Id));
        Assert.False(system.TryGetActor<object>("bad-stop", out _));
    }

    [Fact]
    public async Task Actor_start_hook_failure_rolls_back_registration()
    {
        await using ActorSystem system = new();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await system.SpawnAsync<object>("bad", new FailingStartActor()));

        Assert.False(system.TryGetActor<object>("bad", out _));
    }

    [Fact]
    public void Public_api_does_not_expose_scheduler_lane_concepts()
    {
        string[] publicApiNames = typeof(ActorSystem).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMembers().Select(member => $"{type.Name}.{member.Name}").Append(type.Name))
            .ToArray();

        Assert.DoesNotContain(publicApiNames, name => name.Contains("Scheduler", StringComparison.Ordinal));
        Assert.DoesNotContain(publicApiNames, name => name.Contains("Lane", StringComparison.Ordinal));
        Assert.DoesNotContain(publicApiNames, name => name.Contains("LogicThread", StringComparison.Ordinal));
    }

    [Fact]
    public void Actor_system_lifecycle_api_is_async_only()
    {
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(ActorSystem)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(ActorSystem)));
        Assert.Empty(typeof(ActorSystem).GetMethods().Where(static method => method.Name == "Spawn"));

        System.Reflection.MethodInfo spawnAsync = Assert.Single(
            typeof(ActorSystem).GetMethods()
                .Where(static method =>
                    method.Name == "SpawnAsync" &&
                    method.IsPublic &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType.Name == "IActor`1"));

        Type returnType = spawnAsync.ReturnType;
        Assert.Equal("ValueTask`1", returnType.Name);
        Assert.Equal("ActorHandle`1", returnType.GenericTypeArguments[0].Name);
    }

    [Fact]
    public void Actor_ref_public_api_exposes_only_messaging_operations()
    {
        string[] actorRefMembers = typeof(ActorRef<object>)
            .GetMembers()
            .Select(static member => member.Name)
            .ToArray();

        Assert.Contains("Send", actorRefMembers);
        Assert.Contains("TrySend", actorRefMembers);
        Assert.Contains("Call", actorRefMembers);
        Assert.DoesNotContain("Stop", actorRefMembers);
        Assert.DoesNotContain("GetMailboxMetrics", actorRefMembers);
        Assert.DoesNotContain("GetState", actorRefMembers);
    }

    [Fact]
    public void Actor_system_spawn_async_returns_actor_handle()
    {
        Type[] spawnReturnGenericDefinitions = typeof(ActorSystem)
            .GetMethods()
            .Where(static method => method.Name == "SpawnAsync" && method.IsPublic)
            .Select(static method => method.ReturnType)
            .Where(static returnType => returnType.IsGenericType)
            .Select(static returnType => returnType.GenericTypeArguments[0].GetGenericTypeDefinition())
            .Distinct()
            .ToArray();

        Type returnType = Assert.Single(spawnReturnGenericDefinitions);
        Assert.Equal("ActorHandle`1", returnType.Name);
    }

    [Fact]
    public async Task Dispatch_emits_activity_for_tracing()
    {
        TaskCompletionSource<Activity> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == LakonaActorDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "Lakona.Actor.Actor.Dispatch")
                {
                    stopped.TrySetResult(activity);
                }
            }
        };

        ActivitySource.AddActivityListener(listener);

        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new EchoActor())).Ref;

        string response = await actorRef.Call<string>("trace-me", DefaultCallOptions);
        Activity activity = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("trace-me", response);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal(actorRef.Id.Value, activity.GetTagItem("lakona-actor.actor.id"));
        Assert.Equal(typeof(string).FullName, activity.GetTagItem("lakona-actor.message.type"));
        Assert.Equal("call", activity.GetTagItem("lakona-actor.message.kind"));
    }

    [Fact]
    public async Task Send_and_call_dispatch_activities_preserve_parent_activity_context()
    {
        using ActivitySource testSource = new("Lakona.Actor.Tests");
        ConcurrentQueue<Activity> stopped = new();

        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name is LakonaActorDiagnostics.ActivitySourceName or "Lakona.Actor.Tests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "Lakona.Actor.Actor.Dispatch")
                {
                    stopped.Enqueue(activity);
                }
            }
        };

        ActivitySource.AddActivityListener(listener);

        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new EchoActor())).Ref;

        using Activity? parent = testSource.StartActivity("parent");
        Assert.NotNull(parent);

        await actorRef.Send("trace-send");
        string response = await actorRef.Call<string>("trace-call", DefaultCallOptions);
        await Eventually(() => stopped.Count >= 2);

        Assert.Equal("trace-call", response);
        Assert.All(stopped.ToArray(), activity => Assert.Equal(parent!.SpanId, activity.ParentSpanId));
    }

    [Fact]
    public async Task Timer_dispatch_activity_preserves_scheduling_activity_context()
    {
        using ActivitySource testSource = new("Lakona.Actor.Tests.Timer");
        TaskCompletionSource<Activity> startTimerActivity = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Activity> tickActivity = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name is LakonaActorDiagnostics.ActivitySourceName or "Lakona.Actor.Tests.Timer",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName != "Lakona.Actor.Actor.Dispatch")
                {
                    return;
                }

                string? messageType = activity.GetTagItem("lakona-actor.message.type")?.ToString();

                if (messageType?.Contains(nameof(StartTimer), StringComparison.Ordinal) == true)
                {
                    startTimerActivity.TrySetResult(activity);
                }

                if (messageType?.Contains(nameof(Tick), StringComparison.Ordinal) == true)
                {
                    tickActivity.TrySetResult(activity);
                }
            }
        };

        ActivitySource.AddActivityListener(listener);

        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new TimerActor())).Ref;

        using Activity? parent = testSource.StartActivity("timer-parent");
        Assert.NotNull(parent);

        await actorRef.Send(new StartTimer());

        Activity scheduledBy = await startTimerActivity.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Activity activity = await tickActivity.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(parent!.SpanId, scheduledBy.ParentSpanId);
        Assert.Equal(scheduledBy.SpanId, activity.ParentSpanId);
    }

    [Fact]
    public async Task Runtime_metrics_emit_low_cardinality_counters_and_queue_gauge()
    {
        ConcurrentQueue<MetricMeasurement> measurements = new();

        using MeterListener listener = new()
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == LakonaActorDiagnostics.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            measurements.Enqueue(new MetricMeasurement(
                instrument.Name,
                measurement,
                tags.ToArray()));
        });
        listener.Start();

        await using ActorSystem system = new(new ActorSystemOptions { MailboxCapacity = 2 });
        BlockingActor blocking = new();
        ActorHandle<object> blockedHandle = await system.SpawnAsync(blocking);
        ActorRef<object> blocked = blockedHandle.Ref;
        ActorRef<object> echo = (await system.SpawnAsync(new EchoActor())).Ref;
        ActorRef<object> ignoring = (await system.SpawnAsync(new IgnoringActor())).Ref;
        ActorHandle<object> stoppedHandle = await system.SpawnAsync(new IgnoringActor());
        ActorRef<object> stopped = stoppedHandle.Ref;

        try
        {
            await echo.Send("send");
            _ = await echo.Call<string>("call", DefaultCallOptions);
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await ignoring.Call<string>("timeout", CallOptions(TimeSpan.FromMilliseconds(20))));
            await blocked.Send("active");
            await blocked.Send("queued");
            await stoppedHandle.Stop();
            Assert.Equal(ActorSendResult.ActorUnavailable, stopped.TrySend("late"));
            listener.RecordObservableInstruments();
        }
        finally
        {
            blocking.Release();
        }

        await Eventually(() => blockedHandle.GetMailboxMetrics().ProcessedCount >= 1);

        string[] expectedInstruments =
        [
            "lakona-actor.message.accepted",
            "lakona-actor.message.rejected",
            "lakona-actor.message.processed",
            "lakona-actor.call.started",
            "lakona-actor.call.timeout",
            "lakona-actor.deadletter.published",
            "lakona-actor.mailbox.queue.length"
        ];
        MetricMeasurement[] measurementSnapshot = measurements.ToArray();

        foreach (string expectedInstrument in expectedInstruments)
        {
            Assert.Contains(measurementSnapshot, measurement => measurement.InstrumentName == expectedInstrument);
        }

        Assert.Contains(measurementSnapshot, measurement =>
            measurement.InstrumentName == "lakona-actor.mailbox.queue.length" && measurement.Value > 0);

        string[] allowedTagKeys = ["kind", "reason"];
        foreach (MetricMeasurement measurement in measurementSnapshot)
        {
            Assert.All(measurement.Tags, tag => Assert.Contains(tag.Key, allowedTagKeys));
        }
    }

    [Fact]
    public async Task Mailbox_queue_gauge_counts_disposing_mailboxes_until_drained()
    {
        ConcurrentQueue<MetricMeasurement> measurements = new();

        using MeterListener listener = new()
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == LakonaActorDiagnostics.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            measurements.Enqueue(new MetricMeasurement(
                instrument.Name,
                measurement,
                tags.ToArray()));
        });
        listener.Start();

        ActorSystem system = new(new ActorSystemOptions { MailboxCapacity = 2 });
        DisposeGaugeBlockingActor actor = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(actor)).Ref;

        await actorRef.Send("active");
        await actor.MessageStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await actorRef.Send("queued");

        Task disposeTask = system.DisposeAsync().AsTask();

        try
        {
            listener.RecordObservableInstruments();
            MetricMeasurement[] measurementSnapshot = measurements.ToArray();

            Assert.Contains(measurementSnapshot, measurement =>
                measurement.InstrumentName == "lakona-actor.mailbox.queue.length" &&
                measurement.Value > 0);
        }
        finally
        {
            actor.Release();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task Named_actor_can_be_resolved_and_used()
    {
        await using ActorSystem system = new();
        ActorRef<object> spawned = (await system.SpawnAsync("echo", new EchoActor())).Ref;

        ActorRef<object> resolved = system.GetActor<object>("echo");
        string response = await resolved.Call<string>("named", DefaultCallOptions);

        Assert.Equal(spawned.Id, resolved.Id);
        Assert.Equal("named", response);
    }

    [Fact]
    public async Task Duplicate_actor_name_is_rejected()
    {
        await using ActorSystem system = new();

        await system.SpawnAsync("echo", new EchoActor());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await system.SpawnAsync("echo", new EchoActor()));
    }

    [Fact]
    public async Task Stop_by_name_removes_named_actor_from_registry()
    {
        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync("echo", new EchoActor())).Ref;

        await system.Stop("echo");

        Assert.False(system.TryGetActor<object>("echo", out _));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actorRef.Send("late"));
    }

    [Fact]
    public async Task Typed_named_actor_can_be_resolved_and_used()
    {
        await using ActorSystem system = new();
        ActorRef<CounterMessage> spawned = (await system.SpawnAsync<CounterMessage>("counter", new CounterActor())).Ref;

        ActorRef<CounterMessage> resolved = system.GetActor<CounterMessage>("counter");
        await resolved.Send(new Add(3));
        int value = await spawned.Call<int>(new GetCounter(), DefaultCallOptions);

        Assert.Equal(spawned.Id, resolved.Id);
        Assert.Equal(3, value);
    }

    [Fact]
    public async Task Named_actor_resolution_rejects_wrong_message_type()
    {
        await using ActorSystem system = new();
        await system.SpawnAsync<CounterMessage>("counter", new CounterActor());

        Assert.Throws<InvalidOperationException>(() => system.GetActor<object>("counter"));
    }

    [Fact]
    public async Task Stop_drains_queued_messages_before_completion()
    {
        await using ActorSystem system = new();
        RecordingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        for (int i = 0; i < 16; i++)
        {
            await actorRef.Send(i);
        }

        await actorHandle.Stop();

        Assert.Equal(Enumerable.Range(0, 16), actor.Values);
    }

    [Fact]
    public async Task Stop_with_timeout_drains_queued_messages_before_completion()
    {
        await using ActorSystem system = new();
        RecordingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        for (int i = 0; i < 16; i++)
        {
            await actorRef.Send(i);
        }

        ActorStopResult result = await actorHandle.Stop(TimeSpan.FromSeconds(1));

        Assert.Equal(ActorStopResult.Drained, result);
        Assert.Equal(Enumerable.Range(0, 16), actor.Values);
    }

    [Fact]
    public async Task Stop_with_timeout_returns_timed_out_and_rejects_new_messages()
    {
        await using ActorSystem system = new();
        List<DeadLetter> deadLetters = new();
        system.DeadLetterPublished += deadLetters.Add;
        BlockingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        await actorRef.Send("blocked");

        ActorStopResult result = await actorHandle.Stop(TimeSpan.FromMilliseconds(20));

        Assert.Equal(ActorStopResult.TimedOut, result);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actorRef.Send("late"));

        DeadLetter deadLetter = Assert.Single(deadLetters);
        Assert.Equal(actorRef.Id, deadLetter.Target);
        Assert.Equal(typeof(string).FullName, deadLetter.MessageType);
        Assert.Equal("Actor is stopping.", deadLetter.Reason);

        actor.Release();
    }

    [Fact]
    public async Task Stop_disposes_timers_and_prevents_future_timer_delivery()
    {
        await using ActorSystem system = new();
        TimerRecordingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        await actorRef.Send(new StartTimer());
        await Eventually(() => actor.Ticks > 0);

        await actorHandle.Stop();
        int ticksAfterStop = actor.Ticks;

        await Task.Delay(80);

        Assert.Equal(ticksAfterStop, actor.Ticks);
    }

    [Fact]
    public async Task Stop_disposes_timers_scheduled_by_stopping_hook()
    {
        await using ActorSystem system = new();
        List<DeadLetter> deadLetters = new();
        system.DeadLetterPublished += deadLetters.Add;
        ActorHandle<object> actorHandle = await system.SpawnAsync(new StoppingTimerActor());

        await actorHandle.Stop();
        await Task.Delay(80);

        Assert.Empty(deadLetters);
    }

    [Fact]
    public async Task Background_work_completion_is_delivered_through_actor_mailbox()
    {
        await using ActorSystem system = new();
        OffloadActor actor = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(actor)).Ref;

        await actorRef.Send(new StartOffload(21));
        await Eventually(() => actor.Events.Contains("complete"));
        int doubled = await actorRef.Call<int>(new GetOffloadResult(), DefaultCallOptions);

        Assert.Equal(42, doubled);
        Assert.Equal(new[] { "start", "complete", "get" }, actor.Events);
        Assert.Equal(1, actor.MaxConcurrency);
    }

    [Fact]
    public async Task Actor_state_transitions_from_active_to_draining_to_dead()
    {
        await using ActorSystem system = new();
        BlockingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        Assert.Equal(ActorState.Active, actorHandle.GetState());

        await actorRef.Send("blocked");

        Task<ActorStopResult> stopTask = actorHandle.Stop(TimeSpan.FromSeconds(1)).AsTask();

        await Eventually(() => actorHandle.GetState() == ActorState.Draining);

        actor.Release();
        ActorStopResult result = await stopTask;

        Assert.Equal(ActorState.Dead, system.GetActorState(actorRef.Id));
        Assert.Equal(ActorStopResult.Drained, result);
    }

    [Fact]
    public async Task Actor_state_stays_draining_when_drain_times_out_until_work_finishes()
    {
        await using ActorSystem system = new();
        BlockingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        await actorRef.Send("blocked");

        ActorStopResult result = await actorHandle.Stop(TimeSpan.FromMilliseconds(20));

        Assert.Equal(ActorStopResult.TimedOut, result);
        Assert.Equal(ActorState.Draining, actorHandle.GetState());

        actor.Release();
        await Eventually(() => system.GetActorState(actorRef.Id) == ActorState.Dead);
    }

    [Fact]
    public async Task Message_interceptor_receives_before_and_after_callbacks()
    {
        List<string> events = new();
        RecordingInterceptor interceptor = new(events);

        await using ActorSystem system = new(new ActorSystemOptions
        {
            MessageInterceptor = interceptor
        });

        ActorRef<object> actorRef = (await system.SpawnAsync(new EchoActor())).Ref;

        string response = await actorRef.Call<string>("hello", DefaultCallOptions);

        Assert.Equal("hello", response);
        await Eventually(() => events.Count == 2);
        Assert.Equal(2, events.Count);
        Assert.Equal("before:hello", events[0]);
        Assert.Equal("after:hello:null", events[1]);
    }

    [Fact]
    public async Task Message_interceptor_reports_error_on_failed_dispatch()
    {
        List<string> events = new();
        RecordingInterceptor interceptor = new(events);

        await using ActorSystem system = new(new ActorSystemOptions
        {
            MessageInterceptor = interceptor
        });

        ActorRef<object> actorRef = (await system.SpawnAsync(new ThrowingActor())).Ref;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actorRef.Call<string>("fail", DefaultCallOptions));

        Assert.Equal(2, events.Count);
        Assert.Equal("before:fail", events[0]);
        Assert.StartsWith("after:fail:", events[1]);
        Assert.Contains("InvalidOperationException", events[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Message_interceptor_before_errors_do_not_prevent_actor_dispatch()
    {
        TaskCompletionSource<ActorObserverError> observerError = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ActorSystem system = new(new ActorSystemOptions
        {
            MessageInterceptor = new ThrowingBeforeInterceptor()
        });
        system.ObserverErrorPublished += error => observerError.TrySetResult(error);

        ActorRef<object> actorRef = (await system.SpawnAsync(new EchoActor())).Ref;

        string response = await actorRef.Call<string>("hello", DefaultCallOptions);
        ActorObserverError error = await observerError.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("hello", response);
        Assert.Equal(ActorObserverErrorSource.MessageInterceptorBefore, error.Source);
        Assert.Equal(actorRef.Id, error.ActorId);
        Assert.Equal(typeof(string).FullName, error.MessageType);
    }

    [Fact]
    public async Task Message_interceptor_after_errors_do_not_prevent_actor_dispatch()
    {
        TaskCompletionSource<ActorObserverError> observerError = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ActorSystem system = new(new ActorSystemOptions
        {
            MessageInterceptor = new ThrowingAfterInterceptor()
        });
        system.ObserverErrorPublished += error => observerError.TrySetResult(error);

        ActorRef<object> actorRef = (await system.SpawnAsync(new EchoActor())).Ref;

        string response = await actorRef.Call<string>("hello", DefaultCallOptions);
        ActorObserverError error = await observerError.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("hello", response);
        Assert.Equal(ActorObserverErrorSource.MessageInterceptorAfter, error.Source);
        Assert.Equal(actorRef.Id, error.ActorId);
        Assert.Equal(typeof(string).FullName, error.MessageType);
    }

    [Fact]
    public void Public_options_do_not_expose_execution_timeout()
    {
        Assert.Null(typeof(ActorSystemOptions).GetProperty("ExecutionTimeout"));
    }

    [Fact]
    public async Task Send_to_stopped_actor_publishes_dead_letter()
    {
        await using ActorSystem system = new();
        List<DeadLetter> deadLetters = new();
        system.DeadLetterPublished += deadLetters.Add;
        ActorHandle<object> actorHandle = await system.SpawnAsync(new IgnoringActor());
        ActorRef<object> actorRef = actorHandle.Ref;

        await actorHandle.Stop();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actorRef.Send("late-message"));

        DeadLetter deadLetter = Assert.Single(deadLetters);
        Assert.Equal(actorRef.Id, deadLetter.Target);
        Assert.Equal(typeof(string).FullName, deadLetter.MessageType);
        Assert.Equal("Actor does not exist.", deadLetter.Reason);
    }

    private sealed class ProbeActor : IActor<object>
    {
        private readonly Queue<object> messages = new();
        private readonly SemaphoreSlim available = new(0);

        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            messages.Enqueue(message);
            available.Release();
            return ValueTask.CompletedTask;
        }

        public async Task<object> NextMessage()
        {
            await available.WaitAsync(TimeSpan.FromSeconds(1));
            return messages.Dequeue();
        }
    }

    private sealed class EchoActor : IActor<object>
    {
        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            ctx.Respond(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class IgnoringActor : IActor<object>
    {
        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderingActor : IActor<object>
    {
        private readonly List<int> values = new();

        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            switch (message)
            {
                case int value:
                    values.Add(value);
                    break;
                case GetValues:
                    ctx.Respond(values.ToArray());
                    break;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ConcurrencyProbeActor : IActor<object>
    {
        private int active;
        private int maxConcurrency;

        public async ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            switch (message)
            {
                case int:
                    int current = Interlocked.Increment(ref active);
                    maxConcurrency = Math.Max(maxConcurrency, current);
                    await Task.Delay(5);
                    Interlocked.Decrement(ref active);
                    break;
                case GetMaxConcurrency:
                    ctx.Respond(maxConcurrency);
                    break;
            }
        }
    }

    private sealed class TimerActor : IActor<object>
    {
        private int ticks;
        private IDisposable? timer;

        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            switch (message)
            {
                case StartTimer:
                    timer = ctx.ScheduleRepeated(new Tick(), TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
                    break;
                case Tick:
                    ticks++;
                    break;
                case GetTickCount:
                    timer?.Dispose();
                    ctx.Respond(ticks);
                    break;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TimerRecordingActor : IActor<object>
    {
        public int Ticks { get; private set; }

        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            switch (message)
            {
                case StartTimer:
                    ctx.ScheduleRepeated(new Tick(), TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
                    break;
                case Tick:
                    Ticks++;
                    break;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingTimerActor : IActor<object>
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int tickStarted;
        private IDisposable? timer;

        public TaskCompletionSource TickStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            switch (message)
            {
                case StartTimer:
                    timer = ctx.ScheduleRepeated(new Tick(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
                    break;
                case Tick:
                    if (Interlocked.Exchange(ref tickStarted, 1) == 0)
                    {
                        TickStarted.SetResult();
                    }

                    await release.Task;
                    break;
            }
        }

        public void Release()
        {
            timer?.Dispose();
            release.SetResult();
        }
    }

    private sealed class DisposeGaugeBlockingActor : IActor<object>
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int messageStarted;

        public TaskCompletionSource MessageStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            if (Interlocked.Exchange(ref messageStarted, 1) == 0)
            {
                MessageStarted.SetResult();
            }

            await release.Task;
        }

        public void Release()
        {
            release.SetResult();
        }
    }

    private sealed class BlockingActor : IActor<object>
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            await gate.Task;
        }

        public void Release()
        {
            gate.SetResult();
        }
    }

    private sealed class RecordingActor : IActor<object>
    {
        private readonly List<int> values = new();

        public IReadOnlyList<int> Values => values;

        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            if (message is int value)
            {
                values.Add(value);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class SlowActor(TimeSpan delay) : IActor<object>
    {
        public async ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            await Task.Delay(delay);
        }
    }

    private sealed class OffloadActor : IActor<object>
    {
        private readonly List<string> events = new();
        private int active;
        private int maxConcurrency;
        private int result;

        public IReadOnlyList<string> Events => events;

        public int MaxConcurrency => maxConcurrency;

        public async ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            int current = Interlocked.Increment(ref active);
            maxConcurrency = Math.Max(maxConcurrency, current);

            try
            {
                switch (message)
                {
                    case StartOffload start:
                        events.Add("start");
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(20);
                            await ctx.Self.Send(new OffloadCompleted(start.Value * 2));
                        });
                        break;
                    case OffloadCompleted completed:
                        events.Add("complete");
                        result = completed.Value;
                        break;
                    case GetOffloadResult:
                        events.Add("get");
                        ctx.Respond(result);
                        break;
                }
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }
    }

    private sealed class SelfCallingActor : IActor<object>
    {
        public async ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            if (message is StartSelfCall)
            {
#pragma warning disable ULA001
                await ctx.Self.Call<string>(new InnerSelfCall(), CallOptions(TimeSpan.FromMilliseconds(20)));
#pragma warning restore ULA001
            }
        }
    }

    private sealed class BackgroundSelfCallingActor : IActor<object>
    {
        private readonly TaskCompletionSource releaseBackgroundWork =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> backgroundResult =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> BackgroundResult => backgroundResult.Task;

        public void ReleaseBackgroundWork()
        {
            releaseBackgroundWork.SetResult();
        }

        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            switch (message)
            {
                case StartBackgroundSelfCall:
#pragma warning disable ULA001
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await releaseBackgroundWork.Task;
                            string response = await ctx.Self.Call<string>(new BackgroundSelfCall(), DefaultCallOptions);
                            backgroundResult.TrySetResult(response);
                        }
                        catch (Exception ex)
                        {
                            backgroundResult.TrySetException(ex);
                        }
                    });
#pragma warning restore ULA001
                    ctx.Respond("started");
                    break;
                case BackgroundSelfCall:
                    ctx.Respond("background");
                    break;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class DownstreamCallingActor(ActorRef<object> downstream) : IActor<object>
    {
        public async ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            if (message is StartDownstreamCall)
            {
                await downstream.Call<string>(new DownstreamRequest(), CallOptions(TimeSpan.FromMilliseconds(20)));
            }
        }
    }

    private sealed class CounterActor : IActor<CounterMessage>
    {
        private int value;

        public ValueTask OnMessage(ActorContext<CounterMessage> ctx, CounterMessage message)
        {
            switch (message)
            {
                case Add add:
                    value += add.Value;
                    break;
                case GetCounter:
                    ctx.Respond(value);
                    break;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class LifecycleActor :
        IActor<object>,
        IActorStarted<object>,
        IActorStopping<object>
    {
        private readonly List<string> events = new();

        public IReadOnlyList<string> Events => events;

        public async ValueTask OnStarted(ActorContext<object> ctx)
        {
            events.Add("started");
            await ctx.Self.Send(new StartedMessage());
        }

        public ValueTask OnStopping(ActorContext<object> ctx)
        {
            events.Add("stopping");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            switch (message)
            {
                case StartedMessage:
                    events.Add("started-message");
                    break;
                case GetLifecycleEvents:
                    events.Add("get");
                    ctx.Respond(events.ToArray());
                    break;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingStartActor : IActor<object>, IActorStarted<object>
    {
        public ValueTask OnStarted(ActorContext<object> ctx)
        {
            throw new InvalidOperationException("start failed");
        }

        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingStopActor : IActor<object>, IActorStopping<object>
    {
        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStopping(ActorContext<object> ctx)
        {
            throw new InvalidOperationException("stop failed");
        }
    }

    private sealed class StoppingTimerActor : IActor<object>, IActorStopping<object>
    {
        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStopping(ActorContext<object> ctx)
        {
            ctx.ScheduleRepeated(new Tick(), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SerializedStoppingActor : IActor<object>, IActorStopping<object>
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> events = new();
        private int active;
        private int maxConcurrency;

        public TaskCompletionSource MessageStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StoppingStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Events => events;

        public int MaxConcurrency => maxConcurrency;

        public async ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            int current = Interlocked.Increment(ref active);
            maxConcurrency = Math.Max(maxConcurrency, current);
            events.Add("message-start");
            MessageStarted.SetResult();

            try
            {
                await release.Task;
                events.Add("message-end");
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        public ValueTask OnStopping(ActorContext<object> ctx)
        {
            int current = Interlocked.Increment(ref active);
            maxConcurrency = Math.Max(maxConcurrency, current);
            events.Add("stopping");
            StoppingStarted.SetResult();
            Interlocked.Decrement(ref active);
            return ValueTask.CompletedTask;
        }

        public void Release()
        {
            release.SetResult();
        }
    }

    private static async Task Eventually(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1));

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static ActorCallOptions CallOptions(TimeSpan timeout)
    {
        return new ActorCallOptions(timeout, timeout);
    }

    private sealed record MetricMeasurement(
        string InstrumentName,
        long Value,
        KeyValuePair<string, object?>[] Tags);

    private sealed class RecordingInterceptor(List<string> events) : IActorMessageInterceptor
    {
        public ValueTask OnBeforeMessage(ActorId actorId, object message, CancellationToken cancellationToken)
        {
            events.Add($"before:{message}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnAfterMessage(ActorId actorId, object message, Exception? error, CancellationToken cancellationToken)
        {
            events.Add($"after:{message}:{error?.GetType().Name ?? "null"}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingBeforeInterceptor : IActorMessageInterceptor
    {
        public ValueTask OnBeforeMessage(ActorId actorId, object message, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("before failed");
        }

        public ValueTask OnAfterMessage(ActorId actorId, object message, Exception? error, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAfterInterceptor : IActorMessageInterceptor
    {
        public ValueTask OnBeforeMessage(ActorId actorId, object message, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnAfterMessage(ActorId actorId, object message, Exception? error, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("after failed");
        }
    }

    private sealed class ThrowingActor : IActor<object>
    {
        public ValueTask OnMessage(ActorContext<object> ctx, object message)
        {
            throw new InvalidOperationException("test failure");
        }
    }

    private readonly record struct GetValues;

    private readonly record struct GetMaxConcurrency;

    private readonly record struct StartTimer;

    private readonly record struct Tick;

    private readonly record struct GetTickCount;

    private readonly record struct StartSelfCall;

    private readonly record struct InnerSelfCall;

    private readonly record struct StartBackgroundSelfCall;

    private readonly record struct BackgroundSelfCall;

    private readonly record struct StartDownstreamCall;

    private readonly record struct DownstreamRequest;

    private readonly record struct StartOffload(int Value);

    private readonly record struct OffloadCompleted(int Value);

    private readonly record struct GetOffloadResult;

    private readonly record struct StartedMessage;

    private readonly record struct GetLifecycleEvents;

    private abstract record CounterMessage;

    private sealed record Add(int Value) : CounterMessage;

    private sealed record GetCounter : CounterMessage;
}
