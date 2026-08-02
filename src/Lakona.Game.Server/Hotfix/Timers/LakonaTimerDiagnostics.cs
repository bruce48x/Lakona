using System.Diagnostics.Metrics;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerDiagnostics : IDisposable
{
    public const string MeterName = "Lakona.Game.Timer";

    private readonly Meter meter = new(
        MeterName,
        typeof(LakonaTimerDiagnostics).Assembly.GetName().Version?.ToString());
    private readonly Counter<long> capacityRejections;
    private readonly ObservableGauge<long> activeTimers;
    private readonly ObservableGauge<long> heapEntries;
    private readonly ObservableGauge<long> staleHeapEntries;

    public LakonaTimerDiagnostics(Func<LakonaTimerPopulation> observePopulation)
    {
        ArgumentNullException.ThrowIfNull(observePopulation);
        activeTimers = meter.CreateObservableGauge(
            "lakona-game.timer.active",
            () => (long)observePopulation().ActiveTimers);
        heapEntries = meter.CreateObservableGauge(
            "lakona-game.timer.heap.entries",
            () => (long)observePopulation().HeapEntries);
        staleHeapEntries = meter.CreateObservableGauge(
            "lakona-game.timer.heap.stale",
            () => (long)observePopulation().StaleHeapEntries);
        capacityRejections = meter.CreateCounter<long>(
            "lakona-game.timer.capacity.rejected");
    }

    public void RecordCapacityRejection()
    {
        capacityRejections.Add(1);
    }

    public void Dispose()
    {
        meter.Dispose();
    }
}

internal readonly record struct LakonaTimerPopulation(
    int ActiveTimers,
    int HeapEntries,
    int StaleHeapEntries);
