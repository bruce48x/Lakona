using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ClusterActorTellDispatcherTests
{
    [Fact]
    public async Task AcceptedTryTellReturnsAcceptedAndDispatchesThroughMailbox()
    {
        var runtime = new RecordingActorRuntime(ActorTellResult.Accepted);
        var dispatcher = new ClusterActorTellDispatcher<TestActor>(
            runtime,
            static (actor, envelope, _) =>
            {
                actor.HandledActorIds.Add(envelope.ActorId);
                actor.HandledKinds.Add(envelope.Kind);
                return ValueTask.CompletedTask;
            });
        var message = CreateActorMessage("room/42");

        var status = await dispatcher.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Equal(ActorId.From("room/42"), runtime.LastActorId);
        Assert.Equal("room/42", Assert.Single(runtime.Actor.HandledActorIds));
        Assert.Equal("join", Assert.Single(runtime.Actor.HandledKinds));
    }

    [Fact]
    public async Task MailboxFullMapsToClusterBackpressure()
    {
        var runtime = new RecordingActorRuntime(ActorTellResult.MailboxFull);
        var dispatcher = new ClusterActorTellDispatcher<TestActor>(
            runtime,
            static (_, _, _) => ValueTask.CompletedTask);

        var status = await dispatcher.HandleAsync(
            CreateActorMessage("room/42"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Backpressure, status);
        Assert.Equal(ActorId.From("room/42"), runtime.LastActorId);
        Assert.Empty(runtime.Actor.HandledActorIds);
    }

    [Fact]
    public async Task ActorUnavailableMapsToHandlerUnavailable()
    {
        var runtime = new RecordingActorRuntime(ActorTellResult.ActorUnavailable);
        var dispatcher = new ClusterActorTellDispatcher<TestActor>(
            runtime,
            static (_, _, _) => ValueTask.CompletedTask);

        var status = await dispatcher.HandleAsync(
            CreateActorMessage("room/42"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.HandlerUnavailable, status);
        Assert.Equal(ActorId.From("room/42"), runtime.LastActorId);
    }

    [Fact]
    public async Task NonActorRouteIsRejectedWithoutActorRuntimeDispatch()
    {
        var runtime = new RecordingActorRuntime(ActorTellResult.Accepted);
        var dispatcher = new ClusterActorTellDispatcher<TestActor>(
            runtime,
            static (_, _, _) => ValueTask.CompletedTask);
        var message = new ClusterMessage(
            "room/42",
            "join",
            Array.Empty<byte>(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            "node-a");

        var status = await dispatcher.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.RouteNotFound, status);
        Assert.Equal(0, runtime.DispatchCount);
    }

    private static ClusterMessage CreateActorMessage(string actorId)
    {
        return new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor(actorId),
            actorId,
            "join",
            new byte[] { 1 },
            DateTimeOffset.UtcNow.AddMinutes(1),
            "node-a").ToClusterMessage();
    }

    private sealed class TestActor : IActor
    {
        public List<string> HandledActorIds { get; } = new();

        public List<string> HandledKinds { get; } = new();
    }

    private sealed class RecordingActorRuntime : IActorRuntime
    {
        private readonly ActorTellResult _result;

        public RecordingActorRuntime(ActorTellResult result)
        {
            _result = result;
        }

        public TestActor Actor { get; } = new();

        public ActorId? LastActorId { get; private set; }

        public int DispatchCount { get; private set; }

        public ValueTask<TActor> GetOrCreateAsync<TActor>(
            ActorId id,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new NotSupportedException();
        }

        public ValueTask TellAsync<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new NotSupportedException();
        }

        public ActorTellResult TryTell<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            LastActorId = id;
            DispatchCount++;

            if (_result == ActorTellResult.Accepted)
            {
                var actor = Assert.IsAssignableFrom<TActor>(Actor);
                message(actor, cancellationToken).AsTask().GetAwaiter().GetResult();
            }

            return _result;
        }

        public ValueTask<TResult> AskAsync<TActor, TResult>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask<TResult>> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new NotSupportedException();
        }

        public IAsyncDisposable RegisterTimer<TActor>(
            ActorId id,
            TimeSpan dueTime,
            TimeSpan? period,
            Func<TActor, CancellationToken, ValueTask> callback)
            where TActor : class, IActor
        {
            throw new NotSupportedException();
        }

        public bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics)
        {
            throw new NotSupportedException();
        }

        public ActorState GetState(ActorId id)
        {
            throw new NotSupportedException();
        }

        public ValueTask StopAsync(ActorId id)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ActorStopOutcome> StopAsync(ActorId id, TimeSpan drainTimeout)
        {
            throw new NotSupportedException();
        }
    }
}
