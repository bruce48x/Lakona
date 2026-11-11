using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class HotfixActorMailboxDispatchTests
{
    private static readonly ActorId Id = new("admission/post");

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Accepted_call_keeps_admission_after_wait_ends(bool tell, bool queued, bool timeout)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new CallFixture(tell, timeout ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(10));
        await fixture.Catalog.CreateAsync<TestActor>(Id, ct);
        var blockingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var gate = new RecordingGate();
        var control = new CallControl();
        try
        {
            if (queued)
            {
                Assert.Equal(ActorTellResult.Accepted, fixture.Catalog.TryTell<TestActor>(Id, async (_, _) =>
                {
                    blockingEntered.TrySetResult();
                    await blockingRelease.Task;
                }, ct));
                await blockingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            var call = fixture.Call(control, gate, caller.Token);
            if (!queued) await control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            if (timeout) await Assert.ThrowsAsync<TimeoutException>(() => call.WaitAsync(TimeSpan.FromSeconds(5), ct));
            else
            {
                caller.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            }
            var drain = gate.Inner.CloseAndDrainAsync(TimeSpan.FromSeconds(5), ct).AsTask();
            Assert.False(drain.IsCompleted);
            Assert.Equal(0, gate.Exits);
            blockingRelease.TrySetResult();
            await control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.False(drain.IsCompleted);
            control.Release.TrySetResult();
            Assert.True(await drain);
            Assert.True(control.Completed);
            Assert.Equal(1, gate.Exits);
        }
        finally
        {
            blockingRelease.TrySetResult();
            control.Release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Call_rejection_or_business_failure_releases_exactly_once(bool tell, bool accepted)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new CallFixture(tell, TimeSpan.FromSeconds(5));
        var control = new CallControl { Fail = true };
        control.Release.TrySetResult();
        if (accepted) await fixture.Catalog.CreateAsync<TestActor>(Id, ct);
        var gate = new RecordingGate();
        if (accepted) await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Call(control, gate, ct));
        else await Assert.ThrowsAsync<ActorNotFoundException>(() => fixture.Call(control, gate, ct));
        Assert.True(await gate.Inner.CloseAndDrainAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(1, gate.Exits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Call_canceled_before_acceptance_releases_admission(bool tell)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new CallFixture(tell, TimeSpan.FromSeconds(5));
        await fixture.Catalog.CreateAsync<TestActor>(Id, ct);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var gate = new RecordingGate(caller.Cancel);
        var control = new CallControl();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Call(control, gate, caller.Token));
        Assert.True(await gate.Inner.CloseAndDrainAsync(TimeSpan.FromSeconds(5), ct));
        Assert.False(control.Entered.Task.IsCompleted);
        Assert.Equal(1, gate.Exits);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Full_mailbox_releases_unaccepted_call_on_cancel_or_timeout(bool tell, bool timeout)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new CallFixture(tell, timeout ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(10), capacity: 1);
        await fixture.Catalog.CreateAsync<TestActor>(Id, ct);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = new CallControl();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            Assert.Equal(ActorTellResult.Accepted, fixture.Catalog.TryTell<TestActor>(Id, async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task;
            }, ct));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            // The executing turn retains the mailbox's only capacity slot.
            var gate = new RecordingGate();
            var call = fixture.Call(control, gate, caller.Token);
            if (timeout) await Assert.ThrowsAsync<TimeoutException>(() => call.WaitAsync(TimeSpan.FromSeconds(5), ct));
            else
            {
                caller.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            }
            Assert.True(await gate.Inner.CloseAndDrainAsync(TimeSpan.FromSeconds(5), ct));
            Assert.False(control.Entered.Task.IsCompleted);
            Assert.Equal(1, gate.Exits);
        }
        finally
        {
            release.TrySetResult();
            control.Release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Self_call_releases_admission_after_inline_completion(bool tell)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new CallFixture(tell, TimeSpan.FromSeconds(5));
        await fixture.Catalog.CreateAsync<TestActor>(Id, ct);
        var gate = new RecordingGate();
        var control = new CallControl();
        control.Release.TrySetResult();
        await fixture.Catalog.TellAsync<TestActor>(Id, async (_, _) => await fixture.Call(control, gate, ct), ct);
        Assert.True(control.Completed);
        Assert.True(await gate.Inner.CloseAndDrainAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(1, gate.Exits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Custom_runtime_cannot_claim_distributed_admission_without_acceptance_tracking(bool tell)
    {
        var gate = new RecordingGate();
        var runtime = new PostRuntime();
        var ct = TestContext.Current.CancellationToken;
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => tell
            ? HotfixActorMailboxDispatch.TellAsync<TestActor, bool>(runtime, Id, null!, 1, false, gate, ct).AsTask()
            : HotfixActorMailboxDispatch.AskAsync<TestActor, bool, int>(runtime, Id, null!, 1, false, gate, ct).AsTask());
        Assert.Contains("mailbox acceptance tracking", error.Message);
        Assert.Equal(0, gate.Exits);
        Assert.True(await gate.Inner.CloseAndDrainAsync(TimeSpan.FromSeconds(5), ct));
    }

    public sealed class CallControl
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fail, Completed;
    }

    public sealed class CallBehavior
    {
        public async ValueTask<int> AskAsync(TestActor actor, CallControl control)
        {
            control.Entered.TrySetResult();
            await control.Release.Task;
            control.Completed = true;
            if (control.Fail) throw new InvalidOperationException("Business failure");
            return 7;
        }
        public async ValueTask TellAsync(TestActor actor, CallControl control) => await AskAsync(actor, control);
    }

    private sealed class CallFixture : IAsyncDisposable
    {
        private readonly bool tell;
        private readonly ServiceProvider services;
        private readonly HotfixDispatchTable table;
        private readonly HotfixActorMethodDescriptor descriptor;
        private readonly Accessor accessor;
        public ActorActivationCatalog Catalog { get; }
        public CallFixture(bool tell, TimeSpan timeout, int capacity = 4096)
        {
            this.tell = tell;
            services = new ServiceCollection().AddLakonaGameServerActors(options =>
            {
                options.CallTimeout = timeout;
                options.MailboxCapacity = capacity;
            }).BuildServiceProvider();
            Catalog = services.GetRequiredService<ActorActivationCatalog>();
            var method = tell ? nameof(CallBehavior.TellAsync) : nameof(CallBehavior.AskAsync);
            descriptor = new HotfixActorMethodDescriptor("admission/" + method, typeof(CallBehavior), typeof(TestActor),
                method, typeof(CallControl), tell ? null : typeof(int), typeof(CallBehavior).GetMethod(method)!);
            table = new HotfixDispatchTable(1, [], [], [descriptor]);
            table.ValidateModuleActivation(services);
            accessor = new Accessor(new HotfixRuntimeSnapshot(new HotfixServiceInvoker(table), services, table,
                services, typeof(CallBehavior).Assembly, null, "test", null, ownsRuntimeResources: false, onRetired: null));
        }
        public Task Call(CallControl control, IDistributedWorkAdmissionGate gate, CancellationToken ct) => tell
            ? HotfixActorMailboxDispatch.TellAsync<TestActor, CallControl>(Catalog, Id, accessor, descriptor.MethodId, control, gate, ct).AsTask()
            : HotfixActorMailboxDispatch.AskAsync<TestActor, CallControl, int>(Catalog, Id, accessor, descriptor.MethodId, control, gate, ct).AsTask();
        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await table.DisposeAsync();
        }
    }

    [Fact]
    public async Task Cancellation_after_admission_releases_token_without_enqueueing()
    {
        await using var services = new ServiceCollection().AddLakonaGameServerActors().BuildServiceProvider();
        var catalog = services.GetRequiredService<ActorActivationCatalog>();
        await catalog.CreateAsync<TestActor>(Id, TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var gate = new RecordingGate(cancellation.Cancel);
        cancellation.Token.ThrowIfCancellationRequested(); // Generated Post's pre-check has passed.
        Assert.ThrowsAny<OperationCanceledException>(() => Post(catalog, gate, cancellation.Token));
        Assert.Equal(1, gate.Exits);
        Assert.True(await gate.Inner.CloseAndDrainAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Synchronous_enqueue_exception_is_preserved_and_releases_token()
    {
        var failure = new InvalidOperationException("Before acceptance");
        var runtime = new PostRuntime { Failure = failure };
        var gate = new RecordingGate();
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Post(runtime, gate, TestContext.Current.CancellationToken)));
        Assert.Equal(1, gate.Exits);
        Assert.True(await gate.Inner.CloseAndDrainAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(ActorTellResult.ActorNotFound)]
    [InlineData(ActorTellResult.ActorUnavailable)]
    [InlineData(ActorTellResult.MailboxFull)]
    public async Task Rejected_post_releases_once(ActorTellResult result)
    {
        var gate = new RecordingGate();
        Assert.Equal(result, Post(new PostRuntime { Result = result }, gate, TestContext.Current.CancellationToken));
        Assert.Equal(1, gate.Exits);
        Assert.True(await gate.Inner.CloseAndDrainAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Accepted_post_transfers_release_to_callback(bool inline, bool fail)
    {
        var descriptor = new HotfixActorMethodDescriptor("admission/post", typeof(Behavior), typeof(TestActor),
            nameof(Behavior.PostAsync), typeof(bool), null, typeof(Behavior).GetMethod(nameof(Behavior.PostAsync))!);
        await using var table = new HotfixDispatchTable(1, [], [], [descriptor]);
        await using var services = new ServiceCollection().BuildServiceProvider();
        table.ValidateModuleActivation(services);
        var accessor = new Accessor(new HotfixRuntimeSnapshot(new HotfixServiceInvoker(table), services, table,
            services, typeof(Behavior).Assembly, null, "test", null, ownsRuntimeResources: false, onRetired: null));
        var gate = new RecordingGate();
        var runtime = new PostRuntime { Inline = inline };
        Assert.Equal(ActorTellResult.Accepted, HotfixActorMailboxDispatch.TryTell<TestActor, bool>(
            runtime, Id, accessor, descriptor.MethodId, fail, gate, TestContext.Current.CancellationToken));
        var drain = gate.Inner.CloseAndDrainAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).AsTask();
        if (!inline)
        {
            Assert.False(drain.IsCompleted);
            Assert.Equal(0, gate.Exits);
            runtime.Completion = runtime.Work!().AsTask();
        }
        if (fail) await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Completion!);
        else await runtime.Completion!;
        Assert.True(await drain);
        Assert.Equal(1, gate.Exits);
    }

    [Fact]
    public async Task Closed_gate_rejects_before_runtime_and_absent_gate_preserves_result()
    {
        var gate = new RecordingGate();
        await gate.Inner.CloseAndDrainAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(ActorTellResult.ActorUnavailable, Post(new PostRuntime { Failure = new Exception("Must not run") }, gate, TestContext.Current.CancellationToken));
        Assert.Equal(0, gate.Exits);
        Assert.Equal(ActorTellResult.MailboxFull, Post(new PostRuntime { Result = ActorTellResult.MailboxFull }, null, TestContext.Current.CancellationToken));
    }

    private static ActorTellResult Post(IActorRuntime runtime, IDistributedWorkAdmissionGate? gate,
        CancellationToken cancellationToken = default) =>
        HotfixActorMailboxDispatch.TryTell<TestActor, bool>(runtime, Id, null!, 1, false, gate, cancellationToken);

    public sealed class TestActor : Actor;
    public sealed class Behavior
    {
        public ValueTask PostAsync(TestActor actor, bool fail)
        {
            if (fail) throw new InvalidOperationException("Behavior failed");
            return default;
        }
    }
    private sealed class Accessor(HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current => snapshot;
    }
    private sealed class RecordingGate(Action? admitted = null) : IDistributedWorkAdmissionGate
    {
        public DistributedWorkAdmissionGate Inner { get; } = OpenGate();
        private int exits;
        public int Exits => Volatile.Read(ref exits);
        public bool IsOpen => Inner.IsOpen;
        public bool TryEnter(out DistributedWorkAdmission admission)
        {
            if (!Inner.TryEnter(out admission)) return false;
            admitted?.Invoke();
            return true;
        }
        public void Exit(DistributedWorkAdmission admission) { Interlocked.Increment(ref exits); Inner.Exit(admission); }
        private static DistributedWorkAdmissionGate OpenGate() { var gate = new DistributedWorkAdmissionGate(); gate.Open(); return gate; }
    }
    private sealed class PostRuntime : IActorRuntime
    {
        public ActorTellResult Result { get; init; } = ActorTellResult.Accepted;
        public Exception? Failure { get; init; }
        public bool Inline { get; init; }
        public Func<ValueTask>? Work { get; private set; }
        public Task? Completion { get; set; }
        public ActorTellResult TryTell<TActor>(ActorId id, Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default) where TActor : class, IActor
        {
            if (Failure is not null) throw Failure;
            if (Result == ActorTellResult.Accepted)
            {
                Work = () => message((TActor)(object)new TestActor(), CancellationToken.None);
                if (Inline)
                {
                    Completion = Work().AsTask();
                    Assert.True(Completion.IsCompleted); // Callback finishes before acceptance is returned.
                }
            }
            return Result;
        }
        public ValueTask TellAsync<TActor>(ActorId id, Func<TActor, CancellationToken, ValueTask> message, CancellationToken cancellationToken = default) where TActor : class, IActor => throw new NotSupportedException();
        public ValueTask TellAsync(Type actorType, ActorId id, Func<IActor, CancellationToken, ValueTask> message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ActorTellResult TryTell(Type actorType, ActorId id, Func<IActor, CancellationToken, ValueTask> message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TResult> AskAsync<TActor, TResult>(ActorId id, Func<TActor, CancellationToken, ValueTask<TResult>> message, CancellationToken cancellationToken = default) where TActor : class, IActor => throw new NotSupportedException();
        public IReadOnlyList<ActorId> GetActiveActorIds(Type actorType) => throw new NotSupportedException();
        public bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics) => throw new NotSupportedException();
        public ActorState GetState(ActorId id) => throw new NotSupportedException();
    }
}
