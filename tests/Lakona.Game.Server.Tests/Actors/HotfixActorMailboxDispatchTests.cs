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
        public int Exits { get; private set; }
        public bool IsOpen => Inner.IsOpen;
        public bool TryEnter(out DistributedWorkAdmission admission)
        {
            if (!Inner.TryEnter(out admission)) return false;
            admitted?.Invoke();
            return true;
        }
        public void Exit(DistributedWorkAdmission admission) { Inner.Exit(admission); Exits++; }
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
