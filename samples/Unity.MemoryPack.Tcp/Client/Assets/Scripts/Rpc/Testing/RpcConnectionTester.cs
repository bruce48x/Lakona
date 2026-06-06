using Lakona.Rpc.Client;
using UnityEngine;

namespace Rpc.Testing
{
    public sealed class RpcConnectionTester : RpcConnectionTesterBase
    {
        [SerializeField] private RpcEndpointSettings _endpoint = RpcEndpointSettings.CreateTcp("127.0.0.1", 20000);

        protected override RpcEndpointSettings Endpoint => _endpoint;
        protected override string RuntimeTitle => "RpcCall.MemoryPack Runtime";
        protected override RpcTransportKind TransportKind => RpcTransportKind.Tcp;

        protected override RpcClientOptions CreateClientOptions()
        {
            return new RpcClientOptions(
                new global::Lakona.Rpc.Transport.Tcp.TcpTransport(_endpoint.Host, _endpoint.Port),
                new global::Lakona.Rpc.Serializer.MemoryPack.MemoryPackRpcSerializer());
        }
    }
}
