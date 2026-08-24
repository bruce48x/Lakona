using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster.Membership;

internal sealed class MembershipTableSnapshot
{
    public MembershipTableSnapshot(string clusterId, ClusterIncarnationId cluster, MembershipViewId version, IReadOnlyList<MembershipTableEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
        {
            throw new ArgumentException("Cluster id is required.", nameof(clusterId));
        }

        ArgumentNullException.ThrowIfNull(entries);
        var copy = entries.ToArray();
        Array.Sort(copy, CompareEntries);
        ClusterId = clusterId;
        Cluster = cluster;
        Version = version;
        Entries = new ReadOnlyCollection<MembershipTableEntry>(copy);
    }

    public string ClusterId { get; }
    public ClusterIncarnationId Cluster { get; }
    public MembershipViewId Version { get; }
    public IReadOnlyList<MembershipTableEntry> Entries { get; }

    private static int CompareEntries(MembershipTableEntry left, MembershipTableEntry right)
    {
        var node = string.Compare(left.Reference.Node.Value, right.Reference.Node.Value, StringComparison.Ordinal);
        return node != 0 ? node : left.Reference.Incarnation.Value.CompareTo(right.Reference.Incarnation.Value);
    }
}
