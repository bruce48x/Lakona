using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Sessions;

public sealed class ClusterClientNotificationDispatcher : IClientNotificationRemoteDispatcher
{
    private readonly IClusterClientFactory _clientFactory;

    public ClusterClientNotificationDispatcher(IClusterClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async ValueTask<ClientNotificationStatus> DispatchAsync(
        RouteLocation target,
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await _clientFactory.GetClientAsync(target, cancellationToken)
                .ConfigureAwait(false);
            var reply = await client.CallAsync(
                ClusterClientNotificationProtocol.DispatchMethod,
                new ClientNotificationDispatchRequest { Command = command },
                cancellationToken).ConfigureAwait(false);
            return Enum.IsDefined(typeof(ClientNotificationStatus), reply.Status)
                ? (ClientNotificationStatus)reply.Status
                : ClientNotificationStatus.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return ClientNotificationStatus.Failed;
        }
        catch
        {
            return ClientNotificationStatus.Failed;
        }
    }
}
