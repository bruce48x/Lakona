using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Tcp;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class TcpClusterTransportFactory : IClusterTransportFactory
    {
        public async ValueTask<ITransport> ConnectAsync(
            RouteLocation target,
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transport = new TcpTransport(endpoint.Host, endpoint.Port);
            await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return transport;
        }
    }
}
