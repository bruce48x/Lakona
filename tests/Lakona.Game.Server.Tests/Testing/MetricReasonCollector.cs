using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Lakona.Game.Server.Tests.Testing;

internal sealed class MetricReasonCollector : IDisposable
{
    private readonly MeterListener listener = new();

    public MetricReasonCollector(string meterName, string instrumentName, string tagName)
    {
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == tagName && tag.Value is string reason)
                {
                    Reasons.Add(reason);
                }
            }
        });
        listener.Start();
    }

    public ConcurrentBag<string> Reasons { get; } = [];

    public void Dispose() => listener.Dispose();
}
