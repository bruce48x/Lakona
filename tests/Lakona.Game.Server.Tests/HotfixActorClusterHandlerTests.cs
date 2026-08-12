using System.Reflection;
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Rpc.Core;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests;

public sealed partial class HotfixActorClusterHandlerTests
{
    private const string PingMethodKey =
        "actor:test|method:PingAsync|request:PingRequest|result:PingReply";
    private const string NotifyMethodKey =
        "actor:test|method:NotifyAsync|request:NotifyRequest|result:void";
    private const string ThrowMethodKey =
        "actor:test|method:ThrowAsync|request:PingRequest|result:PingReply";

    [Fact]
    public async Task Actor_rpc_ask_dispatches_typed_request_and_returns_typed_reply()
    {
        var runtime = new RecordingActorRuntime();
        var descriptor = CreatePingDescriptor();
        await using var fixture = CreateFixture(runtime, CreateSnapshot(descriptor));
        var invocation = CreateInvocation<PingRequest, PingReply>(
            descriptor.MethodId,
            new PingRequest { Value = "player-1" });

        using var response = await InvokeAsync(
            fixture.Handler,
            invocation,
            tell: false,
            TestContext.Current.CancellationToken);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.Replied, reply.Status);
        Assert.Equal(typeof(TestActor), runtime.LastActorType);
        Assert.Equal(ActorId.From("user/1"), runtime.LastActorId);
        Assert.Equal("player-1", runtime.Actor.LastPing);
        var result = Assert.IsType<PingReply>(invocation.DeserializeReply(reply.Body));
        Assert.Equal("pong:player-1", result.Value);
    }

    [Fact]
    public async Task Actor_rpc_uses_the_published_typed_codec_without_runtime_type_reflection()
    {
        var descriptor = CreatePingDescriptor();
        await using var fixture = CreateFixture(
            new RecordingActorRuntime(),
            CreateSnapshot(descriptor));
        var invocation = CreateInvocation<PingRequest, PingReply>(
            descriptor.MethodId,
            new PingRequest { Value = "typed" });

        using var request = ClusterActorWireCodec.EncodeRequest(invocation, CreateLocation());
        var decoded = ClusterActorWireCodec.DecodeRequest(request.Memory);
        var requestValue = Assert.IsType<PingRequest>(
            descriptor.Codec.DeserializeRequest(decoded.Body));
        using var response = await fixture.Handler.HandleActorRpcAsync(
            request.Memory,
            tell: false,
            TestContext.Current.CancellationToken);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal("typed", requestValue.Value);
        Assert.Equal(RemoteActorStatus.Replied, reply.Status);
        Assert.IsType<PingReply>(invocation.DeserializeReply(reply.Body));
    }

    [Fact]
    public async Task Actor_rpc_rejects_malformed_payload_before_dispatch()
    {
        var runtime = new RecordingActorRuntime();
        await using var fixture = CreateFixture(runtime, CreateSnapshot(CreatePingDescriptor()));

        using var response = await fixture.Handler.HandleActorRpcAsync(
            new byte[] { 0xFF, 0x00, 0x01 },
            tell: false,
            TestContext.Current.CancellationToken);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.DeserializationFailed, reply.Status);
        Assert.Equal(RemoteActorRetrySafety.DefinitelyNotExecuted, reply.RetrySafety);
        Assert.Null(runtime.LastActorType);
    }

    [Fact]
    public async Task Actor_rpc_rejects_stale_exact_activation_before_mailbox_dispatch()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("40000000-0000-0000-0000-000000000000"));
        var local = new NodeReference(
            cluster,
            new NodeId("local"),
            new NodeIncarnationId(Guid.Parse("40000001-0000-0000-0000-000000000000")));
        var membership = new StubMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            [new ClusterMember(
                local,
                ClusterMemberState.Ready,
                new NodeEndpoint("tcp://127.0.0.1:24001"),
                isVoter: true)]));
        var directory = new TestActorDirectory();
        var actorId = ActorId.From("user/1");
        var activation = await directory.AcquireAsync(
            actorId,
            local,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);
        var runtime = new RecordingActorRuntime();
        var services = new ServiceCollection()
            .AddSingleton<IHotfixRuntimeAccessor>(
                new FixedRuntimeAccessor(CreateSnapshot(CreatePingDescriptor())))
            .AddSingleton<IClusterMembership>(membership)
            .AddSingleton<IActorDirectory>(directory)
            .BuildServiceProvider();
        await using var fixture = new HandlerFixture(
            new HotfixActorClusterHandler(
                runtime,
                new LocalActorNodeIdentity("local"),
                services),
            services);
        var invocation = RemoteActorInvocation.Create<PingRequest, PingReply>(
            local.Node,
            actorId,
            "test",
            "Ping",
            CreatePingDescriptor().MethodId,
            new PingRequest { Value = "stale" },
            DateTimeOffset.UtcNow.AddMinutes(1),
            ownerReference: local,
            activationId: ActorActivationId.New(),
            activationVersion: activation.Record.Version);
        var location = new RouteLocation(
            ClusterActorRouteKeys.ForActor(actorId.Value),
            local,
            new MembershipViewId(1),
            new NodeEndpoint("tcp://127.0.0.1:24001"));

        using var response = await InvokeAsync(
            fixture.Handler,
            invocation,
            tell: false,
            TestContext.Current.CancellationToken,
            location);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.NodeUnavailable, reply.Status);
        Assert.Equal(RemoteActorRetrySafety.DefinitelyNotExecuted, reply.RetrySafety);
        Assert.Null(runtime.LastActorType);
    }

    [Fact]
    public async Task Actor_rpc_accepts_an_exact_activation_after_an_unrelated_membership_commit()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("41000000-0000-0000-0000-000000000000"));
        var local = new NodeReference(cluster, new NodeId("local"),
            new NodeIncarnationId(Guid.Parse("41000001-0000-0000-0000-000000000000")));
        var other = new NodeReference(cluster, new NodeId("other"),
            new NodeIncarnationId(Guid.Parse("41000002-0000-0000-0000-000000000000")));
        var membership = new StubMembership(new ClusterMembershipSnapshot(cluster, new MembershipViewId(2),
        [
            new ClusterMember(local, ClusterMemberState.Ready, new NodeEndpoint("tcp://127.0.0.1:24001"), true),
            new ClusterMember(other, ClusterMemberState.Ready, new NodeEndpoint("tcp://127.0.0.1:24002"), true)
        ]));
        var directory = new TestActorDirectory();
        var actorId = ActorId.From("user/1");
        var activation = await directory.AcquireAsync(actorId, local, ActorActivationId.New(), TestContext.Current.CancellationToken);
        var runtime = new RecordingActorRuntime();
        var services = new ServiceCollection()
            .AddSingleton<IHotfixRuntimeAccessor>(new FixedRuntimeAccessor(CreateSnapshot(CreatePingDescriptor())))
            .AddSingleton<IClusterMembership>(membership)
            .AddSingleton<IActorDirectory>(directory)
            .BuildServiceProvider();
        await using var fixture = new HandlerFixture(new HotfixActorClusterHandler(runtime, new LocalActorNodeIdentity("local"), services), services);
        var invocation = RemoteActorInvocation.Create<PingRequest, PingReply>(
            local.Node, actorId, "test", "Ping", CreatePingDescriptor().MethodId,
            new PingRequest { Value = "after-commit" }, DateTimeOffset.UtcNow.AddMinutes(1),
            ownerReference: local, activationId: activation.Record.ActivationId,
            activationVersion: activation.Record.Version);
        var staleViewLocation = new RouteLocation(ClusterActorRouteKeys.ForActor(actorId.Value), local,
            new MembershipViewId(1), new NodeEndpoint("tcp://127.0.0.1:24001"));

        using var response = await InvokeAsync(fixture.Handler, invocation, false,
            TestContext.Current.CancellationToken, staleViewLocation);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.Replied, reply.Status);
        Assert.Equal("after-commit", runtime.Actor.LastPing);
    }

    [Fact]
    public async Task Actor_rpc_reenters_hotfix_scope_inside_actor_mailbox()
    {
        var runtime = new RecordingActorRuntime { SuppressExecutionContextFlow = true };
        var descriptor = CreatePingDescriptor();
        await using var fixture = CreateFixture(runtime, CreateSnapshot(descriptor));
        var invocation = CreateInvocation<PingRequest, PingReply>(
            descriptor.MethodId,
            new PingRequest { Value = "require-scope" });

        using var response = await InvokeAsync(
            fixture.Handler,
            invocation,
            tell: false,
            TestContext.Current.CancellationToken);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.Replied, reply.Status);
        Assert.Equal("require-scope", runtime.Actor.LastPing);
    }

    [Theory]
    [InlineData(ActorTellResult.ActorNotFound, RemoteActorStatus.RouteNotFound)]
    [InlineData(ActorTellResult.MailboxFull, RemoteActorStatus.Backpressure)]
    [InlineData(ActorTellResult.ActorUnavailable, RemoteActorStatus.HandlerUnavailable)]
    public async Task Actor_rpc_tell_maps_pre_dispatch_rejection(
        ActorTellResult tellResult,
        RemoteActorStatus expectedStatus)
    {
        var runtime = new RecordingActorRuntime { TellResult = tellResult };
        var descriptor = CreateNotifyDescriptor();
        await using var fixture = CreateFixture(runtime, CreateSnapshot(descriptor));
        var invocation = CreateInvocation(
            descriptor.MethodId,
            new NotifyRequest { Value = "notice" });

        using var response = await InvokeAsync(
            fixture.Handler,
            invocation,
            tell: true,
            TestContext.Current.CancellationToken);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(expectedStatus, reply.Status);
        Assert.Equal(RemoteActorRetrySafety.Indeterminate, reply.RetrySafety);
        Assert.Null(runtime.Actor.LastNotification);
    }

    [Fact]
    public async Task Actor_rpc_tell_retains_snapshot_lease_until_mailbox_work_completes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new RecordingActorRuntime
        {
            TellEntered = entered,
            TellRelease = release.Task
        };
        var descriptor = CreateNotifyDescriptor();
        var snapshot = CreateSnapshot(descriptor, () => retired.TrySetResult());
        await using var fixture = CreateFixture(runtime, snapshot);
        var invocation = CreateInvocation(
            descriptor.MethodId,
            new NotifyRequest { Value = "notice" });

        using var response = await InvokeAsync(
            fixture.Handler,
            invocation,
            tell: true,
            TestContext.Current.CancellationToken);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var retirement = snapshot.RetireAsync().AsTask();

        Assert.Equal(RemoteActorStatus.Accepted, reply.Status);
        Assert.False(retirement.IsCompleted);

        release.TrySetResult();
        await retirement.WaitAsync(TestContext.Current.CancellationToken);
        await retired.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("notice", runtime.Actor.LastNotification);
    }

    [Fact]
    public async Task Actor_rpc_maps_behavior_failure_without_exposing_a_partial_reply()
    {
        var descriptor = CreateThrowDescriptor();
        await using var fixture = CreateFixture(
            new RecordingActorRuntime(),
            CreateSnapshot(descriptor));
        var invocation = CreateInvocation<PingRequest, PingReply>(
            descriptor.MethodId,
            new PingRequest { Value = "boom" });

        using var response = await InvokeAsync(
            fixture.Handler,
            invocation,
            tell: false,
            TestContext.Current.CancellationToken);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.NodeUnavailable, reply.Status);
        Assert.True(reply.Body.IsEmpty);
    }

    private static HandlerFixture CreateFixture(
        IActorRuntime runtime,
        HotfixRuntimeSnapshot snapshot)
    {
        var services = new ServiceCollection()
            .AddSingleton<IHotfixRuntimeAccessor>(new FixedRuntimeAccessor(snapshot))
            .BuildServiceProvider();
        return new HandlerFixture(
            new HotfixActorClusterHandler(
                runtime,
                new LocalActorNodeIdentity("local"),
                services),
            services);
    }

    private static async ValueTask<TransportFrame> InvokeAsync(
        HotfixActorClusterHandler handler,
        RemoteActorInvocation invocation,
        bool tell,
        CancellationToken cancellationToken,
        RouteLocation? location = null)
    {
        using var request = ClusterActorWireCodec.EncodeRequest(
            invocation,
            location ?? CreateLocation());
        return await handler.HandleActorRpcAsync(request.Memory, tell, cancellationToken);
    }

    private static RouteLocation CreateLocation()
    {
        return new RouteLocation(
            ClusterActorRouteKeys.ForActor("user/1"),
            new NodeId("local"),
            new NodeEndpoint("tcp://127.0.0.1:24001"),
            DateTimeOffset.UtcNow.AddMinutes(1),
            nodeEpoch: 1);
    }

    private static RemoteActorInvocation CreateInvocation<TRequest>(
        ulong methodId,
        TRequest request)
    {
        return RemoteActorInvocation.Create(
            new NodeId("local"),
            ActorId.From("user/1"),
            "test",
            "Notify",
            methodId,
            request,
            DateTimeOffset.UtcNow.AddMinutes(1),
            expectedNodeEpoch: 1);
    }

    private static RemoteActorInvocation CreateInvocation<TRequest, TResult>(
        ulong methodId,
        TRequest request)
    {
        return RemoteActorInvocation.Create<TRequest, TResult>(
            new NodeId("local"),
            ActorId.From("user/1"),
            "test",
            "Ping",
            methodId,
            request,
            DateTimeOffset.UtcNow.AddMinutes(1),
            expectedNodeEpoch: 1);
    }

    private static HotfixRuntimeSnapshot CreateSnapshot(
        HotfixActorMethodDescriptor descriptor,
        Action? onRetired = null)
    {
        var table = new HotfixDispatchTable(
            1,
            [],
            [],
            [descriptor]);
        var services = new ServiceCollection().BuildServiceProvider();
        table.ValidateModuleActivation(services);
        return new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
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

    private static HotfixRuntimeSnapshot CreateActorTypeSnapshot(Type actorType)
    {
        var table = new HotfixDispatchTable(
            1,
            [],
            [],
            [],
            [new HotfixActorLifecycleDescriptor(
                typeof(HotfixActorClusterHandlerTests),
                actorType,
                null,
                null)]);
        var services = new ServiceCollection().BuildServiceProvider();
        return new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
            services,
            table,
            services,
            mainAssembly: null,
            loadContext: null,
            sourceVersion: "test",
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired: null);
    }

    private static HotfixActorMethodDescriptor CreatePingDescriptor()
    {
        return new HotfixActorMethodDescriptor(
            PingMethodKey,
            typeof(HotfixActorClusterHandlerTests),
            typeof(TestActor),
            nameof(PingAsync),
            typeof(PingRequest),
            typeof(PingReply),
            typeof(HotfixActorClusterHandlerTests).GetMethod(
                nameof(PingAsync),
                BindingFlags.Public | BindingFlags.Instance)!,
            hasCancellationToken: true);
    }

    private static HotfixActorMethodDescriptor CreateNotifyDescriptor()
    {
        return new HotfixActorMethodDescriptor(
            NotifyMethodKey,
            typeof(HotfixActorClusterHandlerTests),
            typeof(TestActor),
            nameof(NotifyAsync),
            typeof(NotifyRequest),
            null,
            typeof(HotfixActorClusterHandlerTests).GetMethod(
                nameof(NotifyAsync),
                BindingFlags.Public | BindingFlags.Instance)!,
            hasCancellationToken: true);
    }

    private static HotfixActorMethodDescriptor CreateThrowDescriptor()
    {
        return new HotfixActorMethodDescriptor(
            ThrowMethodKey,
            typeof(HotfixActorClusterHandlerTests),
            typeof(TestActor),
            nameof(ThrowAsync),
            typeof(PingRequest),
            typeof(PingReply),
            typeof(HotfixActorClusterHandlerTests).GetMethod(
                nameof(ThrowAsync),
                BindingFlags.Public | BindingFlags.Instance)!,
            hasCancellationToken: true);
    }

    public ValueTask<PingReply> PingAsync(
        TestActor actor,
        PingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Value == "require-scope" && HotfixDispatchRuntimeScope.Current is null)
        {
            throw new InvalidOperationException(
                "Hotfix dispatch scope is not active inside the actor mailbox.");
        }

        return actor.PingAsync(request, cancellationToken);
    }

    public ValueTask NotifyAsync(
        TestActor actor,
        NotifyRequest request,
        CancellationToken cancellationToken)
    {
        return actor.NotifyAsync(request, cancellationToken);
    }

    public ValueTask<PingReply> ThrowAsync(
        TestActor actor,
        PingRequest request,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("hotfix actor method failed");
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PingRequest
    {
        [MemoryPackOrder(0)]
        public string Value { get; set; } = string.Empty;
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PingReply
    {
        [MemoryPackOrder(0)]
        public string Value { get; set; } = string.Empty;
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class NotifyRequest
    {
        [MemoryPackOrder(0)]
        public string Value { get; set; } = string.Empty;
    }

    public sealed class TestActor : GameActor
    {
        public string? LastPing { get; private set; }

        public string? LastNotification { get; private set; }

        public ValueTask<PingReply> PingAsync(
            PingRequest request,
            CancellationToken cancellationToken)
        {
            LastPing = request.Value;
            return ValueTask.FromResult(new PingReply { Value = $"pong:{request.Value}" });
        }

        public ValueTask NotifyAsync(
            NotifyRequest request,
            CancellationToken cancellationToken)
        {
            LastNotification = request.Value;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HostCreateActor : GameActor;

    private sealed class RecordingActorRuntime : IActorRuntime
    {
        public TestActor Actor { get; } = new();

        public Type? LastActorType { get; private set; }

        public ActorId? LastActorId { get; private set; }

        public ActorTellResult TellResult { get; init; } = ActorTellResult.Accepted;

        public TaskCompletionSource? TellEntered { get; init; }

        public Task? TellRelease { get; init; }

        public bool SuppressExecutionContextFlow { get; init; }

        public ValueTask TellAsync<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor => throw new NotSupportedException();

        public ActorTellResult TryTell<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor => throw new NotSupportedException();

        public ValueTask TellAsync(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ActorTellResult TryTell(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
        {
            LastActorType = actorType;
            LastActorId = id;
            if (TellResult != ActorTellResult.Accepted)
            {
                return TellResult;
            }

            _ = RunTellAsync(message, cancellationToken);
            return ActorTellResult.Accepted;
        }

        private async Task RunTellAsync(
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken)
        {
            TellEntered?.TrySetResult();
            if (TellRelease is not null)
            {
                await TellRelease.WaitAsync(cancellationToken);
            }

            await message(Actor, cancellationToken);
        }

        public ValueTask<TResult> AskAsync<TActor, TResult>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask<TResult>> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor => throw new NotSupportedException();

        public async ValueTask<object?> AskAsync(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask<object?>> message,
            CancellationToken cancellationToken = default)
        {
            LastActorType = actorType;
            LastActorId = id;
            if (!SuppressExecutionContextFlow)
            {
                return await message(Actor, cancellationToken);
            }

            Task<object?> dispatch;
            using (ExecutionContext.SuppressFlow())
            {
                dispatch = Task.Run(
                    async () => await message(Actor, cancellationToken),
                    cancellationToken);
            }

            return await dispatch;
        }

        public bool TryGetMailboxMetrics(
            ActorId id,
            out ActorMailboxMetrics metrics)
        {
            metrics = default;
            return false;
        }

        public IReadOnlyList<ActorId> GetActiveActorIds(Type actorType) => [];

        public ActorState GetState(ActorId id) => ActorState.Active;
    }

    private sealed class StubMembership(
        ClusterMembershipSnapshot current) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; } = current;

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId observedView,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);
    }

    private sealed class FixedRuntimeAccessor(
        HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current { get; } = snapshot;
    }

    private sealed class HandlerFixture(
        HotfixActorClusterHandler handler,
        ServiceProvider services) : IAsyncDisposable
    {
        public HotfixActorClusterHandler Handler { get; } = handler;

        public ValueTask DisposeAsync() => services.DisposeAsync();
    }
}
