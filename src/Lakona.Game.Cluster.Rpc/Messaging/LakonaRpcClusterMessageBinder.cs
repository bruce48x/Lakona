using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ULinkRpcClusterMessageBinder
    {
        private readonly IClusterMessageHandler _handler;

        public ULinkRpcClusterMessageBinder(IClusterMessageHandler handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Bind(RpcServiceRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(
                ULinkRpcClusterProtocol.ServiceId,
                ULinkRpcClusterProtocol.SendMethodId,
                HandleAsync);
        }

        public static void Bind(
            RpcServiceRegistry registry,
            IClusterMessageHandler handler)
        {
            new ULinkRpcClusterMessageBinder(handler).Bind(registry);
        }

        private async ValueTask<TransportFrame> HandleAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcClusterSendRequest>(request.Payload.Memory);
            var status = await _handler.HandleAsync(
                ULinkRpcClusterMessageConverter.ToClusterMessage(dto),
                cancellationToken).ConfigureAwait(false);

            using var payload = session.Serializer.SerializeFrame(new ULinkRpcClusterSendReply
            {
                Status = (int)status
            });
            return RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, payload.Memory);
        }
    }
}
