using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class FeatureMessageBinder
    {
        private readonly IFeatureMessageHandler _handler;

        public FeatureMessageBinder(IFeatureMessageHandler handler)
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
                ClusterProtocol.ServiceId,
                ClusterProtocol.FeatureMessageMethodId,
                HandleAsync);
        }

        public static void Bind(
            RpcServiceRegistry registry,
            IFeatureMessageHandler handler)
        {
            new FeatureMessageBinder(handler).Bind(registry);
        }

        private async ValueTask<TransportFrame> HandleAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<FeatureSendRequest>(request.Payload.Memory);
            var message = FeatureMessageConverter.ToFeatureRequest(dto);
            FeatureMessageReply reply;
            if (message.IsExpired(DateTimeOffset.UtcNow))
            {
                reply = new FeatureMessageReply(ClusterSendStatus.Expired, Array.Empty<byte>());
            }
            else
            {
                reply = await _handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
            }

            using var payload = session.Serializer.SerializeFrame(FeatureMessageConverter.ToRpcReply(reply));
            return RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, payload.Memory);
        }
    }
}
