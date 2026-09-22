using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Rpc.Tests;

internal static class TestRpcSession
{
    public static RpcSession Create(
        ITransport transport,
        IRpcSerializer serializer,
        bool ownsTransport,
        RpcServiceRegistry? registry = null,
        string? connectionId = null) =>
        new(transport, serializer, registry ?? new RpcServiceRegistry(),
            connectionId ?? Guid.NewGuid().ToString("N"), ownsTransport);
}
