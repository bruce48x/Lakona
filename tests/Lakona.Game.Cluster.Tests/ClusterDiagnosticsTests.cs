using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class ClusterDiagnosticsTests
{
    [Fact]
    public void NodeDirectoryMetricNamesAreStable()
    {
        Assert.Equal("lakona-game.cluster.node_directory.registration", ClusterDiagnostics.NodeDirectoryRegistrationMetricName);
        Assert.Equal("lakona-game.cluster.node_directory.heartbeat", ClusterDiagnostics.NodeDirectoryHeartbeatMetricName);
        Assert.Equal("lakona-game.cluster.node_directory.expired", ClusterDiagnostics.NodeDirectoryExpiredMetricName);
    }

    [Fact]
    public async Task RouterMetricsUseLowCardinalityTags()
    {
        using var collector = new MetricCollector();
        var now = DateTimeOffset.UtcNow;
        var directory = new InMemoryRouteDirectory();
        await directory.RegisterAsync(
            new RouteLocation("room/1", "remote", new NodeEndpoint("in-memory://remote"), now.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        var router = new ClusterRouter(
            "local",
            directory,
            new RecordingHandler(ClusterSendStatus.Accepted),
            new RecordingMessenger(ClusterSendStatus.Backpressure),
            () => now);

        var status = await router.SendAsync(
            NewMessage(now.AddMinutes(1), correlationId: "corr-1", traceId: "trace-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Backpressure, status);
        var measurements = collector.Snapshot();
        Assert.Contains(measurements, measurement =>
            measurement.Name == "lakona-game.cluster.route.lookup" &&
            HasTag(measurement, "lakona-game.cluster.status", "found"));
        Assert.Contains(measurements, measurement =>
            measurement.Name == "lakona-game.cluster.route.sent" &&
            HasTag(measurement, "lakona-game.cluster.delivery", "remote") &&
            HasTag(measurement, "lakona-game.cluster.status", "backpressure"));
        Assert.Contains(measurements, measurement =>
            measurement.Name == "lakona-game.cluster.route.backpressure" &&
            HasTag(measurement, "lakona-game.cluster.stage", "send"));
        Assert.DoesNotContain(measurements, measurement =>
            measurement.Tags.ContainsKey("route") ||
            measurement.Tags.ContainsKey("node") ||
            measurement.Tags.ContainsKey("session"));
    }

    [Fact]
    public async Task ActivityTagsPreserveTracePresenceWithoutHighCardinalityValues()
    {
        using var collector = new ActivityCollector();
        var now = DateTimeOffset.UtcNow;
        var messageKind = "diagnostics-" + Guid.NewGuid().ToString("N");
        var directory = new InMemoryRouteDirectory();
        await directory.RegisterAsync(
            new RouteLocation("room/1", "local", new NodeEndpoint("in-memory://local"), now.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        var router = new ClusterRouter(
            "local",
            directory,
            new RecordingHandler(ClusterSendStatus.Accepted),
            new RecordingMessenger(ClusterSendStatus.Accepted),
            () => now);

        await router.SendAsync(
            NewMessage(now.AddMinutes(1), messageKind, correlationId: "corr-1", traceId: "trace-1"),
            TestContext.Current.CancellationToken);

        var activity = Assert.Single(collector.Snapshot(), activity =>
            activity.OperationName == "send" &&
            Equals(activity.GetTagItem("lakona-game.cluster.status"), "accepted") &&
            Equals(activity.GetTagItem("lakona-game.cluster.message.kind"), messageKind));
        Assert.Equal("send", activity.OperationName);
        Assert.Equal(messageKind, activity.GetTagItem("lakona-game.cluster.message.kind"));
        Assert.Equal(true, activity.GetTagItem("lakona-game.cluster.correlation.present"));
        Assert.Equal(true, activity.GetTagItem("lakona-game.cluster.trace.present"));
        Assert.Equal("accepted", activity.GetTagItem("lakona-game.cluster.status"));
        Assert.Null(activity.GetTagItem("lakona-game.cluster.route"));
        Assert.Null(activity.GetTagItem("lakona-game.cluster.node"));
    }

    private static bool HasTag(
        MetricMeasurement measurement,
        string name,
        string value)
    {
        return measurement.Tags.TryGetValue(name, out var actual) &&
            Equals(actual, value);
    }

    private static ClusterMessage NewMessage(
        DateTimeOffset expiresAt,
        string kind = "command",
        string? correlationId = null,
        string? traceId = null)
    {
        return new ClusterMessage(
            "room/1",
            kind,
            new byte[] { 1 },
            expiresAt,
            "source",
            correlationId,
            traceId);
    }

    private sealed class RecordingHandler : IClusterMessageHandler
    {
        private readonly ClusterSendStatus _status;

        public RecordingHandler(ClusterSendStatus status)
        {
            _status = status;
        }

        public ValueTask<ClusterSendStatus> HandleAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_status);
        }
    }

    private sealed class RecordingMessenger : INodeMessenger
    {
        private readonly ClusterSendStatus _status;

        public RecordingMessenger(ClusterSendStatus status)
        {
            _status = status;
        }

        public ValueTask<ClusterSendStatus> SendAsync(
            RouteLocation target,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_status);
        }
    }

    private sealed class ActivityCollector : IDisposable
    {
        private readonly object _gate = new();
        private readonly ActivityListener _listener;
        private readonly List<Activity> _stopped = new();

        public ActivityCollector()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == ClusterDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    lock (_gate)
                    {
                        _stopped.Add(activity);
                    }
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public Activity[] Snapshot()
        {
            lock (_gate)
            {
                return _stopped.ToArray();
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }

    private sealed class MetricCollector : IDisposable
    {
        private readonly object _gate = new();
        private readonly MeterListener _listener = new();
        private readonly List<MetricMeasurement> _measurements = new();

        public MetricCollector()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ClusterDiagnostics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                lock (_gate)
                {
                    _measurements.Add(new MetricMeasurement(instrument.Name, TagsToDictionary(tags)));
                }
            });
            _listener.Start();
        }

        public MetricMeasurement[] Snapshot()
        {
            lock (_gate)
            {
                return _measurements.ToArray();
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private static Dictionary<string, object?> TagsToDictionary(
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                values[tag.Key] = tag.Value;
            }

            return values;
        }
    }

    private sealed record MetricMeasurement(
        string Name,
        Dictionary<string, object?> Tags);
}
