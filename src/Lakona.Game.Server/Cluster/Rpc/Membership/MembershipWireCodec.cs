using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lakona.Game.Cluster;
using static Lakona.Game.Cluster.Rpc.Membership.MembershipBinaryCodec;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal static class MembershipWireCodec
    {
        private const byte Version = ClusterProtocol.MembershipFrames.Version;
        private const byte JoinRequestKind = ClusterProtocol.MembershipFrames.JoinRequest;
        private const byte JoinResponseKind = ClusterProtocol.MembershipFrames.JoinResponse;
        private const byte AppendRequestKind = ClusterProtocol.MembershipFrames.AppendRequest;
        private const byte AppendResponseKind = ClusterProtocol.MembershipFrames.AppendResponse;
        private const byte VoteRequestKind = ClusterProtocol.MembershipFrames.VoteRequest;
        private const byte VoteResponseKind = ClusterProtocol.MembershipFrames.VoteResponse;
        private const byte ProofKind = ClusterProtocol.MembershipFrames.Proof;
        private const byte ProofResponseKind = ClusterProtocol.MembershipFrames.ProofResponse;
        private const byte PromoteRequestKind = ClusterProtocol.MembershipFrames.PromoteRequest;
        private const byte PromoteResponseKind = ClusterProtocol.MembershipFrames.PromoteResponse;
        private const byte ReadyRequestKind = ClusterProtocol.MembershipFrames.ReadyRequest;
        private const byte ReadyResponseKind = ClusterProtocol.MembershipFrames.ReadyResponse;
        private const byte FormationProbeRequestKind = ClusterProtocol.MembershipFrames.FormationProbeRequest;
        private const byte FormationProbeResponseKind = ClusterProtocol.MembershipFrames.FormationProbeResponse;
        private const byte FormationAgreementRequestKind = ClusterProtocol.MembershipFrames.FormationAgreementRequest;
        private const byte FormationAgreementResponseKind = ClusterProtocol.MembershipFrames.FormationAgreementResponse;
        private const byte SnapshotInstallRequestKind = ClusterProtocol.MembershipFrames.SnapshotInstallRequest;
        private const byte SnapshotInstallResponseKind = ClusterProtocol.MembershipFrames.SnapshotInstallResponse;
        private const byte NotLeaderResponseKind = ClusterProtocol.MembershipFrames.NotLeaderResponse;
        private const byte MembershipUnavailableResponseKind = ClusterProtocol.MembershipFrames.MembershipUnavailableResponse;
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
            WriteGuid(writer, incarnation.Value);
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
            WriteGuid(writer, local.Cluster.Value);
            WriteString(writer, local.Node.Value);
            WriteGuid(writer, local.Incarnation.Value);
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
            WriteGuid(writer, proof.Cluster.Value);
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

        public static ClusterMembershipTransportFrame EncodeNotLeaderResponse(
            NodeEndpoint? leaderEndpoint)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
            WriteHeader(writer, NotLeaderResponseKind);
            writer.Write(leaderEndpoint is not null);
            if (leaderEndpoint is not null)
            {
                WriteEndpoint(writer, leaderEndpoint);
            }

            return new ClusterMembershipTransportFrame(stream.ToArray());
        }

        public static NodeEndpoint? DecodeNotLeaderResponse(
            ClusterMembershipTransportFrame frame)
        {
            using var stream = CreateReadStream(frame);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            ReadHeader(reader, NotLeaderResponseKind);
            var hasEndpoint = reader.ReadBoolean();
            var endpoint = hasEndpoint ? ReadEndpoint(reader) : null;
            EnsureEnd(stream);
            return endpoint;
        }

        public static bool IsNotLeaderResponse(ClusterMembershipTransportFrame frame)
        {
            return GetKind(frame) == NotLeaderResponseKind;
        }

        public static ClusterMembershipTransportFrame EncodeMembershipUnavailableResponse() =>
            new(new byte[] { Version, MembershipUnavailableResponseKind });

        public static bool IsMembershipUnavailableResponse(ClusterMembershipTransportFrame frame) =>
            GetKind(frame) == MembershipUnavailableResponseKind;

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
            WriteMap(writer, endpoint.Metadata, deterministic: false);
        }

        private static NodeEndpoint ReadEndpoint(BinaryReader reader)
        {
            return new NodeEndpoint(ReadString(reader), ReadMap(reader));
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

        private static void WriteReference(BinaryWriter writer, NodeReference reference)
        {
            WriteGuid(writer, reference.Cluster.Value);
            WriteString(writer, reference.Node.Value);
            WriteGuid(writer, reference.Incarnation.Value);
        }

        private static NodeReference ReadReference(BinaryReader reader)
        {
            return new NodeReference(
                new ClusterIncarnationId(ReadGuid(reader)),
                new NodeId(ReadString(reader)),
                new NodeIncarnationId(ReadGuid(reader)));
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
