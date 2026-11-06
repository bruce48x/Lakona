using System;
using Lakona.Rpc.Client;
using Rpc.Testing;

namespace Lakona.SampleTests
{
    public sealed class ShutdownProbe : RpcConnectionTester
    {
        public Func<RpcClientOptions> Options;

        protected override RpcClientOptions CreateClientOptions() => Options();
    }
}
