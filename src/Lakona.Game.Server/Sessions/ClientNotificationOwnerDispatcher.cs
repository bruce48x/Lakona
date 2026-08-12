using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.ReliablePush;

namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotificationOwnerDispatcher
{
    private readonly IReliablePushRuntime _owner;
    private readonly IClusterMembership _membership;
    private readonly NodeId _localNode;
    private readonly IDistributedWorkAdmissionGate? _admissionGate;

    public ClientNotificationOwnerDispatcher(
        IReliablePushRuntime owner,
        IClusterMembership membership,
        NodeId localNode,
        IDistributedWorkAdmissionGate? admissionGate = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _membership = membership ?? throw new ArgumentNullException(nameof(membership));
        _localNode = localNode;
        _admissionGate = admissionGate;
    }

    public async ValueTask<ClientNotificationStatus> DispatchAsync(
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.OwnerKey) ||
            string.IsNullOrWhiteSpace(command.SessionId))
        {
            return ClientNotificationStatus.Failed;
        }

        var session = new GameSessionKey(
            command.OwnerKey,
            command.SessionId);
        var route = MembershipSessionLocator.TryResolve(session, _membership, out var target)
            ? target
            : null;
        if (route is null
            || route!.Node != _localNode)
        {
            return MembershipSessionLocator.ClassifyMissing(command.SessionId, _membership);
        }

        DistributedWorkAdmission admission = default;
        if (_admissionGate is not null && !_admissionGate.TryEnter(out admission))
        {
            return ClientNotificationStatus.Failed;
        }

        try
        {
            return await _owner.PublishAsync(session, command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (admission.IsAdmitted)
            {
                _admissionGate!.Exit(admission);
            }
        }
    }
}
