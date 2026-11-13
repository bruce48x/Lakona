using System.Collections.Concurrent;
using System.Threading.Channels;
using Lakona.Game.Abstractions;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Game.Server.Tests.Testing;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Loopback;
using Lakona.Rpc.Transport.Tcp;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Lakona.Game.Server.Tests.Sessions;

public sealed class RemoteNotificationIsolationTests
{
    [Theory]
    [InlineData(ClientNotificationStatus.Accepted, false)]
    [InlineData(ClientNotificationStatus.Failed, false)]
    [InlineData(ClientNotificationStatus.Accepted, true)]
    public async Task Slow_session_does_not_hold_other_sessions_delivery_or_rpc_response(ClientNotificationStatus slowStatus, bool cancelCaller)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var ct = deadline.Token;
        await using var fixture = new Fixture(slowStatus);
        await fixture.StartAsync(ct);
        using var slowCaller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var slow = fixture.CallAsync(1, slowCaller.Token);
        Assert.Equal("slow-1", await fixture.Admitted.Reader.ReadAsync(ct));
        var fast = fixture.CallAsync(2, ct);
        Assert.Equal("fast-1", await fixture.Admitted.Reader.ReadAsync(ct));

        // Both commands are admitted before the batch clock moves. This catches
        // cross-session grouping without racing a wall-clock batching timeout.
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Sink.SlowEntered.Task.WaitAsync(ct);
        await fast.WaitAsync(ct);
        Assert.False(slow.IsCompleted);
        Assert.Contains("fast-1", fixture.Sink.Delivered);
        if (cancelCaller)
        {
            slowCaller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow);
        }

        var slowSecond = fixture.CallAsync(3, ct);
        await fixture.SecondSlowEnqueued.Task.WaitAsync(ct);
        var fastSecond = fixture.CallAsync(4, ct);
        Assert.Equal("fast-2", await fixture.Admitted.Reader.ReadAsync(ct));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fastSecond.WaitAsync(ct);
        Assert.False(slowSecond.IsCompleted);
        Assert.DoesNotContain("slow-2", fixture.Sink.Entered);
        Assert.Equal(["fast-1", "fast-2"], fixture.Sink.Delivered.ToArray());

        fixture.Sink.ReleaseSlow.TrySetResult();
        if (!cancelCaller)
        {
            if (slowStatus == ClientNotificationStatus.Accepted)
                await slow.WaitAsync(ct);
            else
                Assert.Equal(RpcStatus.InternalError, (await Assert.ThrowsAsync<RpcException>(() => slow.WaitAsync(ct))).Status);
        }
        Assert.Equal("slow-2", await fixture.Admitted.Reader.ReadAsync(ct));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await slowSecond.WaitAsync(ct);
        Assert.Equal(["slow-1", "slow-2"], fixture.Sink.Entered.Where(name => name.StartsWith("slow", StringComparison.Ordinal)));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TcpConnectionAcceptor acceptor = new(0);
        private readonly ClusterClientFactory factory = new(new ClusterRpcChannel());
        private readonly CancellationTokenSource stop = new();
        private readonly RpcServiceRegistry gatewayRegistry = new();
        private readonly ClusterClientNotificationDispatcher remote;
        private readonly ClientNotificationCommandRouter router;
        private readonly RpcSession publisher;
        private readonly RpcClientRuntime caller;
        private readonly RouteLocation target;
        private Task? serving;

        public Fixture(ClientNotificationStatus slowStatus)
        {
            Sink = new ControlledRuntime(slowStatus);
            var gateway = new NodeReference(new ClusterIncarnationId(Guid.NewGuid()), new NodeId("gateway"), NodeIncarnationId.New());
            var membership = new TestClusterMembership(new ClusterMembershipSnapshot(gateway.Cluster, new MembershipViewId(1),
                [new ClusterMember(gateway, ClusterMemberState.Active, new NodeEndpoint(acceptor.ListenAddress))]));
            target = new RouteLocation(new RouteKey("test"), gateway, membership.Current.View, new NodeEndpoint(acceptor.ListenAddress));
            ClientNotificationCommandBinder.BindOwned(gatewayRegistry, new ClientNotificationOwnerDispatcher(Sink, membership, gateway.Node));
            remote = new ClusterClientNotificationDispatcher(factory,
                new ClientNotificationBatchOptions { Window = TimeSpan.FromSeconds(1) }, Clock);
            router = new ClientNotificationCommandRouter(Sink, TestNotificationProfiles.JsonCommands(), membership,
                new ObservedDispatcher(remote, Admitted.Writer), new NodeId("publisher"));
            var requests = new RpcServiceRegistry();
            var slowSession = MembershipSessionLocator.Encode(gateway);
            var fastSession = MembershipSessionLocator.Encode(gateway);
            for (var index = 1; index <= 4; index++)
            {
                var method = index;
                requests.RegisterRaw(1, method, (_, _, _, _) =>
                {
                    var owner = method % 2 == 1 ? "slow" : "fast";
                    Assert.Equal(ClientNotificationStatus.Accepted, router.Enqueue(new ClientNotificationCommand
                    {
                        OwnerKey = owner,
                        SessionId = owner == "slow" ? slowSession : fastSession,
                        CallbackContractType = "test",
                        MethodName = owner + (method <= 2 ? "-1" : "-2")
                    }));
                    if (method == 3) SecondSlowEnqueued.TrySetResult();
                    return new ValueTask<RpcRawResult>(RpcRawResult.Ok(ReadOnlyMemory<byte>.Empty));
                });
            }
            LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
            publisher = TestRpcSession.Create(serverTransport, new JsonRpcSerializer(), true, requests);
            caller = new RpcClientRuntime(clientTransport, new JsonRpcSerializer());
        }

        public FakeTimeProvider Clock { get; } = new();
        public ControlledRuntime Sink { get; }
        public Channel<string> Admitted { get; } = Channel.CreateUnbounded<string>();
        public TaskCompletionSource SecondSlowEnqueued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task StartAsync(CancellationToken ct)
        {
            serving = ServeAsync();
            await factory.GetClientAsync(target, ct);
            await publisher.StartAsync(ct);
            await caller.StartAsync(ct);
        }

        public Task<RpcVoid> CallAsync(int method, CancellationToken ct) =>
            caller.CallAsync(new RpcMethod<int, RpcVoid>(1, method), 0, ct).AsTask();

        private async Task ServeAsync()
        {
            var accepted = await acceptor.AcceptAsync(stop.Token);
            await ClusterRpcProtocolNegotiation.NegotiateServerAsync(accepted.Transport, ClusterProtocol.Identifier, stop.Token);
            await using var session = TestRpcSession.Create(accepted.Transport, new MemoryPackRpcSerializer(), true, gatewayRegistry);
            await session.StartAsync(stop.Token);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            Sink.ReleaseSlow.TrySetResult();
            await router.DisposeAsync();
            await remote.DisposeAsync();
            await caller.DisposeAsync();
            await publisher.DisposeAsync();
            await factory.DisposeAsync();
            stop.Cancel();
            try { if (serving is not null) await serving; }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            await acceptor.DisposeAsync();
            stop.Dispose();
        }
    }

    private sealed class ObservedDispatcher(ClusterClientNotificationDispatcher inner, ChannelWriter<string> admitted) : IClientNotificationRemoteDispatcher
    {
        public ValueTask<ClientNotificationStatus> DispatchAsync(RouteLocation target, ClientNotificationCommand command, CancellationToken cancellationToken = default)
        {
            var result = inner.DispatchAsync(target, command, cancellationToken);
            Assert.True(admitted.TryWrite(command.MethodName));
            return result;
        }
    }

    private sealed class ControlledRuntime(ClientNotificationStatus slowStatus) : IReliablePushRuntime
    {
        public TaskCompletionSource SlowEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSlow { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> Entered { get; } = new();
        public ConcurrentQueue<string> Delivered { get; } = new();

        public async ValueTask<ClientNotificationStatus> PublishAsync(GameSessionKey session, ClientNotificationCommand command, CancellationToken cancellationToken = default)
        {
            Entered.Enqueue(command.MethodName);
            if (command.MethodName == "slow-1")
            {
                SlowEntered.TrySetResult();
                await ReleaseSlow.Task.WaitAsync(cancellationToken);
                if (slowStatus != ClientNotificationStatus.Accepted) return slowStatus;
            }
            Delivered.Enqueue(command.MethodName);
            return ClientNotificationStatus.Accepted;
        }

        public ValueTask<ClientNotificationStatus> PublishGeneratedAsync<TCallback, TPayload>(GameSessionKey session, int serviceId, int methodId, string methodName, TPayload payload, CancellationToken cancellationToken = default) where TCallback : class => throw new NotSupportedException();
        public ValueTask ReplayPendingAsync(GameSessionKey session, string connectionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReliablePushAckOutcome> AckAsync(GameSessionKey currentSession, GameSessionKey acknowledgedSession, long sequence, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
