using Lakona.Rpc.Core;
using MemoryPack;

namespace FrameworkBenchmark.Lakona.Contracts;

[RpcService(1, ApiGroup = "Benchmark", ApiName = "Echo")]
public interface IEchoService
{
    [RpcMethod(1)]
    ValueTask<EchoResponse> EchoAsync(EchoRequest request);

    [RpcMethod(2)]
    ValueTask<EchoResponse> DirectAsync(EchoRequest request);

    [RpcMethod(3)]
    ValueTask<EchoResponse> RoutedAsync(EchoRequest request);
}

[RpcService(2, ApiGroup = "Benchmark", ApiName = "Worker")]
public interface IWorkerService
{
    [RpcMethod(1)]
    ValueTask<EchoResponse> EchoAsync(EchoRequest request);
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class EchoRequest
{
    [MemoryPackOrder(0)]
    public long RequestId { get; set; }

    [MemoryPackOrder(1)]
    public byte[] Payload { get; set; } = [];

    [MemoryPackOrder(2)]
    public string TargetKey { get; set; } = string.Empty;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class EchoResponse
{
    [MemoryPackOrder(0)]
    public long RequestId { get; set; }

    [MemoryPackOrder(1)]
    public byte[] Payload { get; set; } = [];

    [MemoryPackOrder(2)]
    public string TerminalNode { get; set; } = string.Empty;
}
