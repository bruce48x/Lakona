namespace Lakona.Game.Cluster.Membership;

internal sealed class MembershipTableSnapshotProjection
{
    public MembershipTableSnapshotProjection(MembershipTableSnapshot table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var members = table.Entries
            .Where(static entry => entry.Status is MembershipTableStatus.Joining or MembershipTableStatus.Active)
            .Select(static entry => new ClusterMember(
                entry.Reference,
                entry.Status == MembershipTableStatus.Active ? ClusterMemberState.Active : ClusterMemberState.Joining,
                entry.ClusterEndpoint,
                entry.Labels,
                entry.ActorHosts,
                entry.StartupActors))
            .ToArray();
        Snapshot = new ClusterMembershipSnapshot(table.Cluster, table.Version, members);
    }

    public ClusterMembershipSnapshot Snapshot { get; }
}
