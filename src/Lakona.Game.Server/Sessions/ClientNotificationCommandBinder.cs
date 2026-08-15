using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotificationCommandBinder
{
    private readonly Func<ClientNotificationCommand, CancellationToken, ValueTask<ClientNotificationStatus>> _dispatch;

    public ClientNotificationCommandBinder(LocalClientNotificationCommandDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatch = dispatcher.DispatchAsync;
    }

    private ClientNotificationCommandBinder(ClientNotificationOwnerDispatcher ownerDispatcher)
    {
        ArgumentNullException.ThrowIfNull(ownerDispatcher);
        _dispatch = ownerDispatcher.DispatchAsync;
    }

    public void Bind(RpcServiceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var service = registry.RegisterPerConnection<ClientNotificationCommandBinder>(
            ClusterClientNotificationProtocol.ServiceId,
            (_, _) => this,
            serviceName: nameof(ClientNotificationCommandBinder));
        service.Register<ClientNotificationDispatchRequest, ClientNotificationDispatchReply>(
            ClusterClientNotificationProtocol.DispatchMethodId,
            static (binder, request, cancellationToken) =>
                binder.DispatchAsync(request, cancellationToken),
            methodName: nameof(DispatchAsync));
        service.Register<ClientNotificationBatchDispatchRequest, ClientNotificationBatchDispatchReply>(
            ClusterClientNotificationProtocol.BatchDispatchMethodId,
            static (binder, request, cancellationToken) =>
                binder.DispatchBatchAsync(request, cancellationToken),
            methodName: nameof(DispatchBatchAsync));
    }

    public static void Bind(
        RpcServiceRegistry registry,
        LocalClientNotificationCommandDispatcher dispatcher)
    {
        new ClientNotificationCommandBinder(dispatcher).Bind(registry);
    }

    internal static void BindOwned(
        RpcServiceRegistry registry,
        ClientNotificationOwnerDispatcher ownerDispatcher)
    {
        new ClientNotificationCommandBinder(ownerDispatcher).Bind(registry);
    }

    private async ValueTask<ClientNotificationDispatchReply> DispatchAsync(
        ClientNotificationDispatchRequest request,
        CancellationToken cancellationToken)
    {
        var status = request.Command is null
            ? ClientNotificationStatus.Failed
            : await _dispatch(request.Command, cancellationToken).ConfigureAwait(false);
        return new ClientNotificationDispatchReply
        {
            Status = (int)status
        };
    }

    private async ValueTask<ClientNotificationBatchDispatchReply> DispatchBatchAsync(
        ClientNotificationBatchDispatchRequest request,
        CancellationToken cancellationToken)
    {
        var commands = request.Commands ?? [];
        var statuses = new int[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            statuses[i] = (int)await _dispatch(commands[i], cancellationToken)
                .ConfigureAwait(false);
        }

        return new ClientNotificationBatchDispatchReply { Statuses = statuses };
    }
}
