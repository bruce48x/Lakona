using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Lakona.Game.Cluster
{
    public static class ClusterDiagnostics
    {
        public const string MeterName = "Lakona.Game.Cluster";
        public const string ActivitySourceName = "Lakona.Game.Cluster";
        public const string NodeDirectoryRegistrationMetricName = "lakona-game.cluster.node_directory.registration";
        public const string NodeDirectoryHeartbeatMetricName = "lakona-game.cluster.node_directory.heartbeat";
        public const string NodeDirectoryExpiredMetricName = "lakona-game.cluster.node_directory.expired";

        public static readonly Meter Meter = new Meter(MeterName, "0.1.1");
        public static readonly ActivitySource ActivitySource = new ActivitySource(ActivitySourceName, "0.1.1");

        internal static readonly Counter<long> RouteLookupCounter = Meter.CreateCounter<long>(
            "lakona-game.cluster.route.lookup");

        internal static readonly Counter<long> SendCounter = Meter.CreateCounter<long>(
            "lakona-game.cluster.route.sent");

        internal static readonly Counter<long> ReceiveCounter = Meter.CreateCounter<long>(
            "lakona-game.cluster.route.received");

        internal static readonly Counter<long> DispatchCounter = Meter.CreateCounter<long>(
            "lakona-game.cluster.route.dispatched");

        internal static readonly Counter<long> DropCounter = Meter.CreateCounter<long>(
            "lakona-game.cluster.route.dropped");

        internal static readonly Counter<long> ExpiredCounter = Meter.CreateCounter<long>(
            "lakona-game.cluster.route.expired");

        internal static readonly Counter<long> BackpressureCounter = Meter.CreateCounter<long>(
            "lakona-game.cluster.route.backpressure");

        internal static Activity? StartActivity(string name, ClusterMessage message)
        {
            var activity = ActivitySource.StartActivity(name, ActivityKind.Internal);
            if (activity is not null)
            {
                activity.SetTag("messaging.system", "lakona-game.cluster");
                activity.SetTag("messaging.operation", name);
                activity.SetTag("lakona-game.cluster.message.kind", message.Kind);
                activity.SetTag("lakona-game.cluster.correlation.present", message.CorrelationId is not null);
                activity.SetTag("lakona-game.cluster.trace.present", message.TraceId is not null);
            }

            return activity;
        }

        internal static void AddRouteLookup(string status, string kind)
        {
            RouteLookupCounter.Add(1, Tags("lookup", status, null, kind));
        }

        internal static void AddSend(string status, string delivery, string kind)
        {
            SendCounter.Add(1, Tags("send", status, delivery, kind));
        }

        internal static void AddReceive(string status, string kind)
        {
            ReceiveCounter.Add(1, Tags("receive", status, "remote", kind));
        }

        internal static void AddDispatch(string status, string delivery, string kind)
        {
            DispatchCounter.Add(1, Tags("dispatch", status, delivery, kind));
        }

        internal static void AddDrop(string status, string kind)
        {
            DropCounter.Add(1, Tags("drop", status, null, kind));
        }

        internal static void AddExpired(string kind)
        {
            ExpiredCounter.Add(1, Tags("expire", "expired", null, kind));
        }

        internal static void AddBackpressure(string stage, string delivery, string kind)
        {
            BackpressureCounter.Add(1, Tags(stage, "backpressure", delivery, kind));
        }

        internal static string StatusTag(ClusterSendStatus status)
        {
            return status.ToString().ToLowerInvariant();
        }

        private static TagList Tags(string stage, string status, string? delivery, string kind)
        {
            var tags = new TagList
            {
                { "lakona-game.cluster.stage", stage },
                { "lakona-game.cluster.status", status },
                { "lakona-game.cluster.message.kind", kind }
            };

            if (delivery is not null)
            {
                tags.Add("lakona-game.cluster.delivery", delivery);
            }

            return tags;
        }
    }
}
