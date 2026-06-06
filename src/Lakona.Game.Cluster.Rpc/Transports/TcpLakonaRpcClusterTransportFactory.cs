using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Tcp;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class TcpULinkRpcClusterTransportFactory : IULinkRpcClusterTransportFactory
    {
        public async ValueTask<ITransport> ConnectAsync(
            RouteLocation target,
            ULinkRpcClusterEndpoint endpoint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transport = new TcpTransport(endpoint.Host, endpoint.Port);
            await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return transport;
        }
    }
}
