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
            new FixedMembership(Snapshot(Member(current, ClusterMemberState.Ready))));

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
            new FixedMembership(Snapshot(Member(owner, ClusterMemberState.Recovering))));

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
            new FixedMembership(Snapshot(Member(owner, ClusterMemberState.Ready))));

        var result = await transport.TellAsync(
            Invocation(owner, includeActivation: false),
            TestContext.Current.CancellationToken);

        AssertStale(result, factory);
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
        bool includeActivation = true) =>
        RemoteActorInvocation.Create(
            new NodeId("node-b"),
            ActorId.From("room/1"),
            "room",
            "notify",
            methodId: 1,
            request: "payload",
            DateTimeOffset.UtcNow.AddMinutes(1),
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
            new NodeEndpoint($"tcp://{reference.Node.Value}:21000"),
            isVoter: true);

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
}
