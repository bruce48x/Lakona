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
            ClusterDiagnostics.RecordMembershipTableOperation("refresh", "success", TimeSpan.FromMilliseconds(1));
            ClusterDiagnostics.RecordMembershipLifecycle("active");
            ClusterDiagnostics.RecordActorDirectoryTransition("success", TimeSpan.FromMilliseconds(2));
            ClusterDiagnostics.RecordActorDirectoryFailure(ActorDirectoryFailureReason.Unavailable);
            ClusterDiagnostics.RecordActorRequestProofFailure(
                ActorRequestProofFailureReason.Activation);
        }

        Assert.Contains("lakona.game.cluster.membership.table.operation", measurements);
        Assert.Contains("lakona.game.cluster.membership.table.operation.duration", measurements);
        Assert.Contains("lakona.game.cluster.membership.lifecycle", measurements);
        Assert.Contains("lakona.game.cluster.actor_directory.transition.duration", measurements);
        Assert.Contains("lakona.game.cluster.actor_directory.failure", measurements);
        Assert.Contains("lakona.game.cluster.actor_request.proof_failure", measurements);
        Assert.Contains("cluster.test", activities);
    }
}
