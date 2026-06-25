using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

public sealed class ClientNotificationCommandBinder
{
    private readonly LocalClientNotificationCommandDispatcher _dispatcher;

    public ClientNotificationCommandBinder(LocalClientNotificationCommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Bind(RpcServiceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(
            ClusterClientNotificationProtocol.ServiceId,
            ClusterClientNotificationProtocol.DispatchMethodId,
            DispatchAsync);
    }

    public static void Bind(
        RpcServiceRegistry registry,
        LocalClientNotificationCommandDispatcher dispatcher)
    {
        new ClientNotificationCommandBinder(dispatcher).Bind(registry);
    }

    private async ValueTask<TransportFrame> DispatchAsync(
        RpcSession session,
        RpcRequestFrame request,
        CancellationToken cancellationToken)
    {
        var dto = session.Serializer.Deserialize<ClientNotificationDispatchRequest>(request.Payload.Memory);
        var status = dto.Command is null
            ? ClientNotificationStatus.Failed
            : await _dispatcher.DispatchAsync(dto.Command, cancellationToken).ConfigureAwait(false);
        using var payload = session.Serializer.SerializeFrame(new ClientNotificationDispatchReply
        {
            Status = (int)status
        });
        return RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, payload.Memory);
    }
}
