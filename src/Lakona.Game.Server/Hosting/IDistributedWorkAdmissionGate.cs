namespace Lakona.Game.Server.Hosting;

/// <summary>
/// Controls admission of work that requires current distributed ownership authority.
/// </summary>
public interface IDistributedWorkAdmissionGate
{
    /// <summary>
    /// Gets whether new distributed work may currently be admitted.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Attempts to admit one unit of distributed work.
    /// </summary>
    /// <param name="admission">A token that must be exited exactly once when admitted.</param>
    /// <returns><see langword="true"/> when the work was admitted.</returns>
    bool TryEnter(out DistributedWorkAdmission admission);

    /// <summary>
    /// Marks one previously admitted unit of distributed work as complete.
    /// </summary>
    /// <param name="admission">The token returned by <see cref="TryEnter"/>.</param>
    void Exit(DistributedWorkAdmission admission);
}

/// <summary>
/// Identifies one successful admission under a particular gate generation.
/// </summary>
public readonly struct DistributedWorkAdmission
{
    private readonly DistributedWorkAdmissionLease? lease;
    private readonly long leaseVersion;

    internal DistributedWorkAdmission(
        int generation,
        DistributedWorkAdmissionLease lease,
        long leaseVersion)
    {
        Generation = generation;
        this.lease = lease;
        this.leaseVersion = leaseVersion;
    }

    /// <summary>
    /// Gets whether this token represents successfully admitted work.
    /// </summary>
    public bool IsAdmitted => lease is not null;

    internal int Generation { get; }

    internal bool TryComplete()
    {
        return lease is not null && lease.TryComplete(leaseVersion);
    }

    internal DistributedWorkAdmissionLease? Lease => lease;
}

internal sealed class DistributedWorkAdmissionLease
{
    private long activeVersion;
    private long nextVersion;

    internal long Activate()
    {
        var version = Interlocked.Increment(ref nextVersion);
        if (version <= 0)
        {
            throw new InvalidOperationException("Distributed-work admission lease version is exhausted.");
        }

        Volatile.Write(ref activeVersion, version);
        return version;
    }

    internal bool TryComplete(long version)
    {
        return version > 0 &&
            Interlocked.CompareExchange(ref activeVersion, 0, version) == version;
    }
}
