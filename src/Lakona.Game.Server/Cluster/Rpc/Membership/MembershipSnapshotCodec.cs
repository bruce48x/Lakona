using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Lakona.Game.Cluster;
using static Lakona.Game.Cluster.Rpc.Membership.MembershipBinaryCodec;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal static class MembershipSnapshotCodec
    {
        private const byte FormatVersion = ClusterProtocol.MembershipSnapshots.FormatVersion;
        private const int MaximumMembers = ClusterMembershipSnapshot.MaximumMembersV1;
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
                WriteGuid(writer, snapshot.Cluster.Value);
                writer.Write(snapshot.View.Value);
                writer.Write(snapshot.Members.Count);
                for (var i = 0; i < snapshot.Members.Count; i++)
                {
                    var member = snapshot.Members[i];
                    WriteString(writer, member.Reference.Node.Value);
                    WriteGuid(writer, member.Reference.Incarnation.Value);
                    writer.Write((byte)member.State);
                    writer.Write(member.IsVoter);
                    WriteString(writer, member.ClusterEndpoint.Address);
                    WriteMap(writer, member.ClusterEndpoint.Metadata, deterministic: true);
                    WriteMap(writer, member.Labels, deterministic: true);
                    writer.Write(member.ActorHosts.Count);
                    for (var actorHostIndex = 0;
                        actorHostIndex < member.ActorHosts.Count;
                        actorHostIndex++)
                    {
                        var actorHost = member.ActorHosts[actorHostIndex];
                        WriteString(writer, actorHost.Actor);
                        WriteString(writer, actorHost.PolicyHash);
                        WriteString(writer, actorHost.BuildTag);
                        WriteMap(writer, actorHost.Metadata, deterministic: true);
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
                        WriteMap(writer, startup.Metadata, deterministic: true);
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
                using var stream = new MemoryStream(payload.ToArray(), writable: false);
                using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
                if (reader.ReadByte() != FormatVersion)
                {
                    throw InvalidEncoding("unknown format version");
                }

                var cluster = new ClusterIncarnationId(ReadGuid(reader));
                var view = new MembershipViewId(reader.ReadInt64());
                var memberCount = ReadCount(reader, MaximumMembers, "member");
                var members = new List<ClusterMember>(memberCount);
                for (var i = 0; i < memberCount; i++)
                {
                    var node = new NodeId(ReadString(reader));
                    var incarnation = new NodeIncarnationId(ReadGuid(reader));
                    var state = (ClusterMemberState)reader.ReadByte();
                    if (!Enum.IsDefined(typeof(ClusterMemberState), state))
                    {
                        throw InvalidEncoding("unknown member state");
                    }

                    var isVoter = reader.ReadByte() switch
                    {
                        0 => false,
                        1 => true,
                        _ => throw InvalidEncoding("invalid voter flag")
                    };
                    var endpointAddress = ReadString(reader);
                    var endpointMetadata = ReadMap(reader);
                    var labels = ReadMap(reader);
                    var actorHostCount = ReadCount(
                        reader,
                        256,
                        "Actor host descriptor");
                    var actorHosts = new List<NodeActorHostDescriptor>(actorHostCount);
                    for (var actorHostIndex = 0;
                        actorHostIndex < actorHostCount;
                        actorHostIndex++)
                    {
                        actorHosts.Add(new NodeActorHostDescriptor(
                            ReadString(reader),
                            ReadString(reader),
                            ReadString(reader),
                            ReadMap(reader)));
                    }

                    var startupCount = ReadCount(
                        reader,
                        256,
                        "Startup Actor descriptor");
                    var startupActors = new List<StartupActorDescriptor>(startupCount);
                    for (var startupIndex = 0;
                        startupIndex < startupCount;
                        startupIndex++)
                    {
                        startupActors.Add(new StartupActorDescriptor(
                            ReadString(reader),
                            ReadString(reader),
                            ReadString(reader),
                            ReadMap(reader)));
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

                EnsureEnd(stream);

                return new ClusterMembershipSnapshot(cluster, view, members);
            }
            catch (TerminalMembershipException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is OverflowException
                || exception is DecoderFallbackException
                || exception is EndOfStreamException
                || exception is InvalidDataException)
            {
                throw new TerminalMembershipException(
                    "Committed membership snapshot has an invalid encoding.",
                    exception);
            }
        }

        private static TerminalMembershipException InvalidEncoding(string reason)
        {
            return new TerminalMembershipException(
                $"Committed membership snapshot has an invalid encoding: {reason}.");
        }
    }
}
