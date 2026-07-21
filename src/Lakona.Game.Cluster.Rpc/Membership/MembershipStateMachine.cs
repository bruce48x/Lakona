using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class MembershipCommand
    {
        private readonly byte[] payload;

        public MembershipCommand(string kind, ReadOnlyMemory<byte> payload)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Membership command kind is required.", nameof(kind));
            }

            Kind = kind;
            this.payload = payload.ToArray();
        }

        public string Kind { get; }

        public ReadOnlyMemory<byte> Payload => payload;
    }

    internal static class MembershipCommands
    {
        public const string SetMemberStateKind = "member-state-v1";

        public const string ReplaceSnapshotKind = "membership-snapshot-v2";

        public static MembershipCommand SetMemberState(
            NodeReference target,
            ClusterMemberState state)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var nodeBytes = Encoding.UTF8.GetBytes(target.Node.Value);
            if (nodeBytes.Length > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(target),
                    "Encoded node id is too long for the membership protocol.");
            }

            var payload = new byte[1 + 16 + 2 + nodeBytes.Length + 16 + 1];
            var offset = 0;
            payload[offset++] = 1;
            target.Cluster.Value.TryWriteBytes(payload.AsSpan(offset, 16));
            offset += 16;
            BinaryPrimitives.WriteUInt16LittleEndian(
                payload.AsSpan(offset, 2),
                checked((ushort)nodeBytes.Length));
            offset += 2;
            nodeBytes.CopyTo(payload, offset);
            offset += nodeBytes.Length;
            target.Incarnation.Value.TryWriteBytes(payload.AsSpan(offset, 16));
            offset += 16;
            payload[offset] = checked((byte)state);
            return new MembershipCommand(SetMemberStateKind, payload);
        }

        public static MembershipCommand ReplaceSnapshot(
            ClusterMembershipSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new MembershipCommand(
                ReplaceSnapshotKind,
                MembershipSnapshotCodec.Encode(snapshot));
        }
    }

    internal sealed class MembershipStateMachine
    {
        private readonly ClusterMembershipRuntime runtime;
        private readonly MembershipReplicatedLog log;
        private long appliedIndex;

        public MembershipStateMachine(
            ClusterMembershipRuntime runtime,
            MembershipReplicatedLog log)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public int ApplyCommitted()
        {
            lock (log.SyncRoot)
            {
                if (appliedIndex < log.SnapshotIndex)
                {
                    var snapshot = log.InstalledSnapshot;
                    if (snapshot is null || snapshot.LastIncludedIndex != log.SnapshotIndex)
                    {
                        throw new MembershipSnapshotRequiredException(log.SnapshotIndex);
                    }

                    runtime.RestoreCommitted(MembershipSnapshotCodec.Decode(snapshot.Payload.Span));
                    appliedIndex = snapshot.LastIncludedIndex;
                }

                var entries = log.ReadCommittedAfter(appliedIndex);
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    Apply(entry);
                    appliedIndex = entry.Index;
                }

                return entries.Count;
            }
        }

        private void Apply(MembershipLogEntry entry)
        {
            if (string.Equals(
                entry.CommandKind,
                MembershipCommands.ReplaceSnapshotKind,
                StringComparison.Ordinal))
            {
                ApplySnapshot(MembershipSnapshotCodec.Decode(entry.Payload.Span));
                return;
            }

            if (!string.Equals(
                entry.CommandKind,
                MembershipCommands.SetMemberStateKind,
                StringComparison.Ordinal))
            {
                throw new TerminalMembershipException(
                    $"Unknown committed membership command '{entry.CommandKind}'.");
            }

            var command = DecodeSetMemberState(entry.Payload.Span);
            var current = runtime.Current;
            if (!current.TryGetMember(command.Target, out var existing)
                || existing is null)
            {
                throw new TerminalMembershipException(
                    "Committed member-state command targets an unknown node incarnation.");
            }

            var members = new List<ClusterMember>(current.Members.Count);
            for (var i = 0; i < current.Members.Count; i++)
            {
                var member = current.Members[i];
                members.Add(member.Reference == command.Target
                    ? new ClusterMember(
                        member.Reference,
                        command.State,
                        member.ClusterEndpoint,
                        member.IsVoter,
                        member.Labels,
                        member.Advertisements,
                        member.ActorHosts,
                        member.StartupActors)
                    : member);
            }

            runtime.PublishCommitted(new ClusterMembershipSnapshot(
                current.Cluster,
                new MembershipViewId(current.View.Value + 1),
                members));
        }

        private void ApplySnapshot(ClusterMembershipSnapshot next)
        {
            var current = runtime.Current;
            if (next.Cluster != current.Cluster
                || current.View.Value == long.MaxValue
                || next.View.Value != current.View.Value + 1)
            {
                throw new TerminalMembershipException(
                    "Committed membership snapshot is not exactly the next view of the current cluster.");
            }

            runtime.PublishCommitted(next);
        }

        private static SetMemberStateCommand DecodeSetMemberState(ReadOnlySpan<byte> payload)
        {
            const int FixedBytes = 1 + 16 + 2 + 16 + 1;
            if (payload.Length < FixedBytes || payload[0] != 1)
            {
                throw new TerminalMembershipException(
                    "Committed member-state command has an invalid encoding.");
            }

            var offset = 1;
            var cluster = new ClusterIncarnationId(new Guid(payload.Slice(offset, 16)));
            offset += 16;
            var nodeLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset, 2));
            offset += 2;
            if (payload.Length != FixedBytes + nodeLength)
            {
                throw new TerminalMembershipException(
                    "Committed member-state command has an invalid length.");
            }

            var node = new NodeId(Encoding.UTF8.GetString(payload.Slice(offset, nodeLength)));
            offset += nodeLength;
            var incarnation = new NodeIncarnationId(new Guid(payload.Slice(offset, 16)));
            offset += 16;
            var state = (ClusterMemberState)payload[offset];
            if (!Enum.IsDefined(typeof(ClusterMemberState), state))
            {
                throw new TerminalMembershipException(
                    "Committed member-state command contains an unknown member state.");
            }

            return new SetMemberStateCommand(
                new NodeReference(cluster, node, incarnation),
                state);
        }

        private sealed class SetMemberStateCommand
        {
            public SetMemberStateCommand(NodeReference target, ClusterMemberState state)
            {
                Target = target;
                State = state;
            }

            public NodeReference Target { get; }

            public ClusterMemberState State { get; }
        }
    }
}
