using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class RpcClusterActorTransportTests
{
    private static readonly ClusterIncarnationId Cluster = new(Guid.Parse("50000000-0000-0000-0000-000000000000"));

    [Fact]
    public async Task Exact_owner_from_previous_incarnation_fails_before_creating_client()
    {
        var current = Reference("node-b", 2);
        var stale = Reference("node-b", 1);
        var factory = new RejectingClientFactory();
        var transport = new RpcClusterActorTransport(
            factory,
            new FixedMembership(Snapshot(Member(current, ClusterMemberState.Active))));

        var result = await transport.TellAsync(
            Invocation(stale),
            TestContext.Current.CancellationToken);

        AssertStale(result, factory);
    }

    [Fact]
    public async Task Exact_nonReady_owner_fails_before_creating_client()
    {
        var owner = Reference("node-b", 1);
        var factory = new RejectingClientFactory();
        var transport = new RpcClusterActorTransport(
            factory,
            new FixedMembership(Snapshot(Member(owner, ClusterMemberState.Joining))));

        var result = await transport.TellAsync(
            Invocation(owner),
            TestContext.Current.CancellationToken);

        AssertStale(result, factory);
    }

    [Fact]
    public async Task Node_only_target_without_one_ready_member_fails_before_creating_client()
    {
        var target = Reference("node-b", 1);
        var factory = new RejectingClientFactory();
        var transport = new RpcClusterActorTransport(
            factory,
            new FixedMembership(Snapshot(Member(target, ClusterMemberState.Joining))));

        var result = await transport.TellAsync(
            Invocation(owner: null),
            TestContext.Current.CancellationToken);

        AssertStale(result, factory);
    }

    [Fact]
    public async Task Exact_owner_without_activation_fails_before_creating_client()
    {
        var owner = Reference("node-b", 1);
        var factory = new RejectingClientFactory();
        var transport = new RpcClusterActorTransport(
            factory,
            new FixedMembership(Snapshot(Member(owner, ClusterMemberState.Active))));

        var result = await transport.TellAsync(
            Invocation(owner, includeActivation: false),
            TestContext.Current.CancellationToken);

        AssertStale(result, factory);
    }

    [Fact]
    public async Task Expired_deadline_fails_safely_before_resolving_the_target()
    {
        var owner = Reference("node-b", 1);
        var factory = new RejectingClientFactory();
        var now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        var transport = new RpcClusterActorTransport(
            factory,
            new FixedMembership(Snapshot(Member(owner, ClusterMemberState.Active))),
            new FixedTimeProvider(now));

        var result = await transport.TellAsync(
            Invocation(owner, deadline: now.AddTicks(-1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(RemoteActorStatus.Expired, result.Status);
        Assert.Equal(RemoteActorRetrySafety.DefinitelyNotExecuted, result.RetrySafety);
        Assert.Equal(0, factory.Calls);
    }

    [Fact]
    public async Task Connection_timeout_is_indeterminate_and_never_safe_to_retry()
    {
        var owner = Reference("node-b", 1);
        var transport = new RpcClusterActorTransport(
            new ThrowingClientFactory(new TimeoutException("connect timed out")),
            new FixedMembership(Snapshot(Member(owner, ClusterMemberState.Active))));

        var result = await transport.AskAsync(
            Invocation(owner),
            TestContext.Current.CancellationToken);

        Assert.Equal(RemoteActorStatus.Timeout, result.Status);
        Assert.Equal(RemoteActorRetrySafety.Indeterminate, result.RetrySafety);
    }

    [Fact]
    public async Task Caller_cancellation_is_indeterminate_even_before_a_reply_exists()
    {
        var owner = Reference("node-b", 1);
        var transport = new RpcClusterActorTransport(
            new WaitingClientFactory(),
            new FixedMembership(Snapshot(Member(owner, ClusterMemberState.Active))));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var call = transport.AskAsync(Invocation(owner), cancellation.Token).AsTask();
        cancellation.Cancel();
        var result = await call;

        Assert.Equal(RemoteActorStatus.Cancelled, result.Status);
        Assert.Equal(RemoteActorRetrySafety.Indeterminate, result.RetrySafety);
    }

    [Fact]
    public async Task Pre_cancelled_call_does_not_resolve_or_send()
    {
        var owner = Reference("node-b", 1);
        var factory = new RejectingClientFactory();
        var transport = new RpcClusterActorTransport(
            factory,
            new FixedMembership(Snapshot(Member(owner, ClusterMemberState.Active))));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        var result = await transport.AskAsync(Invocation(owner), cancellation.Token);

        Assert.Equal(RemoteActorStatus.Cancelled, result.Status);
        Assert.Equal(RemoteActorRetrySafety.Indeterminate, result.RetrySafety);
        Assert.Equal(0, factory.Calls);
    }

    private static void AssertStale(
        RemoteActorInvocationResult result,
        RejectingClientFactory factory)
    {
        Assert.Equal(RemoteActorStatus.NodeUnavailable, result.Status);
        Assert.Equal(RemoteActorRetrySafety.DefinitelyNotExecuted, result.RetrySafety);
        Assert.Contains("NodeUnavailable", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, factory.Calls);
    }

    private static RemoteActorInvocation Invocation(
        NodeReference? owner,
        bool includeActivation = true,
        DateTimeOffset? deadline = null) =>
        RemoteActorInvocation.Create(
            new NodeId("node-b"),
            ActorId.From("room/1"),
            "room",
            "notify",
            methodId: 1,
            request: "payload",
            deadline ?? DateTimeOffset.UtcNow.AddMinutes(1),
            ownerReference: owner,
            activationId: owner is not null && includeActivation
                ? ActorActivationId.New()
                : null);

    private static ClusterMembershipSnapshot Snapshot(params ClusterMember[] members) =>
        new(Cluster, new MembershipViewId(4), members);

    private static ClusterMember Member(NodeReference reference, ClusterMemberState state) =>
        new(
            reference,
            state,
            new NodeEndpoint($"tcp://{reference.Node.Value}:21000"));

    private static NodeReference Reference(string node, int incarnation) =>
        new(
            Cluster,
            new NodeId(node),
            new NodeIncarnationId(Guid.Parse($"{incarnation:D8}-0000-0000-0000-000000000000")));

    private sealed class FixedMembership(ClusterMembershipSnapshot current) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; } = current;

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default) => new(Current);
    }

    private sealed class RejectingClientFactory : IClusterClientFactory
    {
        public int Calls { get; private set; }

        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Stale routes must not create an RPC client.");
        }
    }

    private sealed class ThrowingClientFactory(Exception exception) : IClusterClientFactory
    {
        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IRpcClient>(exception);
    }

    private sealed class WaitingClientFactory : IClusterClientFactory
    {
        public async ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The wait should only end through cancellation.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
