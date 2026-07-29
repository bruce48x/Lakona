using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Serializer.MemoryPack;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed partial class TypedActorDispatcherTests
{
    [Fact]
    public async Task Typed_actor_handler_dispatches_join_and_sends_reply()
    {
        var runtime = new RecordingActorRuntime();
        var serializer = new JsonRemoteActorSerializer();
        var router = new RecordingClusterNodeSender();
        var handler = new RoomActorClusterHandler(
            runtime,
            serializer,
            router,
            new LocalActorNodeIdentity("local"));
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
        Assert.Equal(new NodeId("node-a"), router.LastDestination);
        Assert.Equal(ClusterActorRouteKeys.ForReply("node-a"), router.LastRoute);
        Assert.NotNull(router.LastMessage);
        Assert.Equal(RemoteActorGateway.ReplyKind, router.LastMessage.Kind);
        Assert.Equal(new NodeId("local"), router.LastMessage.SourceNode);
        Assert.Equal("reply-1", router.LastMessage.CorrelationId);
        var reply = serializer.Deserialize<JoinRoomReply>(router.LastMessage.Payload);
        Assert.True(reply.Accepted);
    }

    [Fact]
    public async Task Typed_actor_handler_returns_reply_delivery_failure_after_actor_execution()
    {
        var runtime = new RecordingActorRuntime();
        var serializer = new JsonRemoteActorSerializer();
        var sender = new RecordingClusterNodeSender { Status = ClusterSendStatus.Failed };
        var handler = new RoomActorClusterHandler(
            runtime,
            serializer,
            sender,
            new LocalActorNodeIdentity("local"));
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("room/failed"),
            "room/failed",
            "join",
            serializer.Serialize(new JoinRoomRequest("player-failed")),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            replyCorrelationId: "reply-failed").ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Failed, status);
        Assert.Equal("player-failed", runtime.Actor.LastPlayerId);
    }

    [Fact]
    public async Task Typed_actor_handler_round_trips_memorypack_actor_payloads()
    {
        var runtime = new RecordingActorRuntime();
        var serializer = new MemoryPackRemoteActorSerializer();
        var router = new RecordingClusterNodeSender();
        var handler = new RoomActorClusterHandler(
            runtime,
            serializer,
            router,
            new LocalActorNodeIdentity("local"));
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
    public async Task Typed_actor_handler_uses_fixed_cluster_memorypack_when_an_endpoint_serializer_is_registered_later()
    {
        using var provider = CreateClusterProvider(new JsonRpcSerializer());
        var runtime = new RecordingActorRuntime();
        var serializer = new MemoryPackRemoteActorSerializer();
        var router = new RecordingClusterNodeSender();
        var handler = new RoomActorClusterHandler(
            runtime,
            serializer,
            router,
            new LocalActorNodeIdentity("local"));
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
            new RecordingClusterNodeSender(),
            new LocalActorNodeIdentity("local"));
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

    private static ServiceProvider CreateClusterProvider(IRpcSerializer laterSerializer)
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001"
            }
        });
        services.AddLakonaGameClusterEndpoint();
        services.AddSingleton(laterSerializer);
        return services.BuildServiceProvider();
    }

    private sealed class RoomActorClusterHandler : IClusterMessageHandler
    {
        private readonly IActorRuntime _runtime;
        private readonly ITestActorSerializer _serializer;
        private readonly IClusterNodeSender _nodeSender;
        private readonly LocalActorNodeIdentity _localNode;

        public RoomActorClusterHandler(
            IActorRuntime runtime,
            ITestActorSerializer serializer,
            IClusterNodeSender nodeSender,
            LocalActorNodeIdentity localNode)
        {
            _runtime = runtime;
            _serializer = serializer;
            _nodeSender = nodeSender;
            _localNode = localNode;
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
                        return await RemoteActorGateway.SendReplyAsync(
                            _nodeSender,
                            _localNode.NodeId,
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
                        return await RemoteActorGateway.SendReplyAsync(
                            _nodeSender,
                            _localNode.NodeId,
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

    private interface ITestActorSerializer
    {
        ReadOnlyMemory<byte> Serialize<T>(T value);

        T Deserialize<T>(ReadOnlyMemory<byte> payload);
    }

    private sealed class JsonRemoteActorSerializer : ITestActorSerializer
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

    private sealed class MemoryPackRemoteActorSerializer : ITestActorSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            return MemoryPackSerializer.Serialize(value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return MemoryPackSerializer.Deserialize<T>(payload.Span)!;
        }
    }

    private sealed class RecordingClusterNodeSender : IClusterNodeSender
    {
        public NodeId? LastDestination { get; private set; }

        public RouteKey LastRoute { get; private set; }

        public ClusterMessage? LastMessage { get; private set; }

        public ClusterSendStatus Status { get; init; } = ClusterSendStatus.Accepted;

        public ValueTask<ClusterSendStatus> SendAsync(
            NodeId nodeId,
            long? expectedNodeEpoch,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            LastDestination = nodeId;
            LastRoute = route;
            LastMessage = message;
            return ValueTask.FromResult(Status);
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
