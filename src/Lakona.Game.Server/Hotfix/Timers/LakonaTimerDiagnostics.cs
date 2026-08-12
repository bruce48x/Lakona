using System.Diagnostics.Metrics;
using Lakona.Game.Server.Observability;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerDiagnostics : IDisposable
{
    public const string MeterName = LakonaGameServerTelemetry.TimerMeterName;

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
            "lakona.game.timer.active",
            () => (long)observePopulation().ActiveTimers,
            unit: "{timer}",
            description: "Active Hotfix timers.");
        heapEntries = meter.CreateObservableGauge(
            "lakona.game.timer.heap.entries",
            () => (long)observePopulation().HeapEntries,
            unit: "{timer}",
            description: "Entries in the Hotfix timer scheduling heap.");
        staleHeapEntries = meter.CreateObservableGauge(
            "lakona.game.timer.heap.stale",
            () => (long)observePopulation().StaleHeapEntries,
            unit: "{timer}",
            description: "Stale entries in the Hotfix timer scheduling heap.");
        capacityRejections = meter.CreateCounter<long>(
            "lakona.game.timer.capacity.rejected",
            unit: "{timer}",
            description: "Hotfix timer registrations rejected by capacity limits.");
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
