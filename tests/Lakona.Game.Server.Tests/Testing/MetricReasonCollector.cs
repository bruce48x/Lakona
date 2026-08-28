using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Lakona.Game.Server.Tests.Testing;

internal sealed class MetricReasonCollector : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly string tagName;

    public MetricReasonCollector(string meterName, string instrumentName, string tagName)
    {
        this.tagName = tagName;
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => Collect(tags));
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) => Collect(tags));
        listener.Start();
    }

    public ConcurrentBag<string> Reasons { get; } = [];

    public void Dispose() => listener.Dispose();

    private void Collect(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == tagName && tag.Value is string reason)
            {
                Reasons.Add(reason);
            }
        }
    }
}
