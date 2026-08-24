using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hosting;

/// <summary>
/// Restores one runtime subsystem before a recovering cluster node can become ready.
/// </summary>
public interface IClusterRecoveryParticipant
{
    /// <summary>
    /// Gets the stable, low-cardinality participant name used in diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the participant order. Lower values run first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Restores and validates this subsystem against the committed membership view.
    /// </summary>
    /// <param name="context">The exact local node and committed recovery view.</param>
    /// <param name="cancellationToken">Cancels host recovery.</param>
    ValueTask RecoverAsync(
        ClusterRecoveryContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Describes the committed membership state a recovery participant must restore against.
/// </summary>
public sealed class ClusterRecoveryContext
{
    public ClusterRecoveryContext(
        NodeReference localNode,
        ClusterMembershipSnapshot membership)
    {
        LocalNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        Membership = membership ?? throw new ArgumentNullException(nameof(membership));
        if (!membership.TryGetMember(localNode, out var member)
            || member is null)
        {
            throw new ArgumentException(
                "The local node incarnation must exist in the committed membership view.",
                nameof(localNode));
        }

        if (member.State != ClusterMemberState.Joining)
        {
            throw new ArgumentException(
                "Cluster recovery requires the local member to be in the joining state.",
                nameof(membership));
        }
    }

    public NodeReference LocalNode { get; }

    public ClusterMembershipSnapshot Membership { get; }
}

/// <summary>
/// Reports which recovery participant prevented the node from becoming ready.
/// </summary>
public sealed class ClusterRecoveryException : Exception
{
    public ClusterRecoveryException(string participantName, Exception innerException)
        : base($"Cluster recovery participant '{participantName}' failed.", innerException)
    {
        if (string.IsNullOrWhiteSpace(participantName))
        {
            throw new ArgumentException("Recovery participant name is required.", nameof(participantName));
        }

        ParticipantName = participantName;
    }

    public string ParticipantName { get; }
}
