using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Cluster.Actors;

internal readonly record struct ActorDirectoryPartitionId(NodeReference Owner, int Index);

internal sealed class ActorDirectoryRing
{
    internal const int DefaultPartitionsPerNode = 30;
    private static readonly byte[] ActorHashDomain = Encoding.UTF8.GetBytes("lakona.actor-directory.actor.v1");
    private static readonly byte[] PartitionHashDomain = Encoding.UTF8.GetBytes("lakona.actor-directory.partition.v1");
    private readonly Boundary[] boundaries;
    private readonly Dictionary<ActorDirectoryPartitionId, ActorDirectoryRange> ranges;

    public ActorDirectoryRing(
        ClusterMembershipSnapshot membership,
        int partitionsPerNode = DefaultPartitionsPerNode)
    {
        ArgumentNullException.ThrowIfNull(membership);
        if (partitionsPerNode <= 0) throw new ArgumentOutOfRangeException(nameof(partitionsPerNode));

        Membership = membership;
        PartitionsPerNode = partitionsPerNode;
        var candidates = new List<Boundary>();
        foreach (var member in membership.Members)
        {
            if (member.State != ClusterMemberState.Active) continue;
            for (var index = 0; index < partitionsPerNode; index++)
            {
                candidates.Add(new Boundary(
                    HashPartition(member.Reference, index),
                    new ActorDirectoryPartitionId(member.Reference, index)));
            }
        }

        candidates.Sort(CompareBoundaries);
        ranges = new Dictionary<ActorDirectoryPartitionId, ActorDirectoryRange>(candidates.Count);
        foreach (var candidate in candidates) ranges[candidate.Partition] = ActorDirectoryRange.Empty;

        // A hash collision is vanishingly unlikely, but every node must resolve it identically.
        // The first exact partition in the stable ordering owns the boundary; colliding partitions
        // remain present with an empty range.
        var unique = new List<Boundary>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (unique.Count == 0 || unique[^1].Hash != candidate.Hash)
                unique.Add(candidate);
        }

        boundaries = unique.ToArray();
        if (boundaries.Length == 1)
        {
            ranges[boundaries[0].Partition] = ActorDirectoryRange.Full;
        }
        else
        {
            for (var index = 0; index < boundaries.Length; index++)
            {
                var current = boundaries[index];
                var next = boundaries[(index + 1) % boundaries.Length];
                ranges[current.Partition] = ActorDirectoryRange.Create(current.Hash, next.Hash);
            }
        }
    }

    public ClusterMembershipSnapshot Membership { get; }

    public MembershipViewId View => Membership.View;

    public int PartitionsPerNode { get; }

    public bool IsEmpty => boundaries.Length == 0;

    public ActorDirectoryPartitionId GetOwner(ActorId actorId) => GetOwner(Hash(actorId));

    public ActorDirectoryPartitionId GetOwner(uint hash)
    {
        if (boundaries.Length == 0)
            throw new ActorDirectoryUnavailableException("Actor Directory has no Active owner.");

        var low = 0;
        var high = boundaries.Length - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (boundaries[middle].Hash <= hash)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return boundaries[result >= 0 ? result : boundaries.Length - 1].Partition;
    }

    public ActorDirectoryRange GetRange(ActorDirectoryPartitionId partition) =>
        ranges.TryGetValue(partition, out var range) ? range : ActorDirectoryRange.Empty;

    public IEnumerable<(ActorDirectoryPartitionId Partition, ActorDirectoryRange Range)>
        GetIntersectingPartitions(ActorDirectoryRange range)
    {
        if (range.IsEmpty) yield break;
        foreach (var boundary in boundaries)
        {
            var owned = ranges[boundary.Partition];
            if (owned.Intersects(range)) yield return (boundary.Partition, owned);
        }
    }

    public ClusterMember? FindMember(NodeReference reference) =>
        Membership.Members.SingleOrDefault(member => member.Reference == reference);

    internal static uint Hash(ActorId actorId) => Hash(ActorHashDomain, Encoding.UTF8.GetBytes(actorId.Value));

    internal static uint HashPartition(NodeReference owner, int partitionIndex)
    {
        Span<byte> index = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(index, partitionIndex);
        return Hash(
            PartitionHashDomain,
            owner.Cluster.Value.ToByteArray(),
            Encoding.UTF8.GetBytes(owner.Node.Value),
            owner.Incarnation.Value.ToByteArray(),
            index.ToArray());
    }

    private static uint Hash(params byte[][] fields)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var field in fields)
        {
            BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)field.Length));
            hash.AppendData(length);
            hash.AppendData(field);
        }

        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        return BinaryPrimitives.ReadUInt32BigEndian(digest);
    }

    private static int CompareBoundaries(Boundary left, Boundary right)
    {
        var hash = left.Hash.CompareTo(right.Hash);
        if (hash != 0) return hash;
        var node = string.CompareOrdinal(left.Partition.Owner.Node.Value, right.Partition.Owner.Node.Value);
        if (node != 0) return node;
        var incarnation = left.Partition.Owner.Incarnation.Value.CompareTo(right.Partition.Owner.Incarnation.Value);
        return incarnation != 0 ? incarnation : left.Partition.Index.CompareTo(right.Partition.Index);
    }

    private readonly record struct Boundary(uint Hash, ActorDirectoryPartitionId Partition);
}
