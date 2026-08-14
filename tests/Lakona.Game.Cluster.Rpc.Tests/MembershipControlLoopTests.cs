using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class MembershipControlLoopTests
{
    [Fact]
    public async Task TransientFailuresAreObservedAndCannotHideAuthorityExpiry()
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var clock = new ManualTimeProvider();
        var membership = new StubMembership(CreateSnapshot());
        var proofTracker = new QuorumProofTracker(
            membership,
            clock,
            TimeSpan.FromSeconds(5));
        var listener = new RecordingAuthorityListener(cancellation);
        var round = new ScriptedRound(proofTracker, membership.Current);
        var delays = new AdvancingDelay(clock);
        var loop = new MembershipControlLoop(
            round,
            proofTracker,
            listener,
            delays,
            new MaximumJitterRandom(),
            new MembershipControlLoopOptions
            {
                HeartbeatInterval = TimeSpan.FromSeconds(1),
                MinimumRetryDelay = TimeSpan.FromSeconds(1),
                MaximumRetryDelay = TimeSpan.FromSeconds(4)
            });

        await loop.RunAsync(cancellation.Token);

        Assert.Equal(1, listener.AvailableCount);
        Assert.Equal(1, listener.LostCount);
        Assert.Equal(3, listener.TransientFailures.Count);
        Assert.Equal(
            new[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(1)
            },
            delays.CompletedDelays);
        Assert.Equal(TimeSpan.FromSeconds(5).Ticks, clock.GetTimestamp());
    }

    [Fact]
    public async Task TerminalFencingEscapesTheRetryLoop()
    {
        var membership = new StubMembership(CreateSnapshot());
        var loop = new MembershipControlLoop(
            new TerminalRound(),
            new QuorumProofTracker(
                membership,
                new ManualTimeProvider(),
                TimeSpan.FromSeconds(5)),
            new RecordingAuthorityListener(),
            new AdvancingDelay(new ManualTimeProvider()),
            new MaximumJitterRandom(),
            new MembershipControlLoopOptions());

        await Assert.ThrowsAsync<TerminalMembershipException>(async () =>
            await loop.RunAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthorityExpiresWhileAControlRoundIsStillPending()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var clock = new ManualTimeProvider();
        var membership = new StubMembership(CreateSnapshot());
        var proofTracker = new QuorumProofTracker(
            membership,
            clock,
            TimeSpan.FromSeconds(5));
        var listener = new RecordingAuthorityListener(cancellation);
        var loop = new MembershipControlLoop(
            new ProofThenPendingRound(proofTracker, membership.Current),
            proofTracker,
            listener,
            new AdvancingDelay(clock),
            new MaximumJitterRandom(),
            new MembershipControlLoopOptions
            {
                HeartbeatInterval = TimeSpan.FromSeconds(1),
                MinimumRetryDelay = TimeSpan.FromSeconds(1),
                MaximumRetryDelay = TimeSpan.FromSeconds(4)
            });

        await loop.RunAsync(cancellation.Token).WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, listener.AvailableCount);
        Assert.Equal(1, listener.LostCount);
        Assert.Equal(TimeSpan.FromSeconds(5).Ticks, clock.GetTimestamp());
    }

    [Fact]
    public async Task AuthorityExpiryCancelsThePendingControlRound()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var clock = new ManualTimeProvider();
        var membership = new StubMembership(CreateSnapshot());
        var proofTracker = new QuorumProofTracker(
            membership,
            clock,
            TimeSpan.FromSeconds(5));
        var round = new ProofThenCancellationAwareRound(proofTracker, membership.Current);
        var listener = new CancellationOrderingListener(
            cancellation,
            () => round.PendingRoundCancellationObserved);
        var loop = new MembershipControlLoop(
            round,
            proofTracker,
            listener,
            new AdvancingDelay(clock),
            new MaximumJitterRandom(),
            new MembershipControlLoopOptions
            {
                HeartbeatInterval = TimeSpan.FromSeconds(1),
                MinimumRetryDelay = TimeSpan.FromSeconds(1),
                MaximumRetryDelay = TimeSpan.FromSeconds(4)
            });

        await loop.RunAsync(cancellation.Token).WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.True(round.PendingRoundCancellationObserved);
        Assert.Equal(1, listener.AvailableCount);
        Assert.Equal(1, listener.LostCount);
        Assert.True(listener.RoundWasCanceledBeforeAuthorityLoss);
    }

    [Fact]
    public async Task ZeroJitterSampleStillYieldsBetweenTransientRetries()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var clock = new ManualTimeProvider();
        var delays = new AdvancingDelay(clock);
        var loop = new MembershipControlLoop(
            new AlwaysTransientRound(),
            new QuorumProofTracker(
                new StubMembership(CreateSnapshot()),
                clock,
                TimeSpan.FromSeconds(5)),
            new CancelAfterFailuresListener(cancellation, failureLimit: 3),
            delays,
            new ZeroJitterRandom(),
            new MembershipControlLoopOptions
            {
                MinimumRetryDelay = TimeSpan.FromSeconds(1),
                MaximumRetryDelay = TimeSpan.FromSeconds(4)
            });

        await loop.RunAsync(cancellation.Token);

        Assert.NotEmpty(delays.CompletedDelays);
        Assert.All(delays.CompletedDelays, value => Assert.True(value > TimeSpan.Zero));
    }

    [Fact]
    public async Task FailedAuthorityRecoveryIsRetriedBeforeActivationIsRemembered()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var clock = new ManualTimeProvider();
        var membership = new StubMembership(CreateSnapshot());
        var tracker = new QuorumProofTracker(membership, clock, TimeSpan.FromSeconds(5));
        var listener = new FailFirstAvailableListener(cancellation);
        var loop = new MembershipControlLoop(
            new RenewingProofRound(tracker, membership.Current),
            tracker,
            listener,
            new AdvancingDelay(clock),
            new MaximumJitterRandom(),
            new MembershipControlLoopOptions { HeartbeatInterval = TimeSpan.FromSeconds(1) });

        await loop.RunAsync(cancellation.Token);

        Assert.Equal(2, listener.AvailableAttempts);
        Assert.Single(listener.TransientFailures);
    }

    private static ClusterMembershipSnapshot CreateSnapshot()
    {
        return new ClusterMembershipSnapshot(
            new ClusterIncarnationId(Guid.Parse("55555555-5555-5555-5555-555555555555")),
            new MembershipViewId(1),
            Array.Empty<ClusterMember>());
    }

    private sealed class ScriptedRound : IMembershipControlRound
    {
        private readonly QuorumProofTracker tracker;
        private readonly ClusterMembershipSnapshot snapshot;
        private int attempts;

        public ScriptedRound(
            QuorumProofTracker tracker,
            ClusterMembershipSnapshot snapshot)
        {
            this.tracker = tracker;
            this.snapshot = snapshot;
        }

        public ValueTask ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            if (attempts == 1)
            {
                tracker.TryAccept(new QuorumProof(
                    snapshot.Cluster,
                    term: 1,
                    snapshot.View,
                    sequence: 1,
                    validFor: TimeSpan.FromSeconds(5)));
                return default;
            }

            throw new IOException("transient consensus transport failure");
        }
    }

    private sealed class TerminalRound : IMembershipControlRound
    {
        public ValueTask ExecuteAsync(CancellationToken cancellationToken)
        {
            throw new TerminalMembershipException("incarnation superseded");
        }
    }

    private sealed class AlwaysTransientRound : IMembershipControlRound
    {
        public ValueTask ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("transient");
        }
    }

    private sealed class RenewingProofRound : IMembershipControlRound
    {
        private readonly QuorumProofTracker tracker;
        private readonly ClusterMembershipSnapshot snapshot;
        private long sequence;

        public RenewingProofRound(
            QuorumProofTracker tracker,
            ClusterMembershipSnapshot snapshot)
        {
            this.tracker = tracker;
            this.snapshot = snapshot;
        }

        public ValueTask ExecuteAsync(CancellationToken cancellationToken)
        {
            tracker.TryAccept(new QuorumProof(
                snapshot.Cluster,
                term: 1,
                snapshot.View,
                sequence: ++sequence,
                validFor: TimeSpan.FromSeconds(5)));
            return default;
        }
    }

    private sealed class ProofThenPendingRound : IMembershipControlRound
    {
        private readonly QuorumProofTracker tracker;
        private readonly ClusterMembershipSnapshot snapshot;
        private int attempts;

        public ProofThenPendingRound(
            QuorumProofTracker tracker,
            ClusterMembershipSnapshot snapshot)
        {
            this.tracker = tracker;
            this.snapshot = snapshot;
        }

        public async ValueTask ExecuteAsync(CancellationToken cancellationToken)
        {
            attempts++;
            if (attempts == 1)
            {
                tracker.TryAccept(new QuorumProof(
                    snapshot.Cluster,
                    term: 1,
                    snapshot.View,
                    sequence: 1,
                    validFor: TimeSpan.FromSeconds(5)));
                return;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class ProofThenCancellationAwareRound : IMembershipControlRound
    {
        private readonly QuorumProofTracker tracker;
        private readonly ClusterMembershipSnapshot snapshot;
        private int attempts;

        public ProofThenCancellationAwareRound(
            QuorumProofTracker tracker,
            ClusterMembershipSnapshot snapshot)
        {
            this.tracker = tracker;
            this.snapshot = snapshot;
        }

        public bool PendingRoundCancellationObserved { get; private set; }

        public async ValueTask ExecuteAsync(CancellationToken cancellationToken)
        {
            attempts++;
            if (attempts == 1)
            {
                tracker.TryAccept(new QuorumProof(
                    snapshot.Cluster,
                    term: 1,
                    snapshot.View,
                    sequence: 1,
                    validFor: TimeSpan.FromSeconds(5)));
                return;
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                PendingRoundCancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class RecordingAuthorityListener : IClusterAuthorityListener
    {
        private readonly CancellationTokenSource? cancellation;

        public RecordingAuthorityListener(CancellationTokenSource? cancellation = null)
        {
            this.cancellation = cancellation;
        }

        public int AvailableCount { get; private set; }

        public int LostCount { get; private set; }

        public List<Exception> TransientFailures { get; } = new();

        public ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken)
        {
            AvailableCount++;
            return default;
        }

        public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken)
        {
            LostCount++;
            cancellation?.Cancel();
            return default;
        }

        public void OnTransientFailure(Exception exception)
        {
            TransientFailures.Add(exception);
        }
    }

    private sealed class CancellationOrderingListener : IClusterAuthorityListener
    {
        private readonly CancellationTokenSource cancellation;
        private readonly Func<bool> roundCancellationObserved;

        public CancellationOrderingListener(
            CancellationTokenSource cancellation,
            Func<bool> roundCancellationObserved)
        {
            this.cancellation = cancellation;
            this.roundCancellationObserved = roundCancellationObserved;
        }

        public int AvailableCount { get; private set; }

        public int LostCount { get; private set; }

        public bool RoundWasCanceledBeforeAuthorityLoss { get; private set; }

        public ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken)
        {
            AvailableCount++;
            return default;
        }

        public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken)
        {
            LostCount++;
            RoundWasCanceledBeforeAuthorityLoss = roundCancellationObserved();
            cancellation.Cancel();
            return default;
        }

        public void OnTransientFailure(Exception exception)
        {
        }
    }

    private sealed class AdvancingDelay : IMembershipControlDelay
    {
        private readonly ManualTimeProvider clock;

        public AdvancingDelay(ManualTimeProvider clock)
        {
            this.clock = clock;
        }

        public List<TimeSpan> CompletedDelays { get; } = new();

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompletedDelays.Add(delay);
            clock.Advance(delay);
            return default;
        }
    }

    private sealed class MaximumJitterRandom : Random
    {
        protected override double Sample()
        {
            return 1.0;
        }
    }

    private sealed class ZeroJitterRandom : Random
    {
        protected override double Sample()
        {
            return 0;
        }
    }

    private sealed class CancelAfterFailuresListener : IClusterAuthorityListener
    {
        private readonly CancellationTokenSource cancellation;
        private readonly int failureLimit;
        private int failures;

        public CancelAfterFailuresListener(
            CancellationTokenSource cancellation,
            int failureLimit)
        {
            this.cancellation = cancellation;
            this.failureLimit = failureLimit;
        }

        public ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken)
        {
            return default;
        }

        public void OnTransientFailure(Exception exception)
        {
            failures++;
            if (failures >= failureLimit)
            {
                cancellation.Cancel();
            }
        }
    }

    private sealed class FailFirstAvailableListener : IClusterAuthorityListener
    {
        private readonly CancellationTokenSource cancellation;

        public FailFirstAvailableListener(CancellationTokenSource cancellation)
        {
            this.cancellation = cancellation;
        }

        public int AvailableAttempts { get; private set; }

        public List<Exception> TransientFailures { get; } = new();

        public ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken)
        {
            AvailableAttempts++;
            if (AvailableAttempts == 1)
            {
                throw new IOException("recovery dependency is temporarily unavailable");
            }

            cancellation.Cancel();
            return default;
        }

        public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken) => default;

        public void OnTransientFailure(Exception exception)
        {
            TransientFailures.Add(exception);
        }
    }

    private sealed class StubMembership : IClusterMembership
    {
        public StubMembership(ClusterMembershipSnapshot current)
        {
            Current = current;
        }

        public ClusterMembershipSnapshot Current { get; }

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
