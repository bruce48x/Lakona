using Lakona.Game.Cluster.Rpc;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterProtocolTests
{
    [Fact]
    public void Method_constants_preserve_compact_v2_assignments()
    {
        int[] methodIds =
        [
            ClusterProtocol.Methods.ActorAsk,
            ClusterProtocol.Methods.ActorTell,
            ClusterProtocol.Methods.ActorDirectoryLookup,
            ClusterProtocol.Methods.ActorDirectoryAcquire,
            ClusterProtocol.Methods.ActorDirectoryRelease,
            ClusterProtocol.Methods.ActorDirectoryActivationSnapshot,
            ClusterProtocol.Methods.ActorLifecycleCreate,
            ClusterProtocol.Methods.ActorLifecycleDestroy,
            ClusterProtocol.Methods.ClientNotificationDispatch,
            ClusterProtocol.Methods.ClientNotificationBatchDispatch,
            ClusterProtocol.Methods.StartupAffinityLookup,
            ClusterProtocol.Methods.StartupAffinityBind,
            ClusterProtocol.Methods.StartupAffinityCatalogLookup,
            ClusterProtocol.Methods.StartupAffinityRetain,
            ClusterProtocol.Methods.StartupAffinityOwnerSnapshot,
            ClusterProtocol.Methods.MembershipProbe,
            ClusterProtocol.Methods.MembershipGossip
        ];

        Assert.Equal(Enumerable.Range(1, methodIds.Length), methodIds);
    }

    [Fact]
    public void Protocol_identifier_marks_the_membership_table_break()
    {
        Assert.Equal("lakona.cluster.v3", ClusterProtocol.Identifier);
    }
}
