using System.Reflection;
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Tests.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Tests.Testing;
using Lakona.Rpc.Core;
using Lakona.Rpc.Client;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Loopback;
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
    private static readonly ActorActivationId DefaultActivation = new(
        Guid.Parse("40000002-0000-0000-0000-000000000000"));

    [Fact]
    public void Construction_rejects_missing_cluster_membership()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<ArgumentNullException>(() => new HotfixActorClusterHandler(
            new RecordingActorRuntime(),
            new LocalActorNodeIdentity("local"),
            null!,
            services));
    }

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
    public async Task Actor_rpc_ask_is_cancelled_when_its_time_to_live_expires()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new RecordingActorRuntime
        {
            AskEntered = entered,
            AskRelease = Task.Delay(
                Timeout.InfiniteTimeSpan,
                TestContext.Current.CancellationToken)
        };
        var time = new ManualDeadlineTimeProvider();
        await using var fixture = CreateFixture(
            runtime,
            CreateSnapshot(CreatePingDescriptor()),
            timeProvider: time);
        var invocation = CreateInvocation<PingRequest, PingReply>(
            CreatePingDescriptor().MethodId,
            new PingRequest { Value = "expires" });
        using var request = ClusterActorWireCodec.EncodeRequest(
            invocation,
            CreateLocation(),
            TimeSpan.FromSeconds(10));

        var call = fixture.Handler.HandleActorRpcAsync(
            request.Memory,
            tell: false,
            TestContext.Current.CancellationToken).AsTask();
        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        time.Expire();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task Actor_rpc_remote_cancellation_stops_an_active_ask_cooperatively()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new RecordingActorRuntime
        {
            AskEntered = entered,
            AskRelease = Task.Delay(
                Timeout.InfiniteTimeSpan,
                TestContext.Current.CancellationToken)
        };
        await using var fixture = CreateFixture(runtime, CreateSnapshot(CreatePingDescriptor()));
        var invocation = CreateInvocation<PingRequest, PingReply>(
            CreatePingDescriptor().MethodId,
            new PingRequest { Value = "cancel" });
        using var request = ClusterActorWireCodec.EncodeRequest(
            invocation,
            CreateLocation(),
            TimeSpan.FromMinutes(1));

        var call = fixture.Handler.HandleActorRpcAsync(
            request.Memory,
            tell: false,
            TestContext.Current.CancellationToken).AsTask();
        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        using var cancellationWriter = new PooledFrameBufferWriter();
        using var cancellationPayloadWriter = new PooledFrameBufferWriter();
        ClusterActorWireCodec.WriteCancellationRequest(
            cancellationPayloadWriter,
            invocation.InvocationId);
        using var cancellationPayload = cancellationPayloadWriter.DetachFrame();
        fixture.Handler.HandleActorCancellationRpc(
            cancellationPayload.Memory,
            cancellationWriter);
        using var cancellationResponse = cancellationWriter.DetachFrame();
        var cancellationReply = ClusterActorWireCodec.DecodeReply(
            cancellationResponse.Memory);

        Assert.Equal(RemoteActorStatus.Accepted, cancellationReply.Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task Cluster_transport_sends_best_effort_cancellation_to_the_executing_node()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new RecordingActorRuntime
        {
            AskEntered = entered,
            AskCancelled = cancelled,
            AskRelease = Task.Delay(
                Timeout.InfiniteTimeSpan,
                TestContext.Current.CancellationToken)
        };
        await using var fixture = CreateFixture(runtime, CreateSnapshot(CreatePingDescriptor()));
        var registry = new RpcServiceRegistry();
        ClusterActorRpcBinder.Bind(registry, fixture.Handler);
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new MemoryPackRpcSerializer();
        await using var server = new RpcSession(serverTransport, serializer, registry);
        await using var client = new RpcClientRuntime(clientTransport, serializer);
        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.StartAsync(TestContext.Current.CancellationToken);
        var location = CreateLocation();
        var membership = new ImmediateTestClusterMembership(new ClusterMembershipSnapshot(
            location.NodeReference.Cluster,
            location.MembershipView,
            [new ClusterMember(
                location.NodeReference,
                ClusterMemberState.Active,
                location.Endpoint)]));
        var transport = new RpcClusterActorTransport(
            new FixedClientFactory(client),
            membership);
        var invocation = CreateInvocation<PingRequest, PingReply>(
            CreatePingDescriptor().MethodId,
            new PingRequest { Value = "cancel-over-rpc" });
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var call = transport.AskAsync(invocation, callerCancellation.Token).AsTask();
        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        callerCancellation.Cancel();
        var result = await call;

        Assert.Equal(RemoteActorStatus.Cancelled, result.Status);
        Assert.Equal(RemoteActorRetrySafety.Indeterminate, result.RetrySafety);
        await cancelled.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
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

        using var request = ClusterActorWireCodec.EncodeRequest(
            invocation,
            CreateLocation(),
            TimeSpan.FromMinutes(1));
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

    [Theory]
    [InlineData("missing")]
    [InlineData("cluster")]
    [InlineData("node")]
    [InlineData("incarnation")]
    [InlineData("view")]
    [InlineData("activation")]
    public async Task Actor_rpc_rejects_incomplete_exact_proof_as_malformed(
        string invalidField)
    {
        var runtime = new RecordingActorRuntime();
        await using var fixture = CreateFixture(runtime, CreateSnapshot(CreatePingDescriptor()));
        var header = CreateValidWireHeader();
        switch (invalidField)
        {
            case "missing":
                header.TargetProof = null!;
                break;
            case "cluster":
                header.TargetProof.ClusterIncarnation = Guid.Empty;
                break;
            case "node":
                header.TargetProof.Node = " ";
                break;
            case "incarnation":
                header.TargetProof.NodeIncarnation = Guid.Empty;
                break;
            case "view":
                header.TargetProof.MembershipView = 0;
                break;
            case "activation":
                header.TargetProof.ActivationId = Guid.Empty;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidField));
        }

        using var response = await fixture.Handler.HandleActorRpcAsync(
            MemoryPackSerializer.Serialize(header),
            tell: false,
            TestContext.Current.CancellationToken);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.DeserializationFailed, reply.Status);
        Assert.Equal(RemoteActorRetrySafety.DefinitelyNotExecuted, reply.RetrySafety);
        Assert.Null(runtime.LastActorType);
    }

    [Fact]
    public void Actor_request_wire_orders_are_compact_for_v4()
    {
        var headerOrders = typeof(ClusterActorWireRequestHeader)
            .GetProperties()
            .Select(property => property.GetCustomAttribute<MemoryPackOrderAttribute>()?.Order)
            .Where(order => order.HasValue)
            .Select(order => order!.Value)
            .Order()
            .ToArray();
        var proofOrders = typeof(ClusterActorWireTargetProof)
            .GetProperties()
            .Select(property => property.GetCustomAttribute<MemoryPackOrderAttribute>()?.Order)
            .Where(order => order.HasValue)
            .Select(order => order!.Value)
            .Order()
            .ToArray();

        Assert.Equal([0, 1, 2, 3, 4], headerOrders);
        Assert.Equal([0, 1, 2, 3, 4], proofOrders);
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
        var membership = new ImmediateTestClusterMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            [new ClusterMember(
                local,
                ClusterMemberState.Active,
                new NodeEndpoint("tcp://127.0.0.1:24001"))]));
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
                membership,
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
            activationId: ActorActivationId.New());
        var location = new RouteLocation(
            ClusterActorRouteKeys.ForActor(actorId.Value),
            local,
            new MembershipViewId(1),
            new NodeEndpoint("tcp://127.0.0.1:24001"));

        using var metrics = new MetricReasonCollector(
            ClusterDiagnostics.MeterName,
            "lakona.game.cluster.actor_request.proof_failure",
            "lakona.game.cluster.reason");
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
        Assert.Contains("activation", metrics.Reasons);
    }

    [Theory]
    [InlineData("cluster", "cluster_incarnation")]
    [InlineData("node", "target_node")]
    [InlineData("incarnation", "node_incarnation")]
    public async Task Actor_rpc_keeps_proof_failures_generic_but_records_the_failed_step(
        string staleField,
        string expectedReason)
    {
        var runtime = new RecordingActorRuntime();
        await using var fixture = CreateFixture(runtime, CreateSnapshot(CreatePingDescriptor()));
        var invocation = CreateInvocation<PingRequest, PingReply>(
            CreatePingDescriptor().MethodId,
            new PingRequest { Value = "stale-proof" });
        using var encoded = ClusterActorWireCodec.EncodeRequest(
            invocation,
            CreateLocation(),
            TimeSpan.FromMinutes(1));
        var decoded = ClusterActorWireCodec.DecodeRequest(encoded.Memory);
        switch (staleField)
        {
            case "cluster":
                decoded.Header.TargetProof.ClusterIncarnation =
                    Guid.Parse("50000000-0000-0000-0000-000000000000");
                break;
            case "node":
                decoded.Header.TargetProof.Node = "other";
                break;
            case "incarnation":
                decoded.Header.TargetProof.NodeIncarnation =
                    Guid.Parse("50000001-0000-0000-0000-000000000000");
                break;
            case "view":
                decoded.Header.TargetProof.MembershipView++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(staleField));
        }

        using var metrics = new MetricReasonCollector(
            ClusterDiagnostics.MeterName,
            "lakona.game.cluster.actor_request.proof_failure",
            "lakona.game.cluster.reason");
        using var response = await fixture.Handler.HandleActorRpcAsync(
            ReencodeRequest(decoded),
            tell: false,
            TestContext.Current.CancellationToken);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.NodeUnavailable, reply.Status);
        Assert.Equal("Remote Actor target or activation is stale.", reply.Message);
        Assert.Equal(RemoteActorRetrySafety.DefinitelyNotExecuted, reply.RetrySafety);
        Assert.Contains(expectedReason, metrics.Reasons);
        Assert.Null(runtime.LastActorType);
    }

    [Fact]
    public async Task Actor_rpc_stops_waiting_for_the_target_membership_view_at_its_deadline()
    {
        var location = CreateLocation();
        var membership = new ControlledTestClusterMembership(new ClusterMembershipSnapshot(
            location.NodeReference.Cluster,
            location.MembershipView,
            [new ClusterMember(
                location.NodeReference,
                ClusterMemberState.Active,
                location.Endpoint)]));
        var time = new ManualDeadlineTimeProvider();
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(time)
            .AddSingleton<ClusterActorCancellationRegistry>()
            .AddSingleton<IHotfixRuntimeAccessor>(
                new FixedRuntimeAccessor(CreateSnapshot(CreatePingDescriptor())))
            .BuildServiceProvider();
        await using var fixture = new HandlerFixture(
            new HotfixActorClusterHandler(
                new RecordingActorRuntime(),
                new LocalActorNodeIdentity("local"),
                membership,
                services,
                timeProvider: time),
            services);
        var invocation = CreateInvocation<PingRequest, PingReply>(
            CreatePingDescriptor().MethodId,
            new PingRequest { Value = "future-view" });
        var futureLocation = new RouteLocation(
            ClusterActorRouteKeys.ForActor(invocation.ActorId.Value),
            location.NodeReference,
            new MembershipViewId(location.MembershipView.Value + 1),
            location.Endpoint);
        using var request = ClusterActorWireCodec.EncodeRequest(
            invocation,
            futureLocation,
            TimeSpan.FromSeconds(10));

        var call = fixture.Handler.HandleActorRpcAsync(
            request.Memory,
            tell: false,
            TestContext.Current.CancellationToken).AsTask();

        Assert.False(call.IsCompleted);
        time.Expire();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Theory]
    [InlineData(ClusterMemberState.Joining, true, "local_node")]
    [InlineData(ClusterMemberState.Active, false, "directory_unavailable")]
    public async Task Actor_rpc_records_the_unavailable_proof_dependency(
        ClusterMemberState localState,
        bool includeDirectoryCache,
        string expectedReason)
    {
        var runtime = new RecordingActorRuntime();
        await using var fixture = CreateFixture(
            runtime,
            CreateSnapshot(CreatePingDescriptor()),
            localState,
            includeDirectoryCache);
        var invocation = CreateInvocation<PingRequest, PingReply>(
            CreatePingDescriptor().MethodId,
            new PingRequest { Value = "unavailable-proof-dependency" });

        using var metrics = new MetricReasonCollector(
            ClusterDiagnostics.MeterName,
            "lakona.game.cluster.actor_request.proof_failure",
            "lakona.game.cluster.reason");
        using var response = await InvokeAsync(
            fixture.Handler,
            invocation,
            tell: false,
            TestContext.Current.CancellationToken);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.NodeUnavailable, reply.Status);
        Assert.Equal(RemoteActorRetrySafety.DefinitelyNotExecuted, reply.RetrySafety);
        Assert.Contains(expectedReason, metrics.Reasons);
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
        var membership = new ImmediateTestClusterMembership(new ClusterMembershipSnapshot(cluster, new MembershipViewId(2),
        [
            new ClusterMember(local, ClusterMemberState.Active, new NodeEndpoint("tcp://127.0.0.1:24001")),
            new ClusterMember(other, ClusterMemberState.Active, new NodeEndpoint("tcp://127.0.0.1:24002"))
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
        await using var fixture = new HandlerFixture(new HotfixActorClusterHandler(
            runtime,
            new LocalActorNodeIdentity("local"),
            membership,
            services), services);
        var invocation = RemoteActorInvocation.Create<PingRequest, PingReply>(
            local.Node, actorId, "test", "Ping", CreatePingDescriptor().MethodId,
            new PingRequest { Value = "after-commit" }, DateTimeOffset.UtcNow.AddMinutes(1),
            ownerReference: local, activationId: activation.Record.ActivationId);
        var staleViewLocation = new RouteLocation(ClusterActorRouteKeys.ForActor(actorId.Value), local,
            new MembershipViewId(1), new NodeEndpoint("tcp://127.0.0.1:24001"));

        using var response = await InvokeAsync(fixture.Handler, invocation, false,
            TestContext.Current.CancellationToken, staleViewLocation);
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.Replied, reply.Status);
        Assert.Equal("after-commit", runtime.Actor.LastPing);
    }

    [Fact]
    public async Task Actor_rpc_waits_through_intermediate_membership_views_before_validating_the_proof()
    {
        var location = CreateLocation();
        var initial = new ClusterMembershipSnapshot(
            location.NodeReference.Cluster,
            location.MembershipView,
            [new ClusterMember(
                location.NodeReference,
                ClusterMemberState.Active,
                location.Endpoint)]);
        var membership = new ControlledTestClusterMembership(initial);
        var directory = new TestActorDirectory();
        var actorId = ActorId.From("user/1");
        var activation = await directory.AcquireAsync(
            actorId,
            location.NodeReference,
            DefaultActivation,
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
                membership,
                services),
            services);
        var invocation = RemoteActorInvocation.Create<PingRequest, PingReply>(
            location.NodeReference.Node,
            actorId,
            "test",
            "Ping",
            CreatePingDescriptor().MethodId,
            new PingRequest { Value = "after-catch-up" },
            DateTimeOffset.UtcNow.AddMinutes(1),
            ownerReference: location.NodeReference,
            activationId: activation.Record.ActivationId);
        var futureLocation = new RouteLocation(
            ClusterActorRouteKeys.ForActor(actorId.Value),
            location.NodeReference,
            new MembershipViewId(location.MembershipView.Value + 2),
            location.Endpoint);

        var call = InvokeAsync(
            fixture.Handler,
            invocation,
            tell: false,
            TestContext.Current.CancellationToken,
            futureLocation).AsTask();

        Assert.False(call.IsCompleted);
        membership.Publish(new ClusterMembershipSnapshot(
            initial.Cluster,
            new MembershipViewId(location.MembershipView.Value + 1),
            initial.Members));
        Assert.False(call.IsCompleted);
        membership.Publish(new ClusterMembershipSnapshot(
            initial.Cluster,
            futureLocation.MembershipView,
            initial.Members));
        using var response = await call;
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.Replied, reply.Status);
        Assert.Equal("after-catch-up", runtime.Actor.LastPing);
    }

    [Fact]
    public async Task Actor_rpc_revalidates_the_exact_node_after_membership_catches_up()
    {
        var location = CreateLocation();
        var initial = new ClusterMembershipSnapshot(
            location.NodeReference.Cluster,
            location.MembershipView,
            [new ClusterMember(
                location.NodeReference,
                ClusterMemberState.Active,
                location.Endpoint)]);
        var membership = new ControlledTestClusterMembership(initial);
        var runtime = new RecordingActorRuntime();
        await using var fixture = CreateFixture(
            runtime,
            CreateSnapshot(CreatePingDescriptor()),
            membership: membership);
        var invocation = CreateInvocation<PingRequest, PingReply>(
            CreatePingDescriptor().MethodId,
            new PingRequest { Value = "replaced-node" });
        var futureLocation = new RouteLocation(
            ClusterActorRouteKeys.ForActor(invocation.ActorId.Value),
            location.NodeReference,
            new MembershipViewId(location.MembershipView.Value + 1),
            location.Endpoint);
        using var metrics = new MetricReasonCollector(
            ClusterDiagnostics.MeterName,
            "lakona.game.cluster.actor_request.proof_failure",
            "lakona.game.cluster.reason");

        var call = InvokeAsync(
            fixture.Handler,
            invocation,
            tell: false,
            TestContext.Current.CancellationToken,
            futureLocation).AsTask();

        Assert.False(call.IsCompleted);
        var replacement = new NodeReference(
            location.NodeReference.Cluster,
            location.NodeReference.Node,
            NodeIncarnationId.New());
        membership.Publish(new ClusterMembershipSnapshot(
            initial.Cluster,
            futureLocation.MembershipView,
            [new ClusterMember(
                replacement,
                ClusterMemberState.Active,
                location.Endpoint)]));
        using var response = await call;
        var reply = ClusterActorWireCodec.DecodeReply(response.Memory);

        Assert.Equal(RemoteActorStatus.NodeUnavailable, reply.Status);
        Assert.Equal(RemoteActorRetrySafety.DefinitelyNotExecuted, reply.RetrySafety);
        Assert.Contains("node_incarnation", metrics.Reasons);
        Assert.Null(runtime.LastActorType);
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
        IActorActivationDispatcher runtime,
        HotfixRuntimeSnapshot snapshot,
        ClusterMemberState localState = ClusterMemberState.Active,
        bool includeDirectoryCache = true,
        TimeProvider? timeProvider = null,
        IClusterMembership? membership = null)
    {
        var location = CreateLocation();
        var effectiveMembership = membership ?? new ImmediateTestClusterMembership(
            new ClusterMembershipSnapshot(
                location.NodeReference.Cluster,
                location.MembershipView,
                [new ClusterMember(
                    location.NodeReference,
                    localState,
                    location.Endpoint)]));
        var cache = new InMemoryActorDirectoryCache();
        cache.Set(new ActorDirectoryRecord(
            ActorId.From("user/1"),
            location.NodeReference,
            DefaultActivation,
            DateTimeOffset.UtcNow));
        var effectiveTimeProvider = timeProvider ?? TimeProvider.System;
        var serviceCollection = new ServiceCollection()
            .AddSingleton(effectiveTimeProvider)
            .AddSingleton<ClusterActorCancellationRegistry>()
            .AddSingleton<IHotfixRuntimeAccessor>(new FixedRuntimeAccessor(snapshot));
        if (includeDirectoryCache)
        {
            serviceCollection.AddSingleton<IActorDirectoryCache>(cache);
        }

        var services = serviceCollection.BuildServiceProvider();
        return new HandlerFixture(
            new HotfixActorClusterHandler(
                runtime,
                new LocalActorNodeIdentity("local"),
                effectiveMembership,
                services,
                timeProvider: effectiveTimeProvider),
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
            location ?? CreateLocation(),
            TimeSpan.FromMinutes(1));
        return await handler.HandleActorRpcAsync(request.Memory, tell, cancellationToken);
    }

    private static RouteLocation CreateLocation()
    {
        return new RouteLocation(
            ClusterActorRouteKeys.ForActor("user/1"),
            new NodeReference(
                new ClusterIncarnationId(Guid.Parse("40000000-0000-0000-0000-000000000000")),
                new NodeId("local"),
                new NodeIncarnationId(Guid.Parse("40000001-0000-0000-0000-000000000000"))),
            new MembershipViewId(1),
            new NodeEndpoint("tcp://127.0.0.1:24001"));
    }

    private static ClusterActorWireRequestHeader CreateValidWireHeader()
    {
        var location = CreateLocation();
        return new ClusterActorWireRequestHeader
        {
            ActorId = "user/1",
            MethodId = CreatePingDescriptor().MethodId,
            TimeToLiveTicks = TimeSpan.FromMinutes(1).Ticks,
            InvocationId = Guid.NewGuid(),
            TargetProof = new ClusterActorWireTargetProof
            {
                ClusterIncarnation = location.NodeReference.Cluster.Value,
                Node = location.NodeReference.Node.Value,
                NodeIncarnation = location.NodeReference.Incarnation.Value,
                MembershipView = location.MembershipView.Value,
                ActivationId = DefaultActivation.Value
            }
        };
    }

    private static byte[] ReencodeRequest(ClusterActorWireRequest request)
    {
        var header = MemoryPackSerializer.Serialize(request.Header);
        var payload = new byte[header.Length + request.Body.Length];
        header.CopyTo(payload, 0);
        request.Body.Span.CopyTo(payload.AsSpan(header.Length));
        return payload;
    }

    private static RemoteActorInvocation CreateInvocation<TRequest>(
        ulong methodId,
        TRequest request)
    {
        var location = CreateLocation();
        return RemoteActorInvocation.Create(
            new NodeId("local"),
            ActorId.From("user/1"),
            "test",
            "Notify",
            methodId,
            request,
            DateTimeOffset.UtcNow.AddMinutes(1),
            ownerReference: location.NodeReference,
            activationId: DefaultActivation);
    }

    private static RemoteActorInvocation CreateInvocation<TRequest, TResult>(
        ulong methodId,
        TRequest request)
    {
        var location = CreateLocation();
        return RemoteActorInvocation.Create<TRequest, TResult>(
            new NodeId("local"),
            ActorId.From("user/1"),
            "test",
            "Ping",
            methodId,
            request,
            DateTimeOffset.UtcNow.AddMinutes(1),
            ownerReference: location.NodeReference,
            activationId: DefaultActivation);
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

    private sealed class ControlledTestClusterMembership(ClusterMembershipSnapshot current)
        : IClusterMembership
    {
        private TaskCompletionSource<ClusterMembershipSnapshot> changed = NewCompletion();

        public ClusterMembershipSnapshot Current { get; private set; } = current;

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default)
        {
            if (Current.View.CompareTo(after) > 0)
            {
                return ValueTask.FromResult(Current);
            }

            return new ValueTask<ClusterMembershipSnapshot>(
                changed.Task.WaitAsync(cancellationToken));
        }

        public void Publish(ClusterMembershipSnapshot snapshot)
        {
            Current = snapshot;
            var completed = changed;
            changed = NewCompletion();
            completed.TrySetResult(snapshot);
        }

        private static TaskCompletionSource<ClusterMembershipSnapshot> NewCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RecordingActorRuntime : IActorRuntime, IActorActivationDispatcher
    {
        public TestActor Actor { get; } = new();

        public ActorTellResult TryTellExact(
            Type actorType,
            ActorId actorId,
            ActorActivationId activationId,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default) =>
            TryTell(actorType, actorId, message, cancellationToken);

        public ValueTask<object?> AskExactAsync(
            Type actorType,
            ActorId actorId,
            ActorActivationId activationId,
            Func<IActor, CancellationToken, ValueTask<object?>> message,
            CancellationToken cancellationToken = default) =>
            AskAsync(actorType, actorId, message, cancellationToken);

        public Type? LastActorType { get; private set; }

        public ActorId? LastActorId { get; private set; }

        public ActorTellResult TellResult { get; init; } = ActorTellResult.Accepted;

        public TaskCompletionSource? TellEntered { get; init; }

        public Task? TellRelease { get; init; }

        public TaskCompletionSource? AskEntered { get; init; }

        public Task? AskRelease { get; init; }

        public TaskCompletionSource? AskCancelled { get; init; }

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
            AskEntered?.TrySetResult();
            if (AskRelease is not null)
            {
                try
                {
                    await AskRelease.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    AskCancelled?.TrySetResult();
                    throw;
                }
            }

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

    private sealed class FixedRuntimeAccessor(
        HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current { get; } = snapshot;
    }

    private sealed class FixedClientFactory(IRpcClient client) : IClusterClientFactory
    {
        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(client);
        }
    }

    private sealed class HandlerFixture(
        HotfixActorClusterHandler handler,
        ServiceProvider services) : IAsyncDisposable
    {
        public HotfixActorClusterHandler Handler { get; } = handler;

        public ValueTask DisposeAsync() => services.DisposeAsync();
    }
}
