using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server.Observability;

internal static class RpcServerTelemetry
{
    internal const string CanceledOutcome = "canceled";
    internal const string ConnectionClosedOutcome = "connection_closed";
    internal const string FailureOutcome = "failure";
    internal const string ResponseOutcome = "response";

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
        description: "RPC request end-to-end duration through its terminal outcome.");

    private static readonly Histogram<double> RequestQueueDuration = Meter.CreateHistogram<double>(
        "lakona.rpc.server.request.queue.duration",
        unit: "s",
        description: "Time an RPC request waits for a Session concurrency slot.");

    private static readonly Counter<long> RequestOutcome = Meter.CreateCounter<long>(
        "lakona.rpc.server.request.outcome",
        unit: "{request}",
        description: "Terminal outcomes for RPC requests accepted by the Session.");

    internal static void RecordRequestStarted(RpcMethodDescriptor? route)
    {
        RequestStarted.Add(1, RouteTags(route));
    }

    internal static void RecordRequestQueueDuration(
        RpcMethodDescriptor? route,
        TimeSpan elapsed)
    {
        RequestQueueDuration.Record(elapsed.TotalSeconds, RouteTags(route));
    }

    internal static void RecordRequestOutcome(
        RpcMethodDescriptor? route,
        string outcome,
        RpcStatus? status,
        TimeSpan elapsed)
    {
        var tags = RouteTags(route);
        tags.Add("lakona.rpc.request.outcome", outcome);
        if (status is { } value)
        {
            tags.Add("lakona.rpc.response.status_code", (int)value);
        }

        RequestOutcome.Add(1, tags);
        RequestDuration.Record(elapsed.TotalSeconds, tags);
    }

    private static TagList RouteTags(RpcMethodDescriptor? route)
    {
        var tags = new TagList { { "rpc.system", "lakona" } };
        if (route is null)
        {
            tags.Add("lakona.rpc.route", "unknown");
        }
        else
        {
            tags.Add("lakona.rpc.service.id", route.ServiceId);
            tags.Add("lakona.rpc.method.id", route.MethodId);
        }
        return tags;
    }
}
