using FrameworkBenchmark.Lakona.Contracts;
using FrameworkBenchmark.Lakona.Peers.Generated;
using FrameworkBenchmark.Lakona.Server;
using Lakona.Game.Client;
using Lakona.Game.Server;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Tcp;
using Microsoft.Extensions.Configuration;

namespace FrameworkBenchmark.Lakona.Hotfix;

[HotfixComponent]
public sealed class BenchmarkRuntime(BenchmarkState state, IConfiguration configuration)
{
    [ComponentStart]
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        if (configuration["Lakona:Node:Id"] != "frontdoor-1") return;
        var port = int.Parse(configuration["Benchmark:Worker1Port"]!, System.Globalization.CultureInfo.InvariantCulture);
        state.Worker = new LakonaGameClient(new LakonaGameClientOptions(
            new TcpTransport("127.0.0.1", port), new MemoryPackRpcSerializer()));
        await state.Worker.ConnectAsync(cancellationToken);
    }

    public ValueTask<EchoResponse> DirectAsync(EchoRequest request) => state.Worker!.Api.Benchmark.Worker.EchoAsync(request);
}
