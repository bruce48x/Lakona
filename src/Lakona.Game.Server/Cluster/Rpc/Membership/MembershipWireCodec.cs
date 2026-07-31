using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal static class MembershipWireCodec
    {
        private const byte Version = 1;
        private const byte JoinRequestKind = 1;
        private const byte JoinResponseKind = 2;
        private const byte AppendRequestKind = 3;
        private const byte AppendResponseKind = 4;
        private const byte VoteRequestKind = 5;
        private const byte VoteResponseKind = 6;
        private const byte ProofKind = 7;
        private const byte ProofResponseKind = 8;
        private const byte PromoteRequestKind = 9;
        private const byte PromoteResponseKind = 10;
        private const byte ReadyRequestKind = 11;
        private const byte ReadyResponseKind = 12;
        private const byte FormationProbeRequestKind = 13;
        private const byte FormationProbeResponseKind = 14;
        private const byte FormationAgreementRequestKind = 15;
        private const byte FormationAgreementResponseKind = 16;
        private const byte SnapshotInstallRequestKind = 17;
        private const byte SnapshotInstallResponseKind = 18;
        private const int MaximumStringBytes = 64 * 1024;
        private const int MaximumMetadataEntries = 256;
        private const int MaximumFormationPeers = 256;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static ClusterMembershipTransportFrame EncodeJoinRequest(
            NodeId node,
            NodeIncarnationId incarnation,
            NodeEndpoint endpoint)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, JoinRequestKind);
            WriteString(writer, node.Value);
            writer.Write(incarnation.Value.ToByteArray());
            WriteEndpoint(writer, endpoint);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static JoinRequest DecodeJoinRequest(ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, JoinRequestKind);
            var node = new NodeId(ReadString(reader));
            var incarnation = new NodeIncarnationId(ReadGuid(reader));
            var endpoint = ReadEndpoint(reader);
            EnsureEnd(stream);
            return new JoinRequest(node, incarnation, endpoint);
        }

        public static ClusterMembershipTransportFrame EncodeJoinResponse(
            NodeReference local,
            ClusterMembershipTransfer transfer)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, JoinResponseKind);
            writer.Write(local.Cluster.Value.ToByteArray());
            WriteString(writer, local.Node.Value);
            writer.Write(local.Incarnation.Value.ToByteArray());
            writer.Write(transfer.LastIncludedIndex);
            writer.Write(transfer.LastIncludedTerm);
            WriteBytes(writer, transfer.Payload);
            WriteBytes(writer, transfer.Checksum);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static JoinResponse DecodeJoinResponse(ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, JoinResponseKind);
            var local = new NodeReference(
                new ClusterIncarnationId(ReadGuid(reader)),
                new NodeId(ReadString(reader)),
                new NodeIncarnationId(ReadGuid(reader)));
            var index = reader.ReadInt64();
            var term = reader.ReadInt64();
            var payload = ReadBytes(reader);
            var checksum = ReadBytes(reader);
            EnsureEnd(stream);
            return new JoinResponse(
                local,
                new ClusterMembershipTransfer(index, term, payload, checksum));
        }

        public static byte GetKind(ClusterMembershipTransportFrame frame)
        {
            var span = frame.Payload.Span;
            if (span.Length < 2 || span[0] != Version)
            {
                throw new InvalidDataException("Unsupported membership protocol frame.");
            }

            return span[1];
        }

        public static bool IsJoinRequest(ClusterMembershipTransportFrame frame)
        {
            return GetKind(frame) == JoinRequestKind;
        }

        public static bool IsAppendRequest(ClusterMembershipTransportFrame frame) =>
            GetKind(frame) == AppendRequestKind;

        public static bool IsVoteRequest(ClusterMembershipTransportFrame frame) =>
            GetKind(frame) == VoteRequestKind;

        public static bool IsProof(ClusterMembershipTransportFrame frame) =>
            GetKind(frame) == ProofKind;

        public static bool IsPromoteRequest(ClusterMembershipTransportFrame frame) =>
            GetKind(frame) == PromoteRequestKind;

        public static bool IsReadyRequest(ClusterMembershipTransportFrame frame) =>
            GetKind(frame) == ReadyRequestKind;

        public static bool IsFormationProbeRequest(ClusterMembershipTransportFrame frame) =>
            GetKind(frame) == FormationProbeRequestKind;

        public static bool IsFormationAgreementRequest(ClusterMembershipTransportFrame frame) =>
            GetKind(frame) == FormationAgreementRequestKind;

        public static bool IsSnapshotInstallRequest(ClusterMembershipTransportFrame frame) =>
            GetKind(frame) == SnapshotInstallRequestKind;

        public static ClusterMembershipTransportFrame EncodeFormationProbeRequest(
            IReadOnlyList<ClusterFormationPeer> peers)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, FormationProbeRequestKind);
            WriteFormationPeers(writer, peers);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static IReadOnlyList<ClusterFormationPeer> DecodeFormationProbeRequest(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, FormationProbeRequestKind);
            var peers = ReadFormationPeers(reader);
            EnsureEnd(stream);
            return peers;
        }

        public static ClusterMembershipTransportFrame EncodeFormationProbeResponse(
            bool established,
            IReadOnlyList<ClusterFormationPeer> peers)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, FormationProbeResponseKind);
            writer.Write(established);
            WriteFormationPeers(writer, peers);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static FormationProbeResponse DecodeFormationProbeResponse(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, FormationProbeResponseKind);
            var response = new FormationProbeResponse(
                reader.ReadBoolean(),
                ReadFormationPeers(reader));
            EnsureEnd(stream);
            return response;
        }

        public static ClusterMembershipTransportFrame EncodeFormationAgreementRequest(
            string digest,
            IReadOnlyList<ClusterFormationPeer> peers)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, FormationAgreementRequestKind);
            WriteString(writer, digest);
            WriteFormationPeers(writer, peers);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static FormationAgreementRequest DecodeFormationAgreementRequest(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, FormationAgreementRequestKind);
            var request = new FormationAgreementRequest(
                ReadString(reader),
                ReadFormationPeers(reader));
            EnsureEnd(stream);
            return request;
        }

        public static ClusterMembershipTransportFrame EncodeFormationAgreementResponse(
            bool established,
            bool accepted,
            IReadOnlyList<ClusterFormationPeer> peers)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, FormationAgreementResponseKind);
            writer.Write(established);
            writer.Write(accepted);
            WriteFormationPeers(writer, peers);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static FormationAgreementResponse DecodeFormationAgreementResponse(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, FormationAgreementResponseKind);
            var response = new FormationAgreementResponse(
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                ReadFormationPeers(reader));
            EnsureEnd(stream);
            return response;
        }

        public static ClusterMembershipTransportFrame EncodeAppendRequest(
            MembershipAppendRequest request)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, AppendRequestKind);
            WriteReference(writer, request.Source);
            WriteReference(writer, request.Target);
            writer.Write(request.Term);
            writer.Write(request.View.Value);
            writer.Write(request.Sequence);
            writer.Write(request.Batch.PreviousIndex);
            writer.Write(request.Batch.PreviousTerm);
            writer.Write(request.Batch.LeaderCommit);
            writer.Write(request.Batch.Entries.Count);
            for (var i = 0; i < request.Batch.Entries.Count; i++)
            {
                var entry = request.Batch.Entries[i];
                writer.Write(entry.Index);
                writer.Write(entry.Term);
                WriteString(writer, entry.CommandKind);
                WriteBytes(writer, entry.Payload);
            }

            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static MembershipAppendRequest DecodeAppendRequest(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, AppendRequestKind);
            var source = ReadReference(reader);
            var target = ReadReference(reader);
            var term = reader.ReadInt64();
            var view = new MembershipViewId(reader.ReadInt64());
            var sequence = reader.ReadInt64();
            var previousIndex = reader.ReadInt64();
            var previousTerm = reader.ReadInt64();
            var leaderCommit = reader.ReadInt64();
            var count = reader.ReadInt32();
            if (count < 0 || count > 64)
            {
                throw new InvalidDataException("Invalid membership append entry count.");
            }

            var entries = new List<MembershipLogEntry>(count);
            for (var i = 0; i < count; i++)
            {
                entries.Add(new MembershipLogEntry(
                    reader.ReadInt64(),
                    reader.ReadInt64(),
                    ReadString(reader),
                    ReadBytes(reader)));
            }

            EnsureEnd(stream);
            return new MembershipAppendRequest(
                source,
                target,
                term,
                view,
                sequence,
                new MembershipAppendBatch(previousIndex, previousTerm, leaderCommit, entries));
        }

        public static ClusterMembershipTransportFrame EncodeAppendResponse(
            MembershipAppendRequest request,
            MembershipAppendReceiveResult result,
            MembershipViewId currentView)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, AppendResponseKind);
            WriteReference(writer, request.Target);
            WriteReference(writer, request.Source);
            writer.Write(result.Term);
            writer.Write(currentView.Value);
            writer.Write(request.Sequence);
            writer.Write(result.Status == MembershipAppendReceiveStatus.Accepted);
            writer.Write(result.MatchIndex);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static MembershipAppendReply DecodeAppendResponse(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, AppendResponseKind);
            var source = ReadReference(reader);
            var target = ReadReference(reader);
            var term = reader.ReadInt64();
            var view = new MembershipViewId(reader.ReadInt64());
            var sequence = reader.ReadInt64();
            var accepted = reader.ReadBoolean();
            var matchIndex = reader.ReadInt64();
            EnsureEnd(stream);
            return new MembershipAppendReply(
                source, target, term, view, sequence, accepted, matchIndex);
        }

        public static ClusterMembershipTransportFrame EncodeSnapshotInstallRequest(
            MembershipSnapshotInstallRequest request)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, SnapshotInstallRequestKind);
            WriteReference(writer, request.Source);
            WriteReference(writer, request.Target);
            writer.Write(request.Term);
            writer.Write(request.View.Value);
            writer.Write(request.Sequence);
            writer.Write(request.Transfer.LastIncludedIndex);
            writer.Write(request.Transfer.LastIncludedTerm);
            WriteBytes(writer, request.Transfer.Payload);
            WriteBytes(writer, request.Transfer.Checksum);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static MembershipSnapshotInstallRequest DecodeSnapshotInstallRequest(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, SnapshotInstallRequestKind);
            var request = new MembershipSnapshotInstallRequest(
                ReadReference(reader),
                ReadReference(reader),
                reader.ReadInt64(),
                new MembershipViewId(reader.ReadInt64()),
                reader.ReadInt64(),
                new ClusterMembershipTransfer(
                    reader.ReadInt64(),
                    reader.ReadInt64(),
                    ReadBytes(reader),
                    ReadBytes(reader)));
            EnsureEnd(stream);
            return request;
        }

        public static ClusterMembershipTransportFrame EncodeSnapshotInstallResponse(
            MembershipSnapshotInstallRequest request,
            MembershipAppendReceiveResult result,
            MembershipViewId currentView)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, SnapshotInstallResponseKind);
            WriteReference(writer, request.Target);
            WriteReference(writer, request.Source);
            writer.Write(result.Term);
            writer.Write(currentView.Value);
            writer.Write(request.Sequence);
            writer.Write(result.Status == MembershipAppendReceiveStatus.Accepted);
            writer.Write(result.MatchIndex);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static MembershipAppendReply DecodeSnapshotInstallResponse(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, SnapshotInstallResponseKind);
            var reply = new MembershipAppendReply(
                ReadReference(reader),
                ReadReference(reader),
                reader.ReadInt64(),
                new MembershipViewId(reader.ReadInt64()),
                reader.ReadInt64(),
                reader.ReadBoolean(),
                reader.ReadInt64());
            EnsureEnd(stream);
            return reply;
        }

        public static ClusterMembershipTransportFrame EncodeVoteRequest(
            MembershipVoteRequest request)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, VoteRequestKind);
            WriteReference(writer, request.Source);
            WriteReference(writer, request.Target);
            writer.Write(request.Term);
            writer.Write(request.View.Value);
            writer.Write(request.LastLogIndex);
            writer.Write(request.LastLogTerm);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static MembershipVoteRequest DecodeVoteRequest(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, VoteRequestKind);
            var request = new MembershipVoteRequest(
                ReadReference(reader),
                ReadReference(reader),
                reader.ReadInt64(),
                new MembershipViewId(reader.ReadInt64()),
                reader.ReadInt64(),
                reader.ReadInt64());
            EnsureEnd(stream);
            return request;
        }

        public static ClusterMembershipTransportFrame EncodeVoteResponse(
            MembershipVoteRequest request,
            MembershipVoteResponse response)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, VoteResponseKind);
            WriteReference(writer, request.Target);
            WriteReference(writer, request.Source);
            writer.Write(response.Term);
            writer.Write(request.View.Value);
            writer.Write(response.Granted);
            writer.Write(checked((byte)response.Rejection));
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static MembershipVoteReply DecodeVoteResponse(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, VoteResponseKind);
            var reply = new MembershipVoteReply(
                ReadReference(reader),
                ReadReference(reader),
                reader.ReadInt64(),
                new MembershipViewId(reader.ReadInt64()),
                reader.ReadBoolean(),
                ReadVoteRejection(reader));
            EnsureEnd(stream);
            return reply;
        }

        private static MembershipVoteRejection ReadVoteRejection(BinaryReader reader)
        {
            var value = (MembershipVoteRejection)reader.ReadByte();
            if (!Enum.IsDefined(value))
            {
                throw new InvalidDataException(
                    $"Unknown membership vote rejection value '{value}'.");
            }

            return value;
        }

        public static ClusterMembershipTransportFrame EncodeProof(QuorumProof proof)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, ProofKind);
            writer.Write(proof.Cluster.Value.ToByteArray());
            writer.Write(proof.Term);
            writer.Write(proof.View.Value);
            writer.Write(proof.Sequence);
            writer.Write(proof.ValidFor.Ticks);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static QuorumProof DecodeProof(ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, ProofKind);
            var proof = new QuorumProof(
                new ClusterIncarnationId(ReadGuid(reader)),
                reader.ReadInt64(),
                new MembershipViewId(reader.ReadInt64()),
                reader.ReadInt64(),
                TimeSpan.FromTicks(reader.ReadInt64()));
            EnsureEnd(stream);
            return proof;
        }

        public static ClusterMembershipTransportFrame EncodeProofResponse(bool accepted)
        {
            return new ClusterMembershipTransportFrame(new byte[]
            {
                Version,
                ProofResponseKind,
                accepted ? (byte)1 : (byte)0
            });
        }

        public static bool DecodeProofResponse(ClusterMembershipTransportFrame frame)
        {
            var span = frame.Payload.Span;
            if (span.Length != 3
                || span[0] != Version
                || span[1] != ProofResponseKind
                || span[2] > 1)
            {
                throw new InvalidDataException("Invalid quorum-proof response frame.");
            }

            return span[2] == 1;
        }

        public static ClusterMembershipTransportFrame EncodePromoteRequest(
            NodeReference learner,
            MembershipViewId learnerView,
            long learnerMatchIndex)
        {
            if (learnerMatchIndex <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(learnerMatchIndex));
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, PromoteRequestKind);
            WriteReference(writer, learner);
            writer.Write(learnerView.Value);
            writer.Write(learnerMatchIndex);
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static (NodeReference Learner, MembershipViewId View, long MatchIndex)
            DecodePromoteRequest(ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, PromoteRequestKind);
            var learner = ReadReference(reader);
            var view = new MembershipViewId(reader.ReadInt64());
            var matchIndex = reader.ReadInt64();
            if (matchIndex <= 0)
            {
                throw new InvalidDataException("Invalid learner promotion match index.");
            }

            EnsureEnd(stream);
            return (learner, view, matchIndex);
        }

        public static ClusterMembershipTransportFrame EncodePromoteResponse(
            ClusterMembershipSnapshot snapshot)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, PromoteResponseKind);
            WriteBytes(writer, MembershipSnapshotCodec.Encode(snapshot));
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static ClusterMembershipSnapshot DecodePromoteResponse(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, PromoteResponseKind);
            var snapshot = MembershipSnapshotCodec.Decode(ReadBytes(reader));
            EnsureEnd(stream);
            return snapshot;
        }

        public static ClusterMembershipTransportFrame EncodeReadyRequest(ClusterMember descriptor)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, ReadyRequestKind);
            WriteBytes(writer, MembershipSnapshotCodec.Encode(
                new ClusterMembershipSnapshot(
                    descriptor.Reference.Cluster,
                    new MembershipViewId(1),
                    new[] { descriptor })));
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static ClusterMember DecodeReadyRequest(ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, ReadyRequestKind);
            var descriptor = MembershipSnapshotCodec.Decode(ReadBytes(reader));
            EnsureEnd(stream);
            if (descriptor.Members.Count != 1)
            {
                throw new InvalidDataException("Ready request must contain exactly one member descriptor.");
            }

            return descriptor.Members[0];
        }

        public static ClusterMembershipTransportFrame EncodeReadyResponse(
            ClusterMembershipSnapshot snapshot)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, ReadyResponseKind);
            WriteBytes(writer, MembershipSnapshotCodec.Encode(snapshot));
            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static ClusterMembershipSnapshot DecodeReadyResponse(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, ReadyResponseKind);
            var snapshot = MembershipSnapshotCodec.Decode(ReadBytes(reader));
            EnsureEnd(stream);
            return snapshot;
        }

        private static MemoryStream CreateReadStream(ClusterMembershipTransportFrame frame)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            return new MemoryStream(frame.Payload.ToArray(), writable: false);
        }

        private static void WriteHeader(BinaryWriter writer, byte kind)
        {
            writer.Write(Version);
            writer.Write(kind);
        }

        private static void ReadHeader(BinaryReader reader, byte expectedKind)
        {
            var version = reader.ReadByte();
            var kind = reader.ReadByte();
            if (version != Version || kind != expectedKind)
            {
                throw new InvalidDataException("Unsupported membership protocol frame.");
            }
        }

        private static void WriteEndpoint(BinaryWriter writer, NodeEndpoint endpoint)
        {
            WriteString(writer, endpoint.Address);
            if (endpoint.Metadata.Count > MaximumMetadataEntries)
            {
                throw new ArgumentOutOfRangeException(nameof(endpoint));
            }

            writer.Write(endpoint.Metadata.Count);
            foreach (var pair in endpoint.Metadata)
            {
                WriteString(writer, pair.Key);
                WriteString(writer, pair.Value);
            }
        }

        private static NodeEndpoint ReadEndpoint(BinaryReader reader)
        {
            var address = ReadString(reader);
            var count = reader.ReadInt32();
            if (count < 0 || count > MaximumMetadataEntries)
            {
                throw new InvalidDataException("Invalid membership endpoint metadata count.");
            }

            var metadata = new Dictionary<string, string>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                metadata.Add(ReadString(reader), ReadString(reader));
            }

            return new NodeEndpoint(address, metadata);
        }

        private static void WriteFormationPeers(
            BinaryWriter writer,
            IReadOnlyList<ClusterFormationPeer> peers)
        {
            if (peers is null || peers.Count == 0 || peers.Count > MaximumFormationPeers)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(peers),
                    $"Formation views must contain 1 to {MaximumFormationPeers} peers.");
            }

            writer.Write(peers.Count);
            for (var i = 0; i < peers.Count; i++)
            {
                WriteString(writer, peers[i].Node.Value);
                WriteEndpoint(writer, peers[i].Endpoint);
            }
        }

        private static IReadOnlyList<ClusterFormationPeer> ReadFormationPeers(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            if (count <= 0 || count > MaximumFormationPeers)
            {
                throw new InvalidDataException("Invalid formation peer count.");
            }

            var peers = new ClusterFormationPeer[count];
            for (var i = 0; i < count; i++)
            {
                peers[i] = new ClusterFormationPeer(
                    new NodeId(ReadString(reader)),
                    ReadEndpoint(reader));
            }

            return peers;
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Utf8.GetBytes(value);
            if (bytes.Length > MaximumStringBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > MaximumStringBytes)
            {
                throw new InvalidDataException("Invalid membership string length.");
            }

            return Utf8.GetString(ReadExactly(reader, length));
        }

        private static Guid ReadGuid(BinaryReader reader)
        {
            return new Guid(ReadExactly(reader, 16));
        }

        private static void WriteReference(BinaryWriter writer, NodeReference reference)
        {
            writer.Write(reference.Cluster.Value.ToByteArray());
            WriteString(writer, reference.Node.Value);
            writer.Write(reference.Incarnation.Value.ToByteArray());
        }

        private static NodeReference ReadReference(BinaryReader reader)
        {
            return new NodeReference(
                new ClusterIncarnationId(ReadGuid(reader)),
                new NodeId(ReadString(reader)),
                new NodeIncarnationId(ReadGuid(reader)));
        }

        private static void WriteBytes(BinaryWriter writer, ReadOnlyMemory<byte> bytes)
        {
            writer.Write(bytes.Length);
            writer.Write(bytes.Span);
        }

        private static byte[] ReadBytes(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > ClusterMembershipTransportFrame.MaximumPayloadLength)
            {
                throw new InvalidDataException("Invalid membership binary payload length.");
            }

            return ReadExactly(reader, length);
        }

        private static byte[] ReadExactly(BinaryReader reader, int length)
        {
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException();
            }

            return bytes;
        }

        private static void EnsureEnd(Stream stream)
        {
            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("Membership frame contains trailing data.");
            }
        }

        internal sealed class JoinRequest
        {
            public JoinRequest(NodeId node, NodeIncarnationId incarnation, NodeEndpoint endpoint)
            {
                Node = node;
                Incarnation = incarnation;
                Endpoint = endpoint;
            }

            public NodeId Node { get; }
            public NodeIncarnationId Incarnation { get; }
            public NodeEndpoint Endpoint { get; }
        }

        internal sealed class JoinResponse
        {
            public JoinResponse(NodeReference local, ClusterMembershipTransfer transfer)
            {
                Local = local;
                Transfer = transfer;
            }

            public NodeReference Local { get; }
            public ClusterMembershipTransfer Transfer { get; }
        }

        internal sealed class FormationProbeResponse
        {
            public FormationProbeResponse(
                bool established,
                IReadOnlyList<ClusterFormationPeer> peers)
            {
                Established = established;
                Peers = peers;
            }

            public bool Established { get; }
            public IReadOnlyList<ClusterFormationPeer> Peers { get; }
        }

        internal sealed class FormationAgreementRequest
        {
            public FormationAgreementRequest(
                string digest,
                IReadOnlyList<ClusterFormationPeer> peers)
            {
                Digest = digest;
                Peers = peers;
            }

            public string Digest { get; }
            public IReadOnlyList<ClusterFormationPeer> Peers { get; }
        }

        internal sealed class FormationAgreementResponse
        {
            public FormationAgreementResponse(
                bool established,
                bool accepted,
                IReadOnlyList<ClusterFormationPeer> peers)
            {
                Established = established;
                Accepted = accepted;
                Peers = peers;
            }

            public bool Established { get; }
            public bool Accepted { get; }
            public IReadOnlyList<ClusterFormationPeer> Peers { get; }
        }
    }
}
