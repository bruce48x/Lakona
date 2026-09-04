using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server.Observability;
using Xunit;

namespace Lakona.Rpc.Tests;

[Collection(RpcTelemetryCollectionNames.Diagnostics)]
public sealed class RpcServerTelemetryTests
{
    [Fact]
    public void Records_queue_duration_and_terminal_outcome_with_bounded_attributes()
    {
        using var listener = new MeterListener();
        var measurements = new ConcurrentQueue<Measurement>();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == LakonaRpcServerTelemetry.MeterName)
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Enqueue(new(instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Enqueue(new(instrument.Name, value, tags.ToArray())));
        listener.Start();

        RpcServerTelemetry.RecordRequestStarted(10, 20);
        RpcServerTelemetry.RecordRequestQueueDuration(10, 20, TimeSpan.FromMilliseconds(5));
        RpcServerTelemetry.RecordRequestOutcome(
            10,
            20,
            RpcServerTelemetry.ResponseOutcome,
            RpcStatus.Ok,
            TimeSpan.FromMilliseconds(25));

        var snapshot = measurements.ToArray();
        Assert.Contains(snapshot, measurement =>
            measurement.Name == "lakona.rpc.server.request.started"
            && measurement.Value == 1
            && HasTag(measurement, "lakona.rpc.service.id", 10)
            && HasTag(measurement, "lakona.rpc.method.id", 20));
        Assert.Contains(snapshot, measurement =>
            measurement.Name == "lakona.rpc.server.request.queue.duration"
            && measurement.Value == 0.005
            && HasTag(measurement, "lakona.rpc.service.id", 10)
            && HasTag(measurement, "lakona.rpc.method.id", 20));
        Assert.Contains(snapshot, measurement =>
            measurement.Name == "lakona.rpc.server.request.outcome"
            && measurement.Value == 1
            && HasTag(measurement, "lakona.rpc.service.id", 10)
            && HasTag(measurement, "lakona.rpc.method.id", 20)
            && HasTag(measurement, "lakona.rpc.request.outcome", "response")
            && HasTag(measurement, "lakona.rpc.response.status_code", (int)RpcStatus.Ok));
        Assert.Contains(snapshot, measurement =>
            measurement.Name == "lakona.rpc.server.request.duration"
            && measurement.Value == 0.025
            && HasTag(measurement, "lakona.rpc.service.id", 10)
            && HasTag(measurement, "lakona.rpc.method.id", 20)
            && HasTag(measurement, "lakona.rpc.request.outcome", "response")
            && HasTag(measurement, "lakona.rpc.response.status_code", (int)RpcStatus.Ok));
    }

    private static bool HasTag(Measurement measurement, string key, object value)
    {
        return measurement.Tags.Any(tag => tag.Key == key && Equals(tag.Value, value));
    }

    private sealed record Measurement(
        string Name,
        double Value,
        IReadOnlyList<KeyValuePair<string, object?>> Tags);
}
