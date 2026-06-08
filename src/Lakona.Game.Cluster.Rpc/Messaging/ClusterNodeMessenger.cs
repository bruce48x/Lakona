using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ClusterNodeMessenger : INodeMessenger
    {
        private readonly IClusterClientFactory _clientFactory;
        private readonly ClusterNodeMessengerOptions _options;

        public ClusterNodeMessenger(
            IClusterClientFactory clientFactory,
            ClusterNodeMessengerOptions? options = null)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _options = options ?? new ClusterNodeMessengerOptions();
        }

        public async ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var timeout = CreateTimeout(cancellationToken);
            var effectiveToken = timeout?.Token ?? cancellationToken;

            try
            {
                var client = await _clientFactory.GetClientAsync(target, effectiveToken).ConfigureAwait(false);
                var reply = await client.CallAsync(
                    ClusterProtocol.SendMethod,
                    ClusterMessageConverter.ToRequest(message),
                    effectiveToken).ConfigureAwait(false);

                return MapReply(reply);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout is not null)
            {
                return ClusterSendStatus.Timeout;
            }
            catch (TimeoutException)
            {
                return ClusterSendStatus.Timeout;
            }
            catch (Exception ex) when (_options.ExceptionMapper is not null)
            {
                return _options.ExceptionMapper(ex);
            }
            catch
            {
                return ClusterSendStatus.Failed;
            }
        }

        private CancellationTokenSource? CreateTimeout(CancellationToken cancellationToken)
        {
            if (!_options.SendTimeout.HasValue)
            {
                return null;
            }

            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.SendTimeout.Value);
            return timeout;
        }

        private static ClusterSendStatus MapReply(ClusterSendReply? reply)
        {
            if (reply is null)
            {
                return ClusterSendStatus.Failed;
            }

            return Enum.IsDefined(typeof(ClusterSendStatus), reply.Status)
                ? (ClusterSendStatus)reply.Status
                : ClusterSendStatus.Failed;
        }
    }
}
