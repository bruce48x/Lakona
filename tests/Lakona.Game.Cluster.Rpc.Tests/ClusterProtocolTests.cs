using Lakona.Game.Cluster.Rpc;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterProtocolTests
{
    [Fact]
    public void Method_constants_preserve_compact_v1_assignments()
    {
        int[] methodIds =
        [
            ClusterProtocol.Methods.ActorAsk,
            ClusterProtocol.Methods.ActorTell,
            ClusterProtocol.Methods.ActorLocationLookup,
            ClusterProtocol.Methods.ActorLocationRegister,
            ClusterProtocol.Methods.ActorLocationUnregister,
            ClusterProtocol.Methods.ActorLocationRegistrySnapshot,
            ClusterProtocol.Methods.ActorLifecycleCreate,
            ClusterProtocol.Methods.ActorLifecycleDestroy,
            ClusterProtocol.Methods.ClientNotificationDispatch,
            ClusterProtocol.Methods.ClientNotificationBatchDispatch,
            ClusterProtocol.Methods.StartupAffinityLookup,
            ClusterProtocol.Methods.StartupAffinityBind,
            ClusterProtocol.Methods.StartupAffinityCatalogLookup,
            ClusterProtocol.Methods.StartupAffinityRetain,
            ClusterProtocol.Methods.StartupAffinityOwnerSnapshot,
            ClusterProtocol.Methods.MembershipFrame
        ];

        Assert.Equal(Enumerable.Range(1, methodIds.Length), methodIds);
    }

    [Fact]
    public void Membership_codec_constants_preserve_frame_and_version_domains()
    {
        byte[] frameIds =
        [
            ClusterProtocol.MembershipFrames.JoinRequest,
            ClusterProtocol.MembershipFrames.JoinResponse,
            ClusterProtocol.MembershipFrames.AppendRequest,
            ClusterProtocol.MembershipFrames.AppendResponse,
            ClusterProtocol.MembershipFrames.VoteRequest,
            ClusterProtocol.MembershipFrames.VoteResponse,
            ClusterProtocol.MembershipFrames.Proof,
            ClusterProtocol.MembershipFrames.ProofResponse,
            ClusterProtocol.MembershipFrames.PromoteRequest,
            ClusterProtocol.MembershipFrames.PromoteResponse,
            ClusterProtocol.MembershipFrames.ReadyRequest,
            ClusterProtocol.MembershipFrames.ReadyResponse,
            ClusterProtocol.MembershipFrames.FormationProbeRequest,
            ClusterProtocol.MembershipFrames.FormationProbeResponse,
            ClusterProtocol.MembershipFrames.FormationAgreementRequest,
            ClusterProtocol.MembershipFrames.FormationAgreementResponse,
            ClusterProtocol.MembershipFrames.SnapshotInstallRequest,
            ClusterProtocol.MembershipFrames.SnapshotInstallResponse,
            ClusterProtocol.MembershipFrames.NotLeaderResponse,
            ClusterProtocol.MembershipFrames.MembershipUnavailableResponse
        ];

        Assert.Equal("lakona.cluster.v1", ClusterProtocol.Identifier);
        Assert.Equal(1, ClusterProtocol.MembershipFrames.Version);
        Assert.Equal(2, ClusterProtocol.MembershipSnapshots.FormatVersion);
        Assert.Equal(
            Enumerable.Range(1, frameIds.Length).Select(static value => (byte)value),
            frameIds);
    }
}
