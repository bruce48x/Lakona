using FrameworkBenchmark.Lakona.Contracts;
using FrameworkBenchmark.Lakona.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace FrameworkBenchmark.Lakona.Hotfix;

[HotfixBehaviorOf(typeof(BenchmarkActor))]
public sealed partial class BenchmarkBehavior(LocalActorNodeIdentity node)
{
    public ValueTask<EchoResponse> EchoAsync(BenchmarkActor self, EchoRequest request) => new(new EchoResponse
    {
        RequestId = request.RequestId, Payload = request.Payload, TerminalNode = node.NodeId.Value
    });
}
