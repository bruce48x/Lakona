using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server.Observability;

internal static class RpcServerTelemetry
{
    private static readonly Meter Meter = new(
        LakonaRpcServerTelemetry.MeterName,
        typeof(RpcServerTelemetry).Assembly.GetName().Version?.ToString());

    private static readonly Counter<long> RequestStarted = Meter.CreateCounter<long>(
        "lakona.rpc.server.request.started",
        unit: "{request}",
        description: "RPC requests accepted for dispatch.");

    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "lakona.rpc.server.request.duration",
        unit: "s",
        description: "RPC request dispatch duration.");

    internal static void RecordRequestStarted(int serviceId, int methodId)
    {
        RequestStarted.Add(1, RouteTags(serviceId, methodId));
    }

    internal static void RecordRequestCompleted(
        int serviceId,
        int methodId,
        RpcStatus? status,
        TimeSpan elapsed)
    {
        var tags = RouteTags(serviceId, methodId);
        if (status is { } value)
        {
            tags.Add("lakona.rpc.response.status_code", (int)value);
        }

        RequestDuration.Record(elapsed.TotalSeconds, tags);
    }

    private static TagList RouteTags(int serviceId, int methodId)
    {
        return new TagList
        {
            { "rpc.system", "lakona" },
            { "lakona.rpc.service.id", serviceId },
            { "lakona.rpc.method.id", methodId }
        };
    }
}
