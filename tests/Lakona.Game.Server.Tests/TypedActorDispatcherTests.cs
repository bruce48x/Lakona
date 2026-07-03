using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Serializer.MemoryPack;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed partial class TypedActorDispatcherTests
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
    public async Task Typed_actor_handler_round_trips_memorypack_actor_payloads()
    {
        var runtime = new RecordingActorRuntime();
        var serializer = new RpcRemoteActorSerializer(new MemoryPackRpcSerializer());
        var router = new RecordingClusterRouter();
        var handler = new RoomActorClusterHandler(runtime, serializer, router);
        var request = new MemoryPackJoinRoomRequest { PlayerId = "player-2" };
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("room/43"),
            "room/43",
            "join-memorypack",
            serializer.Serialize(request),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            correlationId: "corr-2",
            replyCorrelationId: "reply-2").ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Equal(ActorId.From("room/43"), runtime.LastActorId);
        Assert.Equal("player-2", runtime.Actor.LastPlayerId);
        Assert.NotNull(router.LastMessage);
        Assert.Equal(RemoteActorGateway.ReplyKind, router.LastMessage.Kind);
        Assert.Equal("reply-2", router.LastMessage.CorrelationId);
        var reply = serializer.Deserialize<MemoryPackJoinRoomReply>(router.LastMessage.Payload);
        Assert.True(reply.Accepted);
    }

    [Fact]
    public void RpcRemoteActorSerializer_round_trips_type_based_payloads()
    {
        var serializer = new RpcRemoteActorSerializer(new JsonRpcSerializer());
        var request = new JoinRoomRequest("player-type");

        var payload = serializer.Serialize(request, typeof(JoinRoomRequest));
        var decoded = Assert.IsType<JoinRoomRequest>(
            serializer.Deserialize(payload, typeof(JoinRoomRequest)));

        Assert.Equal("player-type", decoded.PlayerId);
    }

    [Fact]
    public async Task Typed_actor_handler_uses_cluster_backed_json_remote_actor_serializer()
    {
        using var provider = CreateClusterProvider("json", new MemoryPackRpcSerializer());
        var runtime = new RecordingActorRuntime();
        var serializer = provider.GetRequiredService<IRemoteActorSerializer>();
        var router = new RecordingClusterRouter();
        var handler = new RoomActorClusterHandler(runtime, serializer, router);
        var request = new JoinRoomRequest("player-3");
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("room/44"),
            "room/44",
            "join",
            serializer.Serialize(request),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            correlationId: "corr-3",
            replyCorrelationId: "reply-3").ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Equal("player-3", runtime.Actor.LastPlayerId);
        Assert.NotNull(router.LastMessage);
        var reply = new JsonRpcSerializer().Deserialize<JoinRoomReply>(router.LastMessage.Payload);
        Assert.True(reply.Accepted);
    }

    [Fact]
    public async Task Typed_actor_handler_uses_cluster_backed_memorypack_remote_actor_serializer()
    {
        using var provider = CreateClusterProvider("memorypack", new JsonRpcSerializer());
        var runtime = new RecordingActorRuntime();
        var serializer = provider.GetRequiredService<IRemoteActorSerializer>();
        var router = new RecordingClusterRouter();
        var handler = new RoomActorClusterHandler(runtime, serializer, router);
        var request = new MemoryPackJoinRoomRequest { PlayerId = "player-4" };
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("room/45"),
            "room/45",
            "join-memorypack",
            serializer.Serialize(request),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            correlationId: "corr-4",
            replyCorrelationId: "reply-4").ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Equal("player-4", runtime.Actor.LastPlayerId);
        Assert.NotNull(router.LastMessage);
        var reply = new MemoryPackRpcSerializer().Deserialize<MemoryPackJoinRoomReply>(router.LastMessage.Payload);
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

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class MemoryPackJoinRoomRequest
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class MemoryPackJoinRoomReply
    {
        [MemoryPackOrder(0)]
        public bool Accepted { get; set; }
    }

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

        public ValueTask<MemoryPackJoinRoomReply> JoinMemoryPackAsync(
            MemoryPackJoinRoomRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPlayerId = request.PlayerId;
            return ValueTask.FromResult(new MemoryPackJoinRoomReply { Accepted = true });
        }
    }

    private readonly record struct TypedDispatcherRoomId(string Value);

    private static ServiceProvider CreateClusterProvider(string clusterSerializer, IRpcSerializer laterSerializer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Serializer = clusterSerializer
            }
        });
        services.AddLakonaGameClusterEndpoint();
        services.AddSingleton(laterSerializer);
        return services.BuildServiceProvider();
    }

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

                case "join-memorypack":
                {
                    var request = _serializer.Deserialize<MemoryPackJoinRoomRequest>(envelope.Payload);
                    var reply = await _runtime.AskAsync<RoomActor, MemoryPackJoinRoomReply>(
                        actorId,
                        (actor, ct) => actor.JoinMemoryPackAsync(request, ct),
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

        public ReadOnlyMemory<byte> Serialize(object? value, Type type)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, type);
        }

        public object? Deserialize(ReadOnlyMemory<byte> payload, Type type)
        {
            return JsonSerializer.Deserialize(payload.Span, type);
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

        public ValueTask TellAsync(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ActorTellResult TryTell(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
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

        public bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics)
        {
            throw new NotSupportedException();
        }

        public IReadOnlyList<ActorId> GetActiveActorIds(Type actorType)
        {
            throw new NotSupportedException();
        }

        public ActorState GetState(ActorId id)
        {
            throw new NotSupportedException();
        }

    }
}
