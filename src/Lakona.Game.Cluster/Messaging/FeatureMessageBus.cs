using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public sealed class FeatureMessageBus : IFeatureMessageBus
    {
        private static readonly TimeSpan DefaultRequestTtl = TimeSpan.FromSeconds(30);

        private readonly IClusterNodeDiscovery _nodeDiscovery;
        private readonly IFeatureMessageTransport _transport;
        private readonly IFeatureMessageSerializer _serializer;
        private readonly NodeId _sourceNode;
        private readonly TimeSpan _requestTtl;
        private readonly Func<DateTimeOffset> _utcNow;

        public FeatureMessageBus(
            IClusterNodeDiscovery nodeDiscovery,
            IFeatureMessageTransport transport,
            IFeatureMessageSerializer serializer,
            NodeId? sourceNode = null,
            TimeSpan? requestTtl = null,
            Func<DateTimeOffset>? utcNow = null)
        {
            _nodeDiscovery = nodeDiscovery ?? throw new ArgumentNullException(nameof(nodeDiscovery));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _sourceNode = sourceNode ?? new NodeId("local");
            _requestTtl = requestTtl ?? DefaultRequestTtl;
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public ValueTask<FeatureMessageReply> SendToFeatureAsync<TRequest, TReply>(
            FeatureName feature,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            return SendToFeatureAsync<TRequest, TReply>(
                feature,
                GetDefaultKind(request),
                request,
                cancellationToken);
        }

        public async ValueTask<FeatureMessageReply> SendToFeatureAsync<TRequest, TReply>(
            FeatureName feature,
            string kind,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = await _nodeDiscovery.AnyAsync(feature, cancellationToken)
                .ConfigureAwait(false);
            if (target is null)
            {
                return new FeatureMessageReply(ClusterSendStatus.FeatureNotFound, Array.Empty<byte>());
            }

            if (target.State != NodeState.Ready || !target.Endpoints.ContainsKey("cluster"))
            {
                return new FeatureMessageReply(ClusterSendStatus.NodeUnavailable, Array.Empty<byte>());
            }

            ReadOnlyMemory<byte> payload;
            try
            {
                payload = _serializer.Serialize(request);
            }
            catch (Exception ex)
            {
                return new FeatureMessageReply(
                    ClusterSendStatus.SerializationFailed,
                    Array.Empty<byte>(),
                    ex.Message);
            }

            var now = _utcNow();
            var message = new FeatureMessageRequest(
                feature,
                kind,
                payload,
                now.Add(_requestTtl),
                _sourceNode,
                Guid.NewGuid().ToString("N"));

            try
            {
                return await _transport.SendAsync(target, message, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException ex)
            {
                return new FeatureMessageReply(ClusterSendStatus.Timeout, Array.Empty<byte>(), ex.Message);
            }
            catch (Exception ex)
            {
                return new FeatureMessageReply(ClusterSendStatus.Failed, Array.Empty<byte>(), ex.Message);
            }
        }

        private static string GetDefaultKind<TRequest>(TRequest request)
        {
            if (request is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return typeof(TRequest).FullName ?? typeof(TRequest).Name;
        }
    }
}
