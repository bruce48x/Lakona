using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class QuorumProofTrackerTests
{
    [Fact]
    public void AuthorityExpiresUsingMonotonicTimeAndRejectsReplay()
    {
        var clock = new ManualTimeProvider();
        var membership = CreateMembership(view: 7);
        var tracker = new QuorumProofTracker(membership, clock, TimeSpan.FromSeconds(10));
        var proof = new QuorumProof(
            membership.Current.Cluster,
            term: 3,
            membership.Current.View,
            sequence: 9,
            validFor: TimeSpan.FromSeconds(5));

        Assert.True(tracker.TryAccept(proof));
        Assert.True(tracker.HasAuthority);
        Assert.False(tracker.TryAccept(proof));

        clock.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.True(tracker.HasAuthority);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(tracker.HasAuthority);
    }

    [Fact]
    public void CommittedViewChangeInvalidatesAnOtherwiseLiveProof()
    {
        var clock = new ManualTimeProvider();
        var cluster = new ClusterIncarnationId(
            Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var membership = new MutableMembership(CreateSnapshot(cluster, view: 7));
        var tracker = new QuorumProofTracker(membership, clock, TimeSpan.FromSeconds(10));

        Assert.True(tracker.TryAccept(new QuorumProof(
            cluster,
            term: 3,
            new MembershipViewId(7),
            sequence: 9,
            validFor: TimeSpan.FromSeconds(5))));

        membership.CurrentSnapshot = CreateSnapshot(cluster, view: 8);

        Assert.False(tracker.HasAuthority);
        Assert.False(tracker.TryAccept(new QuorumProof(
            cluster,
            term: 3,
            new MembershipViewId(7),
            sequence: 10,
            validFor: TimeSpan.FromSeconds(5))));
        Assert.True(tracker.TryAccept(new QuorumProof(
            cluster,
            term: 3,
            new MembershipViewId(8),
            sequence: 11,
            validFor: TimeSpan.FromSeconds(5))));
        Assert.True(tracker.HasAuthority);
    }

    [Fact]
    public void ProofsCannotExceedTheConfiguredSafetyWindow()
    {
        var membership = CreateMembership(view: 1);
        var tracker = new QuorumProofTracker(
            membership,
            new ManualTimeProvider(),
            TimeSpan.FromSeconds(5));

        Assert.False(tracker.TryAccept(new QuorumProof(
            membership.Current.Cluster,
            term: 1,
            membership.Current.View,
            sequence: 1,
            validFor: TimeSpan.FromSeconds(6))));
        Assert.False(tracker.HasAuthority);
    }

    private static MutableMembership CreateMembership(long view)
    {
        return new MutableMembership(CreateSnapshot(
            new ClusterIncarnationId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            view));
    }

    private static ClusterMembershipSnapshot CreateSnapshot(
        ClusterIncarnationId cluster,
        long view)
    {
        return new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(view),
            Array.Empty<ClusterMember>());
    }

    private sealed class MutableMembership : IClusterMembership
    {
        public MutableMembership(ClusterMembershipSnapshot current)
        {
            CurrentSnapshot = current;
        }

        public ClusterMembershipSnapshot CurrentSnapshot { get; set; }

        public ClusterMembershipSnapshot Current => CurrentSnapshot;

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return timestamp;
        }

        public void Advance(TimeSpan duration)
        {
            timestamp += duration.Ticks;
        }
    }
}
