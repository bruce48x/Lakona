using System.Security.Cryptography;
using System.Text;

namespace Lakona.Game.Cluster.Membership;

internal static class MembershipProbeTargetSelector
{
    public static IReadOnlyList<ClusterMember> Select(
        ClusterMembershipSnapshot snapshot,
        NodeReference local,
        int targetCount)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(local);
        if (targetCount < 0) throw new ArgumentOutOfRangeException(nameof(targetCount));

        var ring = snapshot.Members
            .Where(static member => member.State == ClusterMemberState.Active)
            .Select(static member => new RingMember(member, Hash(member.Reference)))
            .OrderBy(static member => member.Hash, ByteArrayComparer.Instance)
            .ThenBy(static member => member.Member.Reference.Node.Value, StringComparer.Ordinal)
            .ToArray();
        var localIndex = Array.FindIndex(ring, member => member.Member.Reference == local);
        if (localIndex < 0 || ring.Length <= 1 || targetCount == 0)
        {
            return Array.Empty<ClusterMember>();
        }

        var count = Math.Min(targetCount, ring.Length - 1);
        var result = new ClusterMember[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = ring[(localIndex + index + 1) % ring.Length].Member;
        }

        return result;
    }

    private static byte[] Hash(NodeReference reference) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{reference.Node.Value}\0{reference.Incarnation.Value:N}"));

    private sealed record RingMember(ClusterMember Member, byte[] Hash);

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public int Compare(byte[]? left, byte[]? right) =>
            left.AsSpan().SequenceCompareTo(right);
    }
}
