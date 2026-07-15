using FrameworkBenchmark.Lakona.Contracts;
using FrameworkBenchmark.Lakona.Server.Generated;
using Lakona.Rpc.Client;

namespace FrameworkBenchmark.Lakona.Server;

public sealed class EchoService(RpcClient workerClient) : IEchoService
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
        return await workerClient.Api.Benchmark.Worker.EchoAsync(request);
    }
}

public sealed class WorkerService : IWorkerService
{
    public ValueTask<EchoResponse> EchoAsync(EchoRequest request)
    {
        return new ValueTask<EchoResponse>(new EchoResponse
        {
            RequestId = request.RequestId,
            Payload = request.Payload,
            TerminalNode = "worker-1"
        });
    }
}
