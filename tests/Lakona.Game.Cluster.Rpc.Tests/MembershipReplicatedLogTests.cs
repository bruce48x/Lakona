using Lakona.Game.Cluster.Rpc.Membership;
using System.Security.Cryptography;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class MembershipReplicatedLogTests
{
    [Fact]
    public void AppendEntriesCommitsOnlyTheLeaderCommittedPrefix()
    {
        var log = new MembershipReplicatedLog();
        var result = log.AppendFromLeader(new MembershipAppendBatch(
            previousIndex: 0,
            previousTerm: 0,
            leaderCommit: 1,
            new[]
            {
                new MembershipLogEntry(1, term: 2, "member-add", new byte[] { 1 }),
                new MembershipLogEntry(2, term: 2, "member-ready", new byte[] { 2 })
            }));

        Assert.Equal(MembershipAppendStatus.Accepted, result.Status);
        Assert.Equal(2, result.MatchIndex);
        Assert.Equal(1, log.CommitIndex);
        Assert.Equal(2, log.LastIndex);

        var committed = log.ReadCommittedAfter(0);
        var entry = Assert.Single(committed);
        Assert.Equal(1, entry.Index);
        Assert.Equal("member-add", entry.CommandKind);
        Assert.Equal(new byte[] { 1 }, entry.Payload.ToArray());
    }

    [Fact]
    public void ConflictingUncommittedSuffixIsReplacedWithoutChangingCommittedPrefix()
    {
        var log = new MembershipReplicatedLog();
        log.AppendFromLeader(new MembershipAppendBatch(
            previousIndex: 0,
            previousTerm: 0,
            leaderCommit: 1,
            new[]
            {
                new MembershipLogEntry(1, term: 1, "member-add", new byte[] { 1 }),
                new MembershipLogEntry(2, term: 1, "old", new byte[] { 2 }),
                new MembershipLogEntry(3, term: 1, "old", new byte[] { 3 })
            }));

        var result = log.AppendFromLeader(new MembershipAppendBatch(
            previousIndex: 1,
            previousTerm: 1,
            leaderCommit: 3,
            new[]
            {
                new MembershipLogEntry(2, term: 2, "replacement", new byte[] { 20 }),
                new MembershipLogEntry(3, term: 2, "replacement", new byte[] { 30 })
            }));

        Assert.Equal(MembershipAppendStatus.Accepted, result.Status);
        Assert.Equal(3, log.CommitIndex);
        var committed = log.ReadCommittedAfter(0);
        Assert.Equal(new long[] { 1, 2, 3 }, committed.Select(entry => entry.Index));
        Assert.Equal(new byte[] { 1 }, committed[0].Payload.ToArray());
        Assert.Equal(new byte[] { 20 }, committed[1].Payload.ToArray());
        Assert.Equal(new byte[] { 30 }, committed[2].Payload.ToArray());
    }

    [Fact]
    public void InstallingCheckedSnapshotCompactsPrefixAndAnchorsFutureAppends()
    {
        var log = new MembershipReplicatedLog();
        log.AppendFromLeader(new MembershipAppendBatch(
            previousIndex: 0,
            previousTerm: 0,
            leaderCommit: 3,
            new[]
            {
                new MembershipLogEntry(1, term: 1, "one", new byte[] { 1 }),
                new MembershipLogEntry(2, term: 1, "two", new byte[] { 2 }),
                new MembershipLogEntry(3, term: 2, "three", new byte[] { 3 })
            }));
        var payload = new byte[] { 10, 20, 30 };
        var snapshot = new MembershipLogSnapshot(
            lastIncludedIndex: 2,
            lastIncludedTerm: 1,
            payload,
            SHA256.HashData(payload));

        var install = log.InstallSnapshot(snapshot);
        var append = log.AppendFromLeader(new MembershipAppendBatch(
            previousIndex: 3,
            previousTerm: 2,
            leaderCommit: 4,
            new[]
            {
                new MembershipLogEntry(4, term: 2, "four", new byte[] { 4 })
            }));

        Assert.Equal(MembershipSnapshotInstallStatus.Installed, install);
        Assert.Equal(2, log.SnapshotIndex);
        Assert.Equal(4, log.CommitIndex);
        Assert.Equal(MembershipAppendStatus.Accepted, append.Status);
        Assert.Throws<MembershipSnapshotRequiredException>(() => log.ReadCommittedAfter(1));
        Assert.Equal(
            new long[] { 3, 4 },
            log.ReadCommittedAfter(2).Select(entry => entry.Index));
    }

    [Fact]
    public void SameIndexAndTermCannotAcknowledgeDifferentCommandBytes()
    {
        var log = new MembershipReplicatedLog();
        log.AppendFromLeader(new MembershipAppendBatch(
            previousIndex: 0,
            previousTerm: 0,
            leaderCommit: 0,
            new[]
            {
                new MembershipLogEntry(1, term: 1, "member-add", new byte[] { 1 })
            }));

        var result = log.AppendFromLeader(new MembershipAppendBatch(
            previousIndex: 0,
            previousTerm: 0,
            leaderCommit: 0,
            new[]
            {
                new MembershipLogEntry(1, term: 1, "member-remove", new byte[] { 2 })
            }));

        Assert.Equal(MembershipAppendStatus.InvalidBatch, result.Status);
        var retained = log.AppendFromLeader(new MembershipAppendBatch(
            previousIndex: 1,
            previousTerm: 1,
            leaderCommit: 1,
            Array.Empty<MembershipLogEntry>()));
        Assert.Equal(MembershipAppendStatus.Accepted, retained.Status);
        Assert.Equal("member-add", Assert.Single(log.ReadCommittedAfter(0)).CommandKind);
    }
}
