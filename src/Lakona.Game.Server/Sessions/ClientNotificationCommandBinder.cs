using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

public sealed class ClientNotificationCommandBinder
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
        registry.Register(
            ClusterClientNotificationProtocol.ServiceId,
            ClusterClientNotificationProtocol.DispatchMethodId,
            DispatchAsync);
        registry.Register(
            ClusterClientNotificationProtocol.ServiceId,
            ClusterClientNotificationProtocol.BatchDispatchMethodId,
            DispatchBatchAsync);
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

    private async ValueTask<TransportFrame> DispatchAsync(
        RpcSession session,
        RpcRequestFrame request,
        CancellationToken cancellationToken)
    {
        var dto = session.Serializer.Deserialize<ClientNotificationDispatchRequest>(request.Payload.Memory);
        var status = dto.Command is null
            ? ClientNotificationStatus.Failed
            : await _dispatch(dto.Command, cancellationToken).ConfigureAwait(false);
        using var payload = session.Serializer.SerializeFrame(new ClientNotificationDispatchReply
        {
            Status = (int)status
        });
        return RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, payload.Memory);
    }

    private async ValueTask<TransportFrame> DispatchBatchAsync(
        RpcSession session,
        RpcRequestFrame request,
        CancellationToken cancellationToken)
    {
        var dto = session.Serializer.Deserialize<ClientNotificationBatchDispatchRequest>(
            request.Payload.Memory);
        var commands = dto.Commands ?? [];
        var statuses = new int[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            statuses[i] = (int)await _dispatch(commands[i], cancellationToken)
                .ConfigureAwait(false);
        }

        using var payload = session.Serializer.SerializeFrame(
            new ClientNotificationBatchDispatchReply { Statuses = statuses });
        return RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, payload.Memory);
    }
}
