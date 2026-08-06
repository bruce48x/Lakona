using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class MembershipLogEntry
    {
        private readonly byte[] payload;

        public MembershipLogEntry(
            long index,
            long term,
            string commandKind,
            ReadOnlyMemory<byte> payload)
        {
            if (index <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Log index must be positive.");
            }

            if (term <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(term), "Log term must be positive.");
            }

            if (string.IsNullOrWhiteSpace(commandKind))
            {
                throw new ArgumentException("Membership command kind is required.", nameof(commandKind));
            }

            Index = index;
            Term = term;
            CommandKind = commandKind;
            this.payload = payload.ToArray();
            EncodedSize = Encoding.UTF8.GetByteCount(commandKind) + this.payload.Length;
        }

        public long Index { get; }

        public long Term { get; }

        public string CommandKind { get; }

        public ReadOnlyMemory<byte> Payload => payload;

        internal int EncodedSize { get; }

        internal bool HasSameCommand(MembershipLogEntry other)
        {
            return string.Equals(CommandKind, other.CommandKind, StringComparison.Ordinal)
                && Payload.Span.SequenceEqual(other.Payload.Span);
        }
    }

    internal sealed class MembershipAppendBatch
    {
        public MembershipAppendBatch(
            long previousIndex,
            long previousTerm,
            long leaderCommit,
            IReadOnlyList<MembershipLogEntry> entries)
        {
            if (previousIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(previousIndex));
            }

            if (previousTerm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(previousTerm));
            }

            if (leaderCommit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(leaderCommit));
            }

            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            PreviousIndex = previousIndex;
            PreviousTerm = previousTerm;
            LeaderCommit = leaderCommit;
            Entries = new ReadOnlyCollection<MembershipLogEntry>(
                new List<MembershipLogEntry>(entries));
        }

        public long PreviousIndex { get; }

        public long PreviousTerm { get; }

        public long LeaderCommit { get; }

        public IReadOnlyList<MembershipLogEntry> Entries { get; }
    }

    internal enum MembershipAppendStatus
    {
        Accepted = 0,
        PreviousEntryMismatch = 1,
        CommittedEntryConflict = 2,
        InvalidBatch = 3,
        CapacityExceeded = 4
    }

    internal sealed class MembershipAppendResult
    {
        public MembershipAppendResult(MembershipAppendStatus status, long matchIndex)
        {
            Status = status;
            MatchIndex = matchIndex;
        }

        public MembershipAppendStatus Status { get; }

        public long MatchIndex { get; }
    }

    internal sealed class MembershipLogSnapshot
    {
        private readonly byte[] payload;
        private readonly byte[] checksum;

        public MembershipLogSnapshot(
            long lastIncludedIndex,
            long lastIncludedTerm,
            ReadOnlyMemory<byte> payload,
            ReadOnlyMemory<byte> checksum)
        {
            if (lastIncludedIndex <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lastIncludedIndex),
                    "Snapshot index must be positive.");
            }

            if (lastIncludedTerm <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lastIncludedTerm),
                    "Snapshot term must be positive.");
            }

            LastIncludedIndex = lastIncludedIndex;
            LastIncludedTerm = lastIncludedTerm;
            this.payload = payload.ToArray();
            this.checksum = checksum.ToArray();
        }

        public long LastIncludedIndex { get; }

        public long LastIncludedTerm { get; }

        public ReadOnlyMemory<byte> Payload => payload;

        public ReadOnlyMemory<byte> Checksum => checksum;
    }

    internal enum MembershipSnapshotInstallStatus
    {
        Installed = 0,
        IgnoredOlder = 1,
        ChecksumMismatch = 2,
        CommittedEntryConflict = 3,
        CapacityExceeded = 4
    }

    internal sealed class MembershipSnapshotRequiredException : InvalidOperationException
    {
        public MembershipSnapshotRequiredException(long snapshotIndex)
            : base($"Committed entries before snapshot index {snapshotIndex} are no longer retained.")
        {
            SnapshotIndex = snapshotIndex;
        }

        public long SnapshotIndex { get; }
    }

    internal sealed class MembershipReplicatedLog
    {
        private const int MaximumEntriesPerBatch = 256;
        private const int MaximumBatchBytes = 1024 * 1024;
        private const int MaximumRetainedEntries = 4096;
        private const int MaximumSnapshotBytes = 4 * 1024 * 1024;

        private readonly object gate = new object();
        private readonly List<MembershipLogEntry> entries = new List<MembershipLogEntry>();
        private long snapshotTerm;

        public MembershipLogSnapshot? InstalledSnapshot
        {
            get
            {
                lock (gate)
                {
                    return installedSnapshot;
                }
            }
            private set { installedSnapshot = value; }
        }

        private MembershipLogSnapshot? installedSnapshot;

        public long CommitIndex
        {
            get
            {
                lock (gate)
                {
                    return commitIndex;
                }
            }
            private set { commitIndex = value; }
        }

        private long commitIndex;

        public long SnapshotIndex
        {
            get
            {
                lock (gate)
                {
                    return snapshotIndex;
                }
            }
            private set { snapshotIndex = value; }
        }

        private long snapshotIndex;

        public long LastIndex
        {
            get
            {
                lock (gate)
                {
                    return entries.Count == 0
                        ? SnapshotIndex
                        : entries[entries.Count - 1].Index;
                }
            }
        }

        public long LastTerm
        {
            get
            {
                lock (gate)
                {
                    return entries.Count == 0
                        ? snapshotTerm
                        : entries[entries.Count - 1].Term;
                }
            }
        }

        internal object SyncRoot => gate;

        public MembershipAppendResult AppendFromLeader(MembershipAppendBatch batch)
        {
            if (batch is null)
            {
                throw new ArgumentNullException(nameof(batch));
            }

            lock (gate)
            {
                if (!TryValidateBatch(batch))
                {
                    return new MembershipAppendResult(MembershipAppendStatus.InvalidBatch, LastIndex);
                }

            if (!TryGetTerm(batch.PreviousIndex, out var previousTerm)
                || previousTerm != batch.PreviousTerm)
            {
                return new MembershipAppendResult(
                    MembershipAppendStatus.PreviousEntryMismatch,
                    LastIndex);
            }

            var firstNewOffset = batch.Entries.Count;
            var truncateAt = -1;
            for (var i = 0; i < batch.Entries.Count; i++)
            {
                var incoming = batch.Entries[i];
                if (!TryGetEntry(incoming.Index, out var existing))
                {
                    firstNewOffset = i;
                    break;
                }

                if (existing!.Term == incoming.Term)
                {
                    if (!existing.HasSameCommand(incoming))
                    {
                        return new MembershipAppendResult(
                            MembershipAppendStatus.InvalidBatch,
                            LastIndex);
                    }

                    continue;
                }

                if (incoming.Index <= CommitIndex)
                {
                    return new MembershipAppendResult(
                        MembershipAppendStatus.CommittedEntryConflict,
                        LastIndex);
                }

                truncateAt = GetOffset(incoming.Index);
                firstNewOffset = i;
                break;
            }

            var appendedCount = batch.Entries.Count - firstNewOffset;
            var retainedCount = truncateAt >= 0 ? truncateAt : entries.Count;
            if (retainedCount + appendedCount > MaximumRetainedEntries)
            {
                return new MembershipAppendResult(
                    MembershipAppendStatus.CapacityExceeded,
                    LastIndex);
            }

            if (truncateAt >= 0)
            {
                entries.RemoveRange(truncateAt, entries.Count - truncateAt);
            }

            for (var i = firstNewOffset; i < batch.Entries.Count; i++)
            {
                entries.Add(batch.Entries[i]);
            }

            var leaderCommit = Math.Min(batch.LeaderCommit, LastIndex);
            if (leaderCommit > CommitIndex)
            {
                CommitIndex = leaderCommit;
            }

                return new MembershipAppendResult(MembershipAppendStatus.Accepted, LastIndex);
            }
        }

        public MembershipSnapshotInstallStatus InstallSnapshot(MembershipLogSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            lock (gate)
            {
                if (snapshot.Payload.Length > MaximumSnapshotBytes)
                {
                    return MembershipSnapshotInstallStatus.CapacityExceeded;
                }

            var actualChecksum = SHA256.HashData(snapshot.Payload.Span);
            if (snapshot.Checksum.Length != actualChecksum.Length
                || !CryptographicOperations.FixedTimeEquals(
                    snapshot.Checksum.Span,
                    actualChecksum))
            {
                return MembershipSnapshotInstallStatus.ChecksumMismatch;
            }

            if (snapshot.LastIncludedIndex < SnapshotIndex)
            {
                return MembershipSnapshotInstallStatus.IgnoredOlder;
            }

            var matchesExisting = TryGetTerm(snapshot.LastIncludedIndex, out var existingTerm)
                && existingTerm == snapshot.LastIncludedTerm;
            if (snapshot.LastIncludedIndex <= CommitIndex && !matchesExisting)
            {
                return MembershipSnapshotInstallStatus.CommittedEntryConflict;
            }

            if (matchesExisting)
            {
                var retainedOffset = 0;
                while (retainedOffset < entries.Count
                    && entries[retainedOffset].Index <= snapshot.LastIncludedIndex)
                {
                    retainedOffset++;
                }

                if (retainedOffset > 0)
                {
                    entries.RemoveRange(0, retainedOffset);
                }
            }
            else
            {
                entries.Clear();
            }

            SnapshotIndex = snapshot.LastIncludedIndex;
            snapshotTerm = snapshot.LastIncludedTerm;
            InstalledSnapshot = snapshot;
            if (CommitIndex < SnapshotIndex)
            {
                CommitIndex = SnapshotIndex;
            }

                return MembershipSnapshotInstallStatus.Installed;
            }
        }

        public IReadOnlyList<MembershipLogEntry> ReadCommittedAfter(long index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            lock (gate)
            {
                if (index < SnapshotIndex)
                {
                    throw new MembershipSnapshotRequiredException(SnapshotIndex);
                }

            var committed = new List<MembershipLogEntry>();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.Index > index && entry.Index <= CommitIndex)
                {
                    committed.Add(entry);
                }
            }

                return new ReadOnlyCollection<MembershipLogEntry>(committed);
            }
        }

        public MembershipAppendBatch CreateCommittedBatchAfter(long index)
        {
            lock (gate)
            {
                if (index < SnapshotIndex)
                {
                    throw new MembershipSnapshotRequiredException(SnapshotIndex);
                }

                if (index > CommitIndex || !TryGetTerm(index, out var previousTerm))
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                var committed = new List<MembershipLogEntry>();
                var bytes = 0;
                for (var i = 0; i < entries.Count && committed.Count < MaximumEntriesPerBatch; i++)
                {
                    var entry = entries[i];
                    if (entry.Index <= index || entry.Index > CommitIndex)
                    {
                        continue;
                    }

                    if (bytes + entry.EncodedSize > MaximumBatchBytes)
                    {
                        break;
                    }

                    committed.Add(entry);
                    bytes += entry.EncodedSize;
                }

                return new MembershipAppendBatch(
                    index,
                    previousTerm,
                    CommitIndex,
                    committed);
            }
        }

        public MembershipAppendBatch CreateBatchAfter(long index)
        {
            lock (gate)
            {
                if (index < SnapshotIndex)
                {
                    throw new MembershipSnapshotRequiredException(SnapshotIndex);
                }

                if (index > LastIndex || !TryGetTerm(index, out var previousTerm))
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                var pending = new List<MembershipLogEntry>();
                var bytes = 0;
                for (var i = 0; i < entries.Count && pending.Count < MaximumEntriesPerBatch; i++)
                {
                    var entry = entries[i];
                    if (entry.Index <= index)
                    {
                        continue;
                    }

                    if (bytes + entry.EncodedSize > MaximumBatchBytes)
                    {
                        break;
                    }

                    pending.Add(entry);
                    bytes += entry.EncodedSize;
                }

                return new MembershipAppendBatch(index, previousTerm, CommitIndex, pending);
            }
        }

        public bool AdvanceLeaderCommit(long index, long currentTerm)
        {
            lock (gate)
            {
                if (index <= CommitIndex || index > LastIndex)
                {
                    return false;
                }

            if (!TryGetTerm(index, out var term) || term != currentTerm)
            {
                return false;
            }

                CommitIndex = index;
                return true;
            }
        }

        internal bool HasMatchingUncommittedTail(
            string commandKind,
            ReadOnlyMemory<byte> payload)
        {
            if (string.IsNullOrWhiteSpace(commandKind))
            {
                throw new ArgumentException("Membership command kind is required.", nameof(commandKind));
            }

            lock (gate)
            {
                if (CommitIndex == LastIndex || entries.Count == 0)
                {
                    return false;
                }

                var tail = entries[entries.Count - 1];
                return tail.Index == LastIndex
                    && string.Equals(tail.CommandKind, commandKind, StringComparison.Ordinal)
                    && tail.Payload.Span.SequenceEqual(payload.Span);
            }
        }

        private bool TryValidateBatch(MembershipAppendBatch batch)
        {
            if (batch.PreviousIndex == long.MaxValue
                || batch.PreviousIndex == 0 && batch.PreviousTerm != 0
                || batch.Entries.Count > MaximumEntriesPerBatch)
            {
                return false;
            }

            var expectedIndex = batch.PreviousIndex + 1;
            var bytes = 0;
            for (var i = 0; i < batch.Entries.Count; i++)
            {
                var entry = batch.Entries[i];
                if (entry is null || entry.Index != expectedIndex)
                {
                    return false;
                }

                expectedIndex++;
                bytes = checked(bytes + entry.EncodedSize);
                if (bytes > MaximumBatchBytes)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryGetTerm(long index, out long term)
        {
            if (index == SnapshotIndex)
            {
                term = snapshotTerm;
                return true;
            }

            if (TryGetEntry(index, out var entry))
            {
                term = entry!.Term;
                return true;
            }

            term = 0;
            return false;
        }

        private bool TryGetEntry(long index, out MembershipLogEntry? entry)
        {
            if (index <= SnapshotIndex || index > LastIndex)
            {
                entry = null;
                return false;
            }

            var offset = GetOffset(index);
            if (offset < 0 || offset >= entries.Count || entries[offset].Index != index)
            {
                entry = null;
                return false;
            }

            entry = entries[offset];
            return true;
        }

        private int GetOffset(long index)
        {
            return checked((int)(index - SnapshotIndex - 1));
        }
    }
}
