using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster.Membership;

internal sealed class MembershipTableSnapshot
{
    public MembershipTableSnapshot(
        ClusterIncarnationId cluster,
        string? buildTag,
        MembershipViewId version,
        IReadOnlyList<MembershipTableEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var copy = entries.ToArray();
        Array.Sort(copy, CompareEntries);
        Cluster = cluster;
        BuildTag = buildTag;
        Version = version;
        Entries = new ReadOnlyCollection<MembershipTableEntry>(copy);
    }

    public ClusterIncarnationId Cluster { get; }
    public string? BuildTag { get; }
    public MembershipViewId Version { get; }
    public IReadOnlyList<MembershipTableEntry> Entries { get; }

    private static int CompareEntries(MembershipTableEntry left, MembershipTableEntry right)
    {
        var node = string.Compare(left.Reference.Node.Value, right.Reference.Node.Value, StringComparison.Ordinal);
        return node != 0 ? node : left.Reference.Incarnation.Value.CompareTo(right.Reference.Incarnation.Value);
    }
}
