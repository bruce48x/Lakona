using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Cluster.Actors;

internal static class StartupActorAffinityLayout
{
    internal const int ShardCount = 1024;
    private static readonly byte[] ShardDomain = Encoding.UTF8.GetBytes("lakona.startup-affinity.shard.v1");
    private static readonly byte[] OwnerDomain = Encoding.UTF8.GetBytes("lakona.startup-affinity.owner.v1");

    public static int GetShard(ActorId actorId)
    {
        var digest = Hash(ShardDomain, Encoding.UTF8.GetBytes(actorId.Value));
        return (int)(BinaryPrimitives.ReadUInt64BigEndian(digest) & (ShardCount - 1));
    }

    public static NodeReference? GetOwner(int shard, ClusterMembershipSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)shard >= ShardCount) throw new ArgumentOutOfRangeException(nameof(shard));

        NodeReference? winner = null;
        ulong winnerScore = 0;
        foreach (var member in snapshot.Members)
        {
            if (member.State != ClusterMemberState.Active) continue;

            var score = GetOwnerScore(shard, member.Reference.Node);
            if (winner is null
                || score > winnerScore
                || score == winnerScore
                && string.CompareOrdinal(member.Reference.Node.Value, winner.Node.Value) > 0)
            {
                winner = member.Reference;
                winnerScore = score;
            }
        }

        return winner;
    }

    private static ulong GetOwnerScore(int shard, NodeId node)
    {
        Span<byte> shardBytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(shardBytes, checked((ushort)shard));
        var digest = Hash(OwnerDomain, shardBytes.ToArray(), Encoding.UTF8.GetBytes(node.Value));
        return BinaryPrimitives.ReadUInt64BigEndian(digest);
    }

    private static byte[] Hash(params byte[][] fields)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var field in fields)
        {
            BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)field.Length));
            hash.AppendData(length);
            hash.AppendData(field);
        }

        return hash.GetHashAndReset();
    }
}
