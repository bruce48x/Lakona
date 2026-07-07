using System.Reflection;
using System.Globalization;
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests;

public sealed class HotfixActorClusterHandlerTests
{
    private const string PingMethodKey =
        "actor:Lakona.Game.Server.Tests.HotfixActorClusterHandlerTests+TestActor, Lakona.Game.Server.Tests|method:PingAsync|request:Lakona.Game.Server.Tests.HotfixActorClusterHandlerTests+PingRequest, Lakona.Game.Server.Tests|result:Lakona.Game.Server.Tests.HotfixActorClusterHandlerTests+PingReply, Lakona.Game.Server.Tests";

    private const string NotifyMethodKey =
        "actor:Lakona.Game.Server.Tests.HotfixActorClusterHandlerTests+TestActor, Lakona.Game.Server.Tests|method:NotifyAsync|request:Lakona.Game.Server.Tests.HotfixActorClusterHandlerTests+NotifyRequest, Lakona.Game.Server.Tests|result:void";

    private const string ThrowMethodKey =
        "actor:Lakona.Game.Server.Tests.HotfixActorClusterHandlerTests+TestActor, Lakona.Game.Server.Tests|method:ThrowAsync|request:Lakona.Game.Server.Tests.HotfixActorClusterHandlerTests+PingRequest, Lakona.Game.Server.Tests|result:Lakona.Game.Server.Tests.HotfixActorClusterHandlerTests+PingReply, Lakona.Game.Server.Tests";

    private static readonly string PingMethodId = HotfixActorApiMetadata
        .CreateMethodId(PingMethodKey)
        .ToString(CultureInfo.InvariantCulture);

    private static readonly string NotifyMethodId = HotfixActorApiMetadata
        .CreateMethodId(NotifyMethodKey)
        .ToString(CultureInfo.InvariantCulture);

    private static readonly string ThrowMethodId = HotfixActorApiMetadata
        .CreateMethodId(ThrowMethodKey)
        .ToString(CultureInfo.InvariantCulture);

