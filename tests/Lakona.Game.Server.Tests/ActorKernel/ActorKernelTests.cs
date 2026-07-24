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
    public async Task TrySend_dispatches_message()
    {
        await using ActorSystem system = new();
        ProbeActor actor = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(actor)).Ref;

        Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("hello"));

        Assert.Equal("hello", await actor.NextMessage());
    }

    [Fact]
    public async Task Call_returns_actor_response()
    {
        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new EchoActor())).Ref;

        string response = await actorRef.Call<string>("ping", DefaultCallOptions, TestContext.Current.CancellationToken);

        Assert.Equal("ping", response);
    }

    [Fact]
    public void Call_requires_distinct_queue_and_response_timeouts()
    {
        System.Reflection.MethodInfo call = Assert.Single(
            typeof(ActorRef<object>).GetMethods(),
            static method => method.Name == nameof(ActorRef<object>.Call));

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
            await actorRef.Call<string>("ping", CallOptions(TimeSpan.FromMilliseconds(20)), TestContext.Current.CancellationToken));
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
            await actorRef.Call<string>("ping", options, TestContext.Current.CancellationToken));

        ActorCallTimeout timeout = await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

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
            Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("first"));

            TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
                await actorRef.Call<string>("queued", options, TestContext.Current.CancellationToken));

            ActorCallTimeout timeout = await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

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

        string response = await actorRef.Call<string>("ping", options, TestContext.Current.CancellationToken);

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

        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, actorHandle.GetMailboxMetrics().ProcessedCount);
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
            await upstream.Call<string>(new StartDownstreamCall(), DefaultCallOptions, TestContext.Current.CancellationToken));

        ActorCallTimeout timeout = await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(upstream.Id, timeout.Caller);
        Assert.Equal(downstream.Id, timeout.Target);
        Assert.Equal(typeof(DownstreamRequest).FullName, timeout.RequestType);
        Assert.Equal(ActorCallTimeoutReason.ResponseTimeout, timeout.Reason);
        Assert.Equal(new[] { upstream.Id }, timeout.CallChain);
    }

    [Fact]
    public async Task Mailbox_processes_messages_in_post_order()
    {
        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new OrderingActor())).Ref;

        for (int i = 0; i < 64; i++)
        {
            Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend(i));
        }

        int[] values = await actorRef.Call<int[]>(new GetValues(), DefaultCallOptions, TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(0, 64), values);
    }

    [Fact]
    public async Task Mailbox_never_executes_same_actor_concurrently()
    {
        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new ConcurrencyProbeActor())).Ref;

        for (int i = 0; i < 32; i++)
        {
            Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend(i));
        }

        int maxConcurrency = await actorRef.Call<int>(new GetMaxConcurrency(), DefaultCallOptions, TestContext.Current.CancellationToken);

        Assert.Equal(1, maxConcurrency);
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
            Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("first"));
            await actor.MessageStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("second"));

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
    public async Task Slow_message_detection_publishes_event_when_threshold_is_exceeded()
    {
        await using ActorSystem system = new(new ActorSystemOptions
        {
            SlowMessageThreshold = TimeSpan.FromMilliseconds(10)
        });

        TaskCompletionSource<SlowMessage> detected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        system.SlowMessageDetected += message => detected.TrySetResult(message);
        ActorRef<object> actorRef = (await system.SpawnAsync(new SlowActor(TimeSpan.FromMilliseconds(30)))).Ref;

        Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("slow"));

        SlowMessage slowMessage = await detected.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

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
        Assert.Null(typeof(ActorKernelContext<object>).GetProperty("System"));
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
                    (Equals(activity.GetTagItem("lakona-game.actor.slow_message"), true) ||
                     activity.Events.Any(evt => evt.Name == "Lakona.Game.Actor.SlowMessage")))
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

        Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("slow"));

        Activity activity = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(true, activity.GetTagItem("lakona-game.actor.slow_message"));
        Assert.Null(activity.GetTagItem("lakona-actor.slow_message"));
        Assert.Null(activity.GetTagItem("lakona-actor.slow_message.elapsed_ms"));
        Assert.Contains(activity.Events, evt => evt.Name == "Lakona.Game.Actor.SlowMessage");
        Assert.DoesNotContain(activity.Events, evt => evt.Name == "Lakona.Actor.Actor.SlowMessage");
    }

    [Fact]
    public async Task Typed_actor_ref_posts_and_calls_typed_messages()
    {
        await using ActorSystem system = new();
        ActorRef<CounterMessage> counter = (await system.SpawnAsync<CounterMessage>(new CounterActor())).Ref;

        Assert.Equal(ActorSendResult.Accepted, counter.TrySend(new Add(2)));
        int value = await counter.Call<int>(new GetCounter(), DefaultCallOptions, TestContext.Current.CancellationToken);

        Assert.Equal(2, value);
    }

    [Fact]
    public async Task Typed_actor_handle_exposes_runtime_operations()
    {
        await using ActorSystem system = new(new ActorSystemOptions { MailboxCapacity = 4 });
        ActorHandle<CounterMessage> counter = await system.SpawnAsync<CounterMessage>(new CounterActor());

        Assert.Equal(4, counter.GetMailboxMetrics().Capacity);

        await counter.Stop();

        Assert.Equal(ActorSendResult.ActorUnavailable, counter.Ref.TrySend(new Add(1)));
    }

    [Fact]
    public void Actor_handle_does_not_convert_to_actor_ref()
    {
        Assert.DoesNotContain(
            typeof(ActorHandle<object>).GetMethods(),
            static method => method.IsSpecialName && method.Name is "op_Implicit" or "op_Explicit");
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
        Assert.DoesNotContain(
            typeof(ActorSystem).GetMethods(),
            static method => method.Name == "Spawn");

        System.Reflection.MethodInfo spawnAsync = Assert.Single(
            typeof(ActorSystem).GetMethods(),
            static method =>
                method.Name == "SpawnAsync" &&
                method.IsPublic &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType.Name == "IActor`1");

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

        Assert.DoesNotContain("Send", actorRefMembers);
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
                if (activity.OperationName == "Lakona.Actor.Actor.Dispatch" &&
                    string.Equals(activity.GetTagItem("lakona-game.actor.message.type")?.ToString(), typeof(string).FullName, StringComparison.Ordinal) &&
                    string.Equals(activity.GetTagItem("lakona-game.actor.message.kind")?.ToString(), "call", StringComparison.Ordinal))
                {
                    stopped.TrySetResult(activity);
                }
            }
        };

        ActivitySource.AddActivityListener(listener);

        await using ActorSystem system = new();
        ActorRef<object> actorRef = (await system.SpawnAsync(new EchoActor())).Ref;

        string response = await actorRef.Call<string>("trace-me", DefaultCallOptions, TestContext.Current.CancellationToken);
        Activity activity = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal("trace-me", response);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Null(activity.GetTagItem("lakona-actor.actor.id"));
        Assert.Null(activity.GetTagItem("lakona-actor.call.chain"));
        Assert.NotNull(activity.GetTagItem("lakona-game.actor.type"));
        Assert.Equal(typeof(string).FullName, activity.GetTagItem("lakona-game.actor.message.type"));
        Assert.Equal("call", activity.GetTagItem("lakona-game.actor.message.kind"));
    }

    [Fact]
    public async Task Post_and_call_dispatch_activities_preserve_parent_activity_context()
    {
        using ActivitySource testSource = new("Lakona.Actor.Tests");
        ConcurrentQueue<Activity> stopped = new();

        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name is LakonaActorDiagnostics.ActivitySourceName or "Lakona.Actor.Tests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                string? messageType = activity.GetTagItem("lakona-game.actor.message.type")?.ToString();

                if (activity.OperationName == "Lakona.Actor.Actor.Dispatch" &&
                    messageType is not null &&
                    (messageType == typeof(ParentTraceSend).FullName ||
                     messageType == typeof(ParentTraceCall).FullName))
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

        Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend(new ParentTraceSend()));
        ParentTraceCall response = await actorRef.Call<ParentTraceCall>(new ParentTraceCall(), DefaultCallOptions, TestContext.Current.CancellationToken);
        await Eventually(() => stopped.Count >= 2);

        Assert.Equal(new ParentTraceCall(), response);
        Activity[] activities = stopped.ToArray();
        Assert.Equal(2, activities.Length);
        Assert.All(activities, activity => Assert.Equal(parent!.SpanId, activity.ParentSpanId));
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
            Assert.Equal(ActorSendResult.Accepted, echo.TrySend("send"));
            _ = await echo.Call<string>("call", DefaultCallOptions, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await ignoring.Call<string>("timeout", CallOptions(TimeSpan.FromMilliseconds(20)), TestContext.Current.CancellationToken));
            Assert.Equal(ActorSendResult.Accepted, blocked.TrySend("active"));
            Assert.Equal(ActorSendResult.Accepted, blocked.TrySend("queued"));
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

        Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("active"));
        await actor.MessageStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("queued"));

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
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }
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
            Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend(i));
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
            Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend(i));
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

        Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("blocked"));

        ActorStopResult result = await actorHandle.Stop(TimeSpan.FromMilliseconds(20));

        Assert.Equal(ActorStopResult.TimedOut, result);
        Assert.Equal(ActorSendResult.ActorUnavailable, actorRef.TrySend("late"));

        DeadLetter deadLetter = Assert.Single(deadLetters);
        Assert.Equal(actorRef.Id, deadLetter.Target);
        Assert.Equal(typeof(string).FullName, deadLetter.MessageType);
        Assert.Equal("Actor is stopping.", deadLetter.Reason);

        actor.Release();
    }

    [Fact]
    public async Task Actor_state_transitions_from_active_to_draining_to_dead()
    {
        await using ActorSystem system = new();
        BlockingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        Assert.Equal(ActorState.Active, actorHandle.GetState());

        Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("blocked"));

        Task<ActorStopResult> stopTask = actorHandle.Stop(TimeSpan.FromSeconds(1)).AsTask();

        await Eventually(() => actorHandle.GetState() == ActorState.Draining);

        actor.Release();
        ActorStopResult result = await stopTask;

        Assert.Equal(ActorState.Dead, actorHandle.GetState());
        Assert.Equal(ActorStopResult.Drained, result);
    }

    [Fact]
    public async Task Actor_state_stays_draining_when_drain_times_out_until_work_finishes()
    {
        await using ActorSystem system = new();
        BlockingActor actor = new();
        ActorHandle<object> actorHandle = await system.SpawnAsync(actor);
        ActorRef<object> actorRef = actorHandle.Ref;

        Assert.Equal(ActorSendResult.Accepted, actorRef.TrySend("blocked"));

        ActorStopResult result = await actorHandle.Stop(TimeSpan.FromMilliseconds(20));

        Assert.Equal(ActorStopResult.TimedOut, result);
        Assert.Equal(ActorState.Draining, actorHandle.GetState());

        actor.Release();
        await Eventually(() => actorHandle.GetState() == ActorState.Dead);
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

        string response = await actorRef.Call<string>("hello", DefaultCallOptions, TestContext.Current.CancellationToken);

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
            await actorRef.Call<string>("fail", DefaultCallOptions, TestContext.Current.CancellationToken));

        Assert.Equal(2, events.Count);
        Assert.Equal("before:fail", events[0]);
        Assert.StartsWith("after:fail:", events[1]);
        Assert.Contains("InvalidOperationException", events[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Message_interceptor_before_errors_do_not_prevent_actor_dispatch()
    {
        await using ActorSystem system = new(new ActorSystemOptions
        {
            MessageInterceptor = new ThrowingBeforeInterceptor()
        });

        ActorRef<object> actorRef = (await system.SpawnAsync(new EchoActor())).Ref;

        string response = await actorRef.Call<string>("hello", DefaultCallOptions, TestContext.Current.CancellationToken);

        Assert.Equal("hello", response);
    }

    [Fact]
    public async Task Message_interceptor_after_errors_do_not_prevent_actor_dispatch()
    {
        await using ActorSystem system = new(new ActorSystemOptions
        {
            MessageInterceptor = new ThrowingAfterInterceptor()
        });

        ActorRef<object> actorRef = (await system.SpawnAsync(new EchoActor())).Ref;

        string response = await actorRef.Call<string>("hello", DefaultCallOptions, TestContext.Current.CancellationToken);

        Assert.Equal("hello", response);
    }

    [Fact]
    public void Public_options_do_not_expose_execution_timeout()
    {
        Assert.Null(typeof(ActorSystemOptions).GetProperty("ExecutionTimeout"));
    }

    private sealed class ProbeActor : IActor<object>
    {
        private readonly Queue<object> messages = new();
        private readonly SemaphoreSlim available = new(0);

        public ValueTask OnMessage(ActorKernelContext<object> ctx, object message)
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
        public ValueTask OnMessage(ActorKernelContext<object> ctx, object message)
        {
            ctx.Respond(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class IgnoringActor : IActor<object>
    {
        public ValueTask OnMessage(ActorKernelContext<object> ctx, object message)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderingActor : IActor<object>
    {
        private readonly List<int> values = new();

        public ValueTask OnMessage(ActorKernelContext<object> ctx, object message)
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

        public async ValueTask OnMessage(ActorKernelContext<object> ctx, object message)
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

    private sealed class DisposeGaugeBlockingActor : IActor<object>
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int messageStarted;

        public TaskCompletionSource MessageStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask OnMessage(ActorKernelContext<object> ctx, object message)
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

        public async ValueTask OnMessage(ActorKernelContext<object> ctx, object message)
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

        public ValueTask OnMessage(ActorKernelContext<object> ctx, object message)
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
        public async ValueTask OnMessage(ActorKernelContext<object> ctx, object message)
        {
            await Task.Delay(delay);
        }
    }

    private sealed class DownstreamCallingActor(ActorRef<object> downstream) : IActor<object>
    {
        public async ValueTask OnMessage(ActorKernelContext<object> ctx, object message)
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

        public ValueTask OnMessage(ActorKernelContext<CounterMessage> ctx, CounterMessage message)
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
        public ValueTask OnMessage(ActorKernelContext<object> ctx, object message)
        {
            throw new InvalidOperationException("test failure");
        }
    }

    private readonly record struct GetValues;

    private readonly record struct GetMaxConcurrency;

    private readonly record struct StartDownstreamCall;

    private readonly record struct DownstreamRequest;

    private readonly record struct ParentTraceSend;

    private readonly record struct ParentTraceCall;

    private abstract record CounterMessage;

    private sealed record Add(int Value) : CounterMessage;

    private sealed record GetCounter : CounterMessage;
}
