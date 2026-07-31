namespace Lakona.Rpc.Server;

/// <summary>Describes an accepted connection before its RPC Session starts.</summary>
public sealed record RpcSessionAdmissionContext(
    string ConnectionId,
    string DisplayName);

/// <summary>Allows a framework integration to admit or reject an accepted RPC connection.</summary>
public interface IRpcSessionAdmissionGate
{
    ValueTask<RpcSessionAdmissionResult> EvaluateAsync(
        RpcSessionAdmissionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Describes an RPC Session admission decision and its optional lifetime lease.</summary>
public readonly struct RpcSessionAdmissionResult
{
    private RpcSessionAdmissionResult(
        bool isAllowed,
        string? rejectionReason,
        CancellationToken sessionCancellation,
        IAsyncDisposable? lease)
    {
        IsAllowed = isAllowed;
        RejectionReason = rejectionReason;
        SessionCancellation = sessionCancellation;
        Lease = lease;
    }

    /// <summary>Gets whether the connection was admitted.</summary>
    public bool IsAllowed { get; }

    /// <summary>Gets the low-cardinality reason for a rejected connection.</summary>
    public string? RejectionReason { get; }

    /// <summary>Gets a token whose cancellation terminates the admitted RPC Session.</summary>
    public CancellationToken SessionCancellation { get; }

    /// <summary>Gets the lease released by the host after Session cleanup.</summary>
    public IAsyncDisposable? Lease { get; }

    /// <summary>Creates an allowed decision.</summary>
    public static RpcSessionAdmissionResult Allow(
        CancellationToken sessionCancellation = default,
        IAsyncDisposable? lease = null)
    {
        return new RpcSessionAdmissionResult(true, null, sessionCancellation, lease);
    }

    /// <summary>Creates a rejected decision.</summary>
    public static RpcSessionAdmissionResult Deny(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Admission rejection reason is required.", nameof(reason));

        return new RpcSessionAdmissionResult(false, reason, default, null);
    }
}