    [Fact]
    public async Task HandleAsync_dispatches_request_reply_hotfix_actor_method_by_metadata()
    {
        var serializer = new JsonRemoteActorSerializer();
        var runtime = new RecordingActorRuntime();
        var router = new RecordingClusterRouter();
        var handler = CreateHandler(runtime, serializer, router, CreateSnapshot(CreatePingDescriptor()));
        var request = new PingRequest("player-1");
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("user/1"),
            "user/1",
            HotfixActorApiMetadata.ActorMessageKind,
            serializer.Serialize(request, typeof(PingRequest)),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            correlationId: "corr-1",
            replyCorrelationId: "reply-1",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [HotfixActorApiMetadata.MethodIdKey] = PingMethodId
            }).ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, status);
        Assert.Equal(typeof(TestActor), runtime.LastActorType);
        Assert.Equal(ActorId.From("user/1"), runtime.LastActorId);
        Assert.Equal("player-1", runtime.Actor.LastPing);
        Assert.NotNull(router.LastMessage);
        Assert.Equal(RemoteActorGateway.ReplyKind, router.LastMessage.Kind);
        Assert.Equal("reply-1", router.LastMessage.CorrelationId);
        var reply = (PingReply)serializer.Deserialize(router.LastMessage.Payload, typeof(PingReply))!;
        Assert.Equal("pong:player-1", reply.Value);
    }

    [Fact]
    public async Task HandleAsync_returns_route_not_found_when_method_id_metadata_is_missing()
    {
        var serializer = new JsonRemoteActorSerializer();
        var handler = CreateHandler(
            new RecordingActorRuntime(),
            serializer,
            new RecordingClusterRouter(),
            CreateSnapshot(CreatePingDescriptor()));
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("user/1"),
            "user/1",
            HotfixActorApiMetadata.ActorMessageKind,
            serializer.Serialize(new PingRequest("player-1"), typeof(PingRequest)),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            correlationId: "corr-1",
            replyCorrelationId: "reply-1").ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.RouteNotFound, status);
    }

    [Fact]
    public async Task HandleAsync_maps_resultless_actor_not_found_to_route_not_found()
    {
        var serializer = new JsonRemoteActorSerializer();
        var runtime = new RecordingActorRuntime { TellResult = ActorTellResult.ActorNotFound };
        var handler = CreateHandler(
            runtime,
            serializer,
            new RecordingClusterRouter(),
            CreateSnapshot(CreateNotifyDescriptor()));
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("user/missing"),
            "user/missing",
            HotfixActorApiMetadata.ActorMessageKind,
            serializer.Serialize(new NotifyRequest("seen"), typeof(NotifyRequest)),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [HotfixActorApiMetadata.MethodIdKey] = NotifyMethodId
            }).ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.RouteNotFound, status);
    }

    [Fact]
    public async Task HandleAsync_resultless_tell_releases_hotfix_lease_when_caller_cancels_after_acceptance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serializer = new JsonRemoteActorSerializer();
        var actorId = ActorId.From("user/canceled-tell");
        var actorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseActor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retired = false;

        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        await hosting.CreateAsync<TestActor>(actorId, cancellationToken);

        var blockingTurn = runtime.TellAsync<TestActor>(
            actorId,
            (actor, ct) => actor.BlockAsync(actorEntered, releaseActor.Task, ct),
            cancellationToken).AsTask();
        await actorEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        var snapshot = CreateSnapshot(CreateNotifyDescriptor(), () => retired = true);
        var handler = CreateHandler(runtime, serializer, new RecordingClusterRouter(), snapshot);
        using var callerCancellation = new CancellationTokenSource();
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor(actorId.Value),
            actorId.Value,
            HotfixActorApiMetadata.ActorMessageKind,
            serializer.Serialize(new NotifyRequest("seen"), typeof(NotifyRequest)),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [HotfixActorApiMetadata.MethodIdKey] = NotifyMethodId
            }).ToClusterMessage();

        try
        {
            var status = await handler.HandleAsync(message, callerCancellation.Token);

            Assert.Equal(ClusterSendStatus.Accepted, status);

            await callerCancellation.CancelAsync();
            snapshot.Retire();
            Assert.False(retired);

            releaseActor.SetResult();
            await blockingTurn;

            var notification = await runtime.AskAsync<TestActor, string>(
                actorId,
                static (actor, _) => new ValueTask<string>(actor.LastNotification ?? string.Empty),
                cancellationToken);

            Assert.Equal("seen", notification);
            Assert.True(retired);
        }
        finally
        {
            releaseActor.TrySetResult();
            await blockingTurn.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    [Fact]
    public async Task HandleAsync_resultless_tell_releases_hotfix_lease_when_runtime_trytell_throws()
    {
        var serializer = new JsonRemoteActorSerializer();
        var retired = false;
        var runtime = new RecordingActorRuntime
        {
            TellException = new InvalidOperationException("runtime enqueue failed")
        };
        var snapshot = CreateSnapshot(CreateNotifyDescriptor(), () => retired = true);
        var handler = CreateHandler(
            runtime,
            serializer,
            new RecordingClusterRouter(),
            snapshot);
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("user/throw"),
            "user/throw",
            HotfixActorApiMetadata.ActorMessageKind,
            serializer.Serialize(new NotifyRequest("seen"), typeof(NotifyRequest)),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [HotfixActorApiMetadata.MethodIdKey] = NotifyMethodId
            }).ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        snapshot.Retire();

        Assert.Equal(ClusterSendStatus.Failed, status);
        Assert.True(retired);
        Assert.Null(runtime.Actor.LastNotification);
    }

    [Fact]
    public async Task HandleAsync_resultless_tell_observes_cancellation_before_enqueue_and_releases_hotfix_lease()
    {
        var serializer = new JsonRemoteActorSerializer();
        var retired = false;
        var runtime = new RecordingActorRuntime();
        var snapshot = CreateSnapshot(CreateNotifyDescriptor(), () => retired = true);
        var handler = CreateHandler(
            runtime,
            serializer,
            new RecordingClusterRouter(),
            snapshot);
        using var callerCancellation = new CancellationTokenSource();
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("user/canceled-before-enqueue"),
            "user/canceled-before-enqueue",
            HotfixActorApiMetadata.ActorMessageKind,
            serializer.Serialize(new NotifyRequest("seen"), typeof(NotifyRequest)),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [HotfixActorApiMetadata.MethodIdKey] = NotifyMethodId
            }).ToClusterMessage();

        await callerCancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler
            .HandleAsync(message, callerCancellation.Token)
            .AsTask());
        snapshot.Retire();

        Assert.True(retired);
        Assert.Null(runtime.LastActorType);
        Assert.Null(runtime.Actor.LastNotification);
    }

    [Fact]
    public async Task HandleAsync_returns_failed_when_hotfix_actor_method_throws()
    {
        var serializer = new JsonRemoteActorSerializer();
        var runtime = new RecordingActorRuntime();
        var router = new RecordingClusterRouter();
        var handler = CreateHandler(runtime, serializer, router, CreateSnapshot(CreateThrowDescriptor()));
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("user/1"),
            "user/1",
            HotfixActorApiMetadata.ActorMessageKind,
            serializer.Serialize(new PingRequest("player-1"), typeof(PingRequest)),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            correlationId: "corr-1",
            replyCorrelationId: "reply-1",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [HotfixActorApiMetadata.MethodIdKey] = ThrowMethodId
            }).ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Failed, status);
        Assert.Null(router.LastMessage);
    }

    [Fact]
    public async Task HandleAsync_returns_serialization_failed_when_reply_serialization_fails()
    {
        var serializer = new ThrowingReplySerializer();
        var router = new RecordingClusterRouter();
        var handler = CreateHandler(
            new RecordingActorRuntime(),
            serializer,
            router,
            CreateSnapshot(CreatePingDescriptor()));
        var message = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor("user/1"),
            "user/1",
            HotfixActorApiMetadata.ActorMessageKind,
            serializer.Serialize(new PingRequest("player-1"), typeof(PingRequest)),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("node-a"),
            correlationId: "corr-1",
            replyCorrelationId: "reply-1",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [HotfixActorApiMetadata.MethodIdKey] = PingMethodId
            }).ToClusterMessage();

        var status = await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.SerializationFailed, status);
        Assert.Null(router.LastMessage);
    }

    private static HotfixActorClusterHandler CreateHandler(
        IActorRuntime runtime,
        IRemoteActorSerializer serializer,
        IClusterRouter router,
        HotfixRuntimeSnapshot snapshot)
    {
        var services = new ServiceCollection()
            .AddSingleton<IHotfixRuntimeAccessor>(new FixedRuntimeAccessor(snapshot))
            .BuildServiceProvider();
        return new HotfixActorClusterHandler(runtime, serializer, router, services);
    }

    private static HotfixRuntimeSnapshot CreateSnapshot(
        HotfixActorMethodDescriptor descriptor,
        Action? onRetired = null)
    {
        var table = new HotfixDispatchTable(
            1,
            Array.Empty<HotfixMethodBinding>(),
            Array.Empty<HotfixServiceMethodBinding>(),
            [descriptor]);
        var services = new ServiceCollection().BuildServiceProvider();
        return new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
            EmptyHotfixFeatureCommandInvoker.Instance,
            services,
            table,
            services,
            mainAssembly: null,
            loadContext: null,
            sourceVersion: "test",
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired);
    }

    private static HotfixActorMethodDescriptor CreatePingDescriptor()
    {
        return new HotfixActorMethodDescriptor(
            PingMethodKey,
            typeof(TestActor),
            nameof(PingAsync),
            typeof(PingRequest),
            typeof(PingReply),
            typeof(HotfixActorClusterHandlerTests).GetMethod(nameof(PingAsync), BindingFlags.Public | BindingFlags.Static)!,
            hasCancellationToken: true);
    }

    private static HotfixActorMethodDescriptor CreateNotifyDescriptor()
    {
        return new HotfixActorMethodDescriptor(
            NotifyMethodKey,
            typeof(TestActor),
            nameof(NotifyAsync),
            typeof(NotifyRequest),
            null,
            typeof(HotfixActorClusterHandlerTests).GetMethod(nameof(NotifyAsync), BindingFlags.Public | BindingFlags.Static)!,
            hasCancellationToken: true);
    }

    private static HotfixActorMethodDescriptor CreateThrowDescriptor()
    {
        return new HotfixActorMethodDescriptor(
            ThrowMethodKey,
            typeof(TestActor),
            nameof(ThrowAsync),
            typeof(PingRequest),
            typeof(PingReply),
            typeof(HotfixActorClusterHandlerTests).GetMethod(nameof(ThrowAsync), BindingFlags.Public | BindingFlags.Static)!,
            hasCancellationToken: true);
    }

    public static ValueTask<PingReply> PingAsync(
        TestActor actor,
        PingRequest request,
        CancellationToken cancellationToken)
    {
        return actor.PingAsync(request, cancellationToken);
    }

    public static ValueTask NotifyAsync(
        TestActor actor,
        NotifyRequest request,
        CancellationToken cancellationToken)
    {
        return actor.NotifyAsync(request, cancellationToken);
    }

    public static ValueTask<PingReply> ThrowAsync(
        TestActor actor,
        PingRequest request,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("hotfix actor method failed");
    }

    public sealed record PingRequest(string Value);

    public sealed record PingReply(string Value);

    public sealed record NotifyRequest(string Value);

    public sealed class TestActor : GameActor
    {
        public string? LastPing { get; private set; }

        public string? LastNotification { get; private set; }

        public ValueTask<PingReply> PingAsync(PingRequest request, CancellationToken cancellationToken)
        {
            LastPing = request.Value;
            return new ValueTask<PingReply>(new PingReply($"pong:{request.Value}"));
        }

        public ValueTask NotifyAsync(NotifyRequest request, CancellationToken cancellationToken)
        {
            LastNotification = request.Value;
            return default;
        }

        public async ValueTask BlockAsync(
            TaskCompletionSource entered,
            Task release,
            CancellationToken cancellationToken)
        {
            entered.SetResult();
            await release.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class RecordingActorRuntime : IActorRuntime
    {
        public TestActor Actor { get; } = new();

        public Type? LastActorType { get; private set; }

        public ActorId? LastActorId { get; private set; }

        public ActorTellResult TellResult { get; init; } = ActorTellResult.Accepted;

        public Exception? TellException { get; init; }

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
            LastActorType = actorType;
            LastActorId = id;
            if (TellException is not null)
            {
                throw TellException;
            }

            if (TellResult != ActorTellResult.Accepted)
            {
                return TellResult;
            }

            message(Actor, cancellationToken).AsTask().GetAwaiter().GetResult();
            return ActorTellResult.Accepted;
        }

        public ValueTask<TResult> AskAsync<TActor, TResult>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask<TResult>> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new NotSupportedException();
        }

        public async ValueTask<object?> AskAsync(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask<object?>> message,
            CancellationToken cancellationToken = default)
        {
            LastActorType = actorType;
            LastActorId = id;
            return await message(Actor, cancellationToken).ConfigureAwait(false);
        }

        public ActorRuntimeDiagnosticsSnapshot GetDiagnosticsSnapshot()
        {
            return new ActorRuntimeDiagnosticsSnapshot([]);
        }

        public IReadOnlyList<ActorId> GetActiveActorIds(Type actorType)
        {
            return [];
        }

        public bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics)
        {
            metrics = default;
            return false;
        }

        public ActorState GetState(ActorId id)
        {
            return ActorState.Active;
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

    private sealed class ThrowingReplySerializer : IRemoteActorSerializer
    {
        private readonly JsonRemoteActorSerializer _inner = new();

        public ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            return _inner.Serialize(value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return _inner.Deserialize<T>(payload);
        }

        public ReadOnlyMemory<byte> Serialize(object? value, Type type)
        {
            if (type != typeof(PingReply))
            {
                return _inner.Serialize(value, type);
            }

            throw new InvalidOperationException("reply serialization failed");
        }

        public object? Deserialize(ReadOnlyMemory<byte> payload, Type type)
        {
            return _inner.Deserialize(payload, type);
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
            return new ValueTask<ClusterSendStatus>(ClusterSendStatus.Accepted);
        }
    }

    private sealed class FixedRuntimeAccessor(HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current { get; } = snapshot;
    }
}
