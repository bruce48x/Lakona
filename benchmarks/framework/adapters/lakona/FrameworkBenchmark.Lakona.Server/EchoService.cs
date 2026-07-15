using FrameworkBenchmark.Lakona.Contracts;
using FrameworkBenchmark.Lakona.Server.Generated;
using Lakona.Game.Cluster;
using Lakona.Rpc.Client;

namespace FrameworkBenchmark.Lakona.Server;

public sealed class EchoService(
    RpcClient worker1Client,
    RpcClient worker2Client,
    IRouteDirectory routeDirectory) : IEchoService
{
    public ValueTask<EchoResponse> EchoAsync(EchoRequest request)
    {
        return new ValueTask<EchoResponse>(new EchoResponse
        {
            RequestId = request.RequestId,
            Payload = request.Payload,
            TerminalNode = "frontdoor-1"
        });
    }
    public async ValueTask<EchoResponse> DirectAsync(EchoRequest request)
    {
        return await worker1Client.Api.Benchmark.Worker.EchoAsync(request);
    }

    public async ValueTask<EchoResponse> RoutedAsync(EchoRequest request)
    {
        var location = await routeDirectory.ResolveAsync(request.TargetKey, DateTimeOffset.UtcNow)
            ?? throw new InvalidOperationException($"Route '{request.TargetKey}' is not registered.");
        var client = location.Node.Value == "worker-1" ? worker1Client : worker2Client;
        return await client.Api.Benchmark.Worker.EchoAsync(request);
    }
}

public sealed class WorkerService(string nodeId) : IWorkerService
{
    public ValueTask<EchoResponse> EchoAsync(EchoRequest request)
    {
        return new ValueTask<EchoResponse>(new EchoResponse
        {
            RequestId = request.RequestId,
            Payload = request.Payload,
            TerminalNode = nodeId
        });
    }
}
