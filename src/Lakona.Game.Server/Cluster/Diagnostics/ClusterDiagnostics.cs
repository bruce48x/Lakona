using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lakona.Game.Server.Observability;

namespace Lakona.Game.Cluster
{
    public static class ClusterDiagnostics
    {
        public const string MeterName = LakonaGameServerTelemetry.ClusterMeterName;
        public const string ActivitySourceName = LakonaGameServerTelemetry.ClusterActivitySourceName;
        private static readonly string? InstrumentationVersion =
            typeof(ClusterDiagnostics).Assembly.GetName().Version?.ToString();
        public static readonly Meter Meter = new(MeterName, InstrumentationVersion);
        public static readonly ActivitySource ActivitySource = new(ActivitySourceName, InstrumentationVersion);

    }
}
