using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterDiagnosticsTests
{
    [Fact]
    public void Cluster_scope_publishes_low_cardinality_operational_signals()
    {
        var measurements = new ConcurrentBag<string>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ClusterDiagnostics.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, _, _, _) =>
            measurements.Add(instrument.Name));
        meterListener.SetMeasurementEventCallback<double>((instrument, _, _, _) =>
            measurements.Add(instrument.Name));
        meterListener.Start();

        var activities = new ConcurrentBag<string>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ClusterDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity.OperationName)
        };
        ActivitySource.AddActivityListener(activityListener);

        using (ClusterDiagnostics.StartActivity("cluster.test"))
        {
            ClusterDiagnostics.RecordMembershipRequest("success", TimeSpan.FromMilliseconds(1));
            ClusterDiagnostics.RecordAuthorityTransition("available");
            ClusterDiagnostics.RecordActorLocationRecovery("success", TimeSpan.FromMilliseconds(2));
            ClusterDiagnostics.RecordActorLocationFailure(ActorLocationFailureReason.Unavailable);
        }

        Assert.Contains("lakona.game.cluster.membership.request", measurements);
        Assert.Contains("lakona.game.cluster.membership.request.duration", measurements);
        Assert.Contains("lakona.game.cluster.authority.transition", measurements);
        Assert.Contains("lakona.game.cluster.actor_location.recovery.duration", measurements);
        Assert.Contains("lakona.game.cluster.actor_location.failure", measurements);
        Assert.Contains("cluster.test", activities);
    }
}
