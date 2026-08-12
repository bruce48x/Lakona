using System.Diagnostics.Metrics;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server.Observability;
using Xunit;

namespace Lakona.Rpc.Tests;

public sealed class RpcServerTelemetryTests
{
    [Fact]
    public void Records_request_count_and_duration_with_bounded_route_attributes()
    {
        using var listener = new MeterListener();
        var measurements = new List<Measurement>();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == LakonaRpcServerTelemetry.MeterName)
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new(instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new(instrument.Name, value, tags.ToArray())));
        listener.Start();

        RpcServerTelemetry.RecordRequestStarted(10, 20);
        RpcServerTelemetry.RecordRequestCompleted(10, 20, RpcStatus.Ok, TimeSpan.FromMilliseconds(25));

        Assert.Contains(measurements, measurement =>
            measurement.Name == "lakona.rpc.server.request.started"
            && measurement.Value == 1
            && HasTag(measurement, "lakona.rpc.service.id", 10)
            && HasTag(measurement, "lakona.rpc.method.id", 20));
        Assert.Contains(measurements, measurement =>
            measurement.Name == "lakona.rpc.server.request.duration"
            && measurement.Value == 0.025
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
