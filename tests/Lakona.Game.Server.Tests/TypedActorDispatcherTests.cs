using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class TypedActorDispatcherTests
{
    [Fact]
    public async Task Typed_actor_handler_dispatches_join_and_sends_reply()
    {
        var runtime = new RecordingActorRuntime();
        var serializer = new JsonRemoteActorSerializer();
        var router = new RecordingClusterRouter();
        var handler = new RoomActorClusterHandler(runtime, serializer, router);
        var request = new JoinRoomRequest("player-1");
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("room/42"),
            "room/42",
            "join",
            serializer.Serialize(request),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            correlationId: "corr-1",
            replyCorrelationId: "reply-1").ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Equal(ActorId.From("room/42"), runtime.LastActorId);
        Assert.Equal("player-1", runtime.Actor.LastPlayerId);
        Assert.NotNull(router.LastMessage);
        Assert.Equal(RemoteActorGateway.ReplyKind, router.LastMessage.Kind);
        Assert.Equal("reply-1", router.LastMessage.CorrelationId);
        var reply = serializer.Deserialize<JoinRoomReply>(router.LastMessage.Payload);
        Assert.True(reply.Accepted);
    }

    [Fact]
    public async Task Typed_actor_handler_rejects_unknown_method()
    {
        var handler = new RoomActorClusterHandler(
            new RecordingActorRuntime(),
            new JsonRemoteActorSerializer(),
            new RecordingClusterRouter());
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("room/42"),
            "room/42",
            "leave",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a")).ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.RouteNotFound, status);
    }

    private sealed record JoinRoomRequest(string PlayerId);

    private sealed record JoinRoomReply(bool Accepted);

    private sealed class RoomActor : Actor<TypedDispatcherRoomId>
    {
        public string? LastPlayerId { get; private set; }

        public ValueTask<JoinRoomReply> JoinAsync(
            JoinRoomRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPlayerId = request.PlayerId;
            return ValueTask.FromResult(new JoinRoomReply(true));
        }
    }

    private readonly record struct TypedDispatcherRoomId(string Value);

    private sealed class RoomActorClusterHandler : IClusterMessageHandler
    {
        private readonly IActorRuntime _runtime;
        private readonly IRemoteActorSerializer _serializer;
        private readonly IClusterRouter _router;

        public RoomActorClusterHandler(
            IActorRuntime runtime,
            IRemoteActorSerializer serializer,
            IClusterRouter router)
        {
            _runtime = runtime;
            _serializer = serializer;
            _router = router;
        }

        public async ValueTask<ClusterSendStatus> HandleAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            if (!ClusterActorEnvelope.TryFromClusterMessage(message, out var envelope) || envelope is null)
            {
                return ClusterSendStatus.RouteNotFound;
            }

            if (!envelope.ActorId.StartsWith("room/", StringComparison.Ordinal))
            {
                return ClusterSendStatus.RouteNotFound;
            }

            var actorId = ActorId.From(envelope.ActorId);
            switch (envelope.Kind)
            {
                case "join":
                {
                    var request = _serializer.Deserialize<JoinRoomRequest>(envelope.Payload);
                    var reply = await _runtime.AskAsync<RoomActor, JoinRoomReply>(
                        actorId,
                        (actor, ct) => actor.JoinAsync(request, ct),
                        cancellationToken).ConfigureAwait(false);
                    if (envelope.ReplyCorrelationId is not null)
                    {
                        await RemoteActorGateway.SendReplyAsync(
                            _router,
                            envelope.SourceNode,
                            envelope.ReplyCorrelationId,
                            _serializer.Serialize(reply),
                            cancellationToken).ConfigureAwait(false);
                    }

                    return ClusterSendStatus.Accepted;
                }

                default:
                    return ClusterSendStatus.RouteNotFound;
            }
        }
    }

    private sealed class JsonRemoteActorSerializer : IRemoteActorSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return JsonSerializer.Deserialize<T>(payload.Span)!;
        }
    }

    private sealed class RecordingClusterRouter : IClusterRouter
    {
        public ClusterMessage? LastMessage { get; private set; }

        public ValueTask<ClusterSendStatus> SendAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            LastMessage = message;
            return ValueTask.FromResult(ClusterSendStatus.Accepted);
        }
    }

    private sealed class RecordingActorRuntime : IActorRuntime
    {
        public RoomActor Actor { get; } = new();

        public ActorId? LastActorId { get; private set; }

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
            throw new NotSupportedException();
        }

        public async ValueTask<TResult> AskAsync<TActor, TResult>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask<TResult>> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            LastActorId = id;
            var actor = Assert.IsAssignableFrom<TActor>(Actor);
            return await message(actor, cancellationToken).ConfigureAwait(false);
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
