using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class MembershipStateMachineTests
{
    [Fact]
    public void OnlyCommittedCommandsPublishTheNextMembershipViewAndApplyOnce()
    {
        var runtime = new ClusterMembershipRuntime();
        runtime.BootstrapNewCluster(
            new NodeId("data-1"),
            new NodeIncarnationId(Guid.Parse("bbbbbbbb-1111-2222-3333-bbbbbbbbbbbb")),
            new NodeEndpoint("tcp://127.0.0.1:21001"));
        IClusterMembership membership = runtime;
        var recovering = membership.Current;
        var local = Assert.Single(recovering.Members).Reference;
        var log = new MembershipReplicatedLog();
        var command = MembershipCommands.SetMemberState(
            local,
            ClusterMemberState.Ready);
        log.AppendFromLeader(new MembershipAppendBatch(
            previousIndex: 0,
            previousTerm: 0,
            leaderCommit: 0,
            new[] { new MembershipLogEntry(1, term: 1, command.Kind, command.Payload) }));
        var stateMachine = new MembershipStateMachine(runtime, log);

        Assert.Equal(0, stateMachine.ApplyCommitted());
        Assert.Same(recovering, membership.Current);

        Assert.True(log.AdvanceLeaderCommit(index: 1, currentTerm: 1));
        Assert.Equal(1, stateMachine.ApplyCommitted());
        Assert.Equal(0, stateMachine.ApplyCommitted());

        var ready = membership.Current;
        Assert.Equal(new MembershipViewId(2), ready.View);
        Assert.Equal(ClusterMemberState.Ready, Assert.Single(ready.Members).State);
        Assert.Equal(local, Assert.Single(ready.Members).Reference);
    }

    [Fact]
    public void InstalledSnapshotRestoresStateBeforeApplyingTheCommittedTail()
    {
        var sourceRuntime = new ClusterMembershipRuntime();
        sourceRuntime.BootstrapNewCluster(
            new NodeId("data-1"),
            new NodeIncarnationId(Guid.Parse("cccccccc-1111-2222-3333-cccccccccccc")),
            new NodeEndpoint(
                "tcp://127.0.0.1:21001",
                new Dictionary<string, string> { ["tls"] = "required" }));
        var sourceMembership = (IClusterMembership)sourceRuntime;
        var local = Assert.Single(sourceMembership.Current.Members).Reference;
        var sourceLog = new MembershipReplicatedLog();
        var readyCommand = MembershipCommands.SetMemberState(
            local,
            ClusterMemberState.Ready);
        sourceLog.AppendFromLeader(new MembershipAppendBatch(
            previousIndex: 0,
            previousTerm: 0,
            leaderCommit: 1,
            new[]
            {
                new MembershipLogEntry(1, term: 1, readyCommand.Kind, readyCommand.Payload)
            }));
        var sourceStateMachine = new MembershipStateMachine(sourceRuntime, sourceLog);
        Assert.Equal(1, sourceStateMachine.ApplyCommitted());
        var snapshot = MembershipSnapshotCodec.Create(
            lastIncludedIndex: 1,
            lastIncludedTerm: 1,
            sourceMembership.Current);

        var restoredRuntime = new ClusterMembershipRuntime();
        var restoredLog = new MembershipReplicatedLog();
        Assert.Equal(
            MembershipSnapshotInstallStatus.Installed,
            restoredLog.InstallSnapshot(snapshot));
        var readyAgainCommand = MembershipCommands.SetMemberState(
            local,
            ClusterMemberState.Ready);
        Assert.Equal(
            MembershipAppendStatus.Accepted,
            restoredLog.AppendFromLeader(new MembershipAppendBatch(
                previousIndex: 1,
                previousTerm: 1,
                leaderCommit: 2,
                new[]
                {
                    new MembershipLogEntry(
                        2,
                        term: 2,
                        readyAgainCommand.Kind,
                        readyAgainCommand.Payload)
                })).Status);
        var restoredStateMachine = new MembershipStateMachine(restoredRuntime, restoredLog);

        Assert.Equal(1, restoredStateMachine.ApplyCommitted());

        var restored = ((IClusterMembership)restoredRuntime).Current;
        var member = Assert.Single(restored.Members);
        Assert.Equal(new MembershipViewId(3), restored.View);
        Assert.Equal(ClusterMemberState.Ready, member.State);
        Assert.Equal("required", member.ClusterEndpoint.Metadata["tls"]);
    }
}
