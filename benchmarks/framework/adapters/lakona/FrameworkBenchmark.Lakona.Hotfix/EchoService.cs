using FrameworkBenchmark.Lakona.Contracts;
using FrameworkBenchmark.Lakona.Server;
using FrameworkBenchmark.Lakona.Server.Generated;
using Lakona.Game.Server;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace FrameworkBenchmark.Lakona.Hotfix;

[HotfixService(typeof(IEchoService))]
[NodeRole("frontdoor")]
internal sealed class EchoService(LocalActorNodeIdentity node, BenchmarkRuntime runtime, ActorAccess actors)
{
    public ValueTask<EchoResponse> EchoAsync(EchoServiceCall<EchoRequest> call) => new(new EchoResponse
    {
        RequestId = call.Request.RequestId, Payload = call.Request.Payload, TerminalNode = node.NodeId.Value
    });

    public ValueTask<EchoResponse> DirectAsync(EchoServiceCall<EchoRequest> call) => runtime.DirectAsync(call.Request);

    public ValueTask<EchoResponse> RoutedAsync(EchoServiceCall<EchoRequest> call) => actors
        .Route<BenchmarkActor>(call.Request.TargetKey)
        .CallAsync(static behavior => behavior.EchoAsync, call.Request);
}

[HotfixService(typeof(IWorkerService))]
[NodeRole("worker")]
internal sealed class WorkerService(LocalActorNodeIdentity node)
{
    public ValueTask<EchoResponse> EchoAsync(WorkerServiceCall<EchoRequest> call) => new(new EchoResponse
    {
        RequestId = call.Request.RequestId, Payload = call.Request.Payload, TerminalNode = node.NodeId.Value
    });
}
