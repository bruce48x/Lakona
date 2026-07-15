using FrameworkBenchmark.Lakona.Contracts;

namespace FrameworkBenchmark.Lakona.Server;

public sealed class EchoService : IEchoService
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
}
