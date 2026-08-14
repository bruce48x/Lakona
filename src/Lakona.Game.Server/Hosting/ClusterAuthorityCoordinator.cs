using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hosting;

internal interface IClusterRecoveryCompletion
{
    ValueTask CommitReadyAsync(CancellationToken cancellationToken);
}

internal sealed class ClusterAuthorityCoordinator : IClusterAuthorityListener
{
    private readonly NodeReference local;
    private readonly IClusterMembership membership;
    private readonly DistributedWorkAdmissionGate admissionGate;
    private readonly ClusterRecoveryBarrier recoveryBarrier;
    private readonly IClusterRecoveryCompletion recoveryCompletion;
    private readonly TimeSpan drainTimeout;

    public ClusterAuthorityCoordinator(
        NodeReference local,
        IClusterMembership membership,
        DistributedWorkAdmissionGate admissionGate,
        ClusterRecoveryBarrier recoveryBarrier,
        IClusterRecoveryCompletion recoveryCompletion,
        TimeSpan drainTimeout)
    {
        this.local = local ?? throw new ArgumentNullException(nameof(local));
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
        this.admissionGate = admissionGate ?? throw new ArgumentNullException(nameof(admissionGate));
        this.recoveryBarrier = recoveryBarrier ?? throw new ArgumentNullException(nameof(recoveryBarrier));
        this.recoveryCompletion = recoveryCompletion
            ?? throw new ArgumentNullException(nameof(recoveryCompletion));
        if (drainTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(drainTimeout),
                "Distributed-work drain timeout must be positive.");
        }

        this.drainTimeout = drainTimeout;
    }

    public async ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken)
    {
        var before = membership.Current;
        var localMember = RequireLocalMember(before);
        if (localMember.State == ClusterMemberState.Recovering)
        {
            await recoveryBarrier.RecoverAsync(
                new ClusterRecoveryContext(local, before),
                cancellationToken).ConfigureAwait(false);
            await recoveryCompletion.CommitReadyAsync(cancellationToken).ConfigureAwait(false);

            var committed = membership.Current;
            var ready = RequireLocalMember(committed);
            if (committed.View.CompareTo(before.View) <= 0
                || ready.State != ClusterMemberState.Ready)
            {
                throw new InvalidOperationException(
                    "Recovery completion returned before the local ready view was committed.");
            }

            ClusterDiagnostics.RecordAuthorityTransition("available");
            return;
        }

        if (localMember.State != ClusterMemberState.Ready)
        {
            throw new ClusterAuthorityFencingException(
                $"Quorum authority cannot activate local member state '{localMember.State}'.");
        }

        if (!admissionGate.IsOpen)
        {
            admissionGate.Open();
        }
        ClusterDiagnostics.RecordAuthorityTransition("available");
    }

    public async ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken)
    {
        var drained = await admissionGate.CloseAndDrainAsync(
            drainTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!drained)
        {
            throw new ClusterAuthorityFencingException(
                "Distributed work did not drain before the fencing deadline.");
        }
        ClusterDiagnostics.RecordAuthorityTransition("lost");
    }

    public void OnTransientFailure(Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        ClusterDiagnostics.RecordAuthorityTransition("transient_failure");
    }

    private ClusterMember RequireLocalMember(ClusterMembershipSnapshot snapshot)
    {
        if (snapshot.Cluster != local.Cluster
            || !snapshot.TryGetMember(local, out var member)
            || member is null)
        {
            throw new ClusterAuthorityFencingException(
                "The local node incarnation is no longer present in committed membership.");
        }

        return member;
    }
}
