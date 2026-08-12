using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal static class MembershipSnapshotCodec
    {
        private const byte FormatVersion = 2;
        private const int MaximumMembers = ClusterMembershipSnapshot.MaximumMembersV1;
        private const int MaximumMapEntries = 256;
        private const int MaximumStringBytes = 64 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static MembershipLogSnapshot Create(
            long lastIncludedIndex,
            long lastIncludedTerm,
            ClusterMembershipSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var payload = Encode(snapshot);
            return new MembershipLogSnapshot(
                lastIncludedIndex,
                lastIncludedTerm,
                payload,
                SHA256.HashData(payload));
        }

        public static byte[] Encode(ClusterMembershipSnapshot snapshot)
        {
            if (snapshot.Members.Count > MaximumMembers)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(snapshot),
                    $"Membership snapshots cannot exceed {MaximumMembers} members.");
            }

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true))
            {
                writer.Write(FormatVersion);
                writer.Write(snapshot.Cluster.Value.ToByteArray());
                writer.Write(snapshot.View.Value);
                writer.Write(snapshot.Members.Count);
                for (var i = 0; i < snapshot.Members.Count; i++)
                {
                    var member = snapshot.Members[i];
                    WriteString(writer, member.Reference.Node.Value);
                    writer.Write(member.Reference.Incarnation.Value.ToByteArray());
                    writer.Write((byte)member.State);
                    writer.Write(member.IsVoter);
                    WriteString(writer, member.ClusterEndpoint.Address);
                    WriteMap(writer, member.ClusterEndpoint.Metadata);
                    WriteMap(writer, member.Labels);
                    writer.Write(member.ActorHosts.Count);
                    for (var actorHostIndex = 0;
                        actorHostIndex < member.ActorHosts.Count;
                        actorHostIndex++)
                    {
                        var actorHost = member.ActorHosts[actorHostIndex];
                        WriteString(writer, actorHost.Actor);
                        WriteString(writer, actorHost.PolicyHash);
                        WriteString(writer, actorHost.BuildTag);
                        WriteMap(writer, actorHost.Metadata);
                    }

                    writer.Write(member.StartupActors.Count);
                    for (var startupIndex = 0;
                        startupIndex < member.StartupActors.Count;
                        startupIndex++)
                    {
                        var startup = member.StartupActors[startupIndex];
                        WriteString(writer, startup.Actor);
                        WriteString(writer, startup.PolicyHash);
                        WriteString(writer, startup.BuildTag);
                        WriteMap(writer, startup.Metadata);
                    }
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        public static ClusterMembershipSnapshot Decode(ReadOnlySpan<byte> payload)
        {
            try
            {
                var offset = 0;
                if (ReadByte(payload, ref offset) != FormatVersion)
                {
                    throw InvalidEncoding("unknown format version");
                }

                var cluster = new ClusterIncarnationId(ReadGuid(payload, ref offset));
                var view = new MembershipViewId(ReadInt64(payload, ref offset));
                var memberCount = ReadCount(payload, ref offset, MaximumMembers, "member");
                var members = new List<ClusterMember>(memberCount);
                for (var i = 0; i < memberCount; i++)
                {
                    var node = new NodeId(ReadString(payload, ref offset));
                    var incarnation = new NodeIncarnationId(ReadGuid(payload, ref offset));
                    var state = (ClusterMemberState)ReadByte(payload, ref offset);
                    if (!Enum.IsDefined(typeof(ClusterMemberState), state))
                    {
                        throw InvalidEncoding("unknown member state");
                    }

                    var isVoter = ReadByte(payload, ref offset) switch
                    {
                        0 => false,
                        1 => true,
                        _ => throw InvalidEncoding("invalid voter flag")
                    };
                    var endpointAddress = ReadString(payload, ref offset);
                    var endpointMetadata = ReadMap(payload, ref offset);
                    var labels = ReadMap(payload, ref offset);
                    var actorHostCount = ReadCount(
                        payload,
                        ref offset,
                        256,
                        "Actor host descriptor");
                    var actorHosts = new List<NodeActorHostDescriptor>(actorHostCount);
                    for (var actorHostIndex = 0;
                        actorHostIndex < actorHostCount;
                        actorHostIndex++)
                    {
                        actorHosts.Add(new NodeActorHostDescriptor(
                            ReadString(payload, ref offset),
                            ReadString(payload, ref offset),
                            ReadString(payload, ref offset),
                            ReadMap(payload, ref offset)));
                    }

                    var startupCount = ReadCount(
                        payload,
                        ref offset,
                        256,
                        "Startup Actor descriptor");
                    var startupActors = new List<StartupActorDescriptor>(startupCount);
                    for (var startupIndex = 0;
                        startupIndex < startupCount;
                        startupIndex++)
                    {
                        startupActors.Add(new StartupActorDescriptor(
                            ReadString(payload, ref offset),
                            ReadString(payload, ref offset),
                            ReadString(payload, ref offset),
                            ReadMap(payload, ref offset)));
                    }

                    members.Add(new ClusterMember(
                        new NodeReference(cluster, node, incarnation),
                        state,
                        new NodeEndpoint(endpointAddress, endpointMetadata),
                        isVoter,
                        labels,
                        actorHosts,
                        startupActors));
                }

                if (offset != payload.Length)
                {
                    throw InvalidEncoding("trailing bytes");
                }

                return new ClusterMembershipSnapshot(cluster, view, members);
            }
            catch (TerminalMembershipException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is OverflowException
                || exception is DecoderFallbackException)
            {
                throw new TerminalMembershipException(
                    "Committed membership snapshot has an invalid encoding.",
                    exception);
            }
        }

        private static void WriteMap(
            BinaryWriter writer,
            IReadOnlyDictionary<string, string> values)
        {
            if (values.Count > MaximumMapEntries)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(values),
                    $"Snapshot maps cannot exceed {MaximumMapEntries} entries.");
            }

            writer.Write(values.Count);
            foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                WriteString(writer, pair.Key);
                WriteString(writer, pair.Value);
            }
        }

        private static Dictionary<string, string> ReadMap(
            ReadOnlySpan<byte> payload,
            ref int offset)
        {
            var count = ReadCount(payload, ref offset, MaximumMapEntries, "map");
            var values = new Dictionary<string, string>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                if (!values.TryAdd(
                    ReadString(payload, ref offset),
                    ReadString(payload, ref offset)))
                {
                    throw InvalidEncoding("duplicate map key");
                }
            }

            return values;
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Utf8.GetBytes(value);
            if (bytes.Length > MaximumStringBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Snapshot strings cannot exceed {MaximumStringBytes} UTF-8 bytes.");
            }

            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadString(ReadOnlySpan<byte> payload, ref int offset)
        {
            return Utf8.GetString(ReadBytes(
                payload,
                ref offset,
                MaximumStringBytes,
                "string"));
        }

        private static byte[] ReadBytes(
            ReadOnlySpan<byte> payload,
            ref int offset,
            int maximumLength,
            string field)
        {
            var length = ReadInt32(payload, ref offset);
            if (length < 0 || length > maximumLength || length > payload.Length - offset)
            {
                throw InvalidEncoding($"invalid {field} length");
            }

            var value = payload.Slice(offset, length).ToArray();
            offset += length;
            return value;
        }

        private static int ReadCount(
            ReadOnlySpan<byte> payload,
            ref int offset,
            int maximum,
            string field)
        {
            var count = ReadInt32(payload, ref offset);
            if (count < 0 || count > maximum)
            {
                throw InvalidEncoding($"invalid {field} count");
            }

            return count;
        }

        private static Guid ReadGuid(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureAvailable(payload, offset, 16);
            var value = new Guid(payload.Slice(offset, 16));
            offset += 16;
            return value;
        }

        private static long ReadInt64(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureAvailable(payload, offset, sizeof(long));
            var value = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(offset, sizeof(long)));
            offset += sizeof(long);
            return value;
        }

        private static int ReadInt32(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureAvailable(payload, offset, sizeof(int));
            var value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, sizeof(int)));
            offset += sizeof(int);
            return value;
        }

        private static byte ReadByte(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureAvailable(payload, offset, 1);
            return payload[offset++];
        }

        private static void EnsureAvailable(
            ReadOnlySpan<byte> payload,
            int offset,
            int length)
        {
            if (offset < 0 || length < 0 || length > payload.Length - offset)
            {
                throw InvalidEncoding("truncated payload");
            }
        }

        private static TerminalMembershipException InvalidEncoding(string reason)
        {
            return new TerminalMembershipException(
                $"Committed membership snapshot has an invalid encoding: {reason}.");
        }
    }
}
