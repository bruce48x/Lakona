using Lakona.Game.Server.Observability;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class LakonaGameServerTelemetryTests
{
    [Fact]
    public void SourceCatalog_ExposesStableOpenTelemetryInstrumentationScopes()
    {
        Assert.Equal(
            [
                "Lakona.Game.Actor",
                "Lakona.Game.Cluster",
                "Lakona.Game.ReliablePush",
                "Lakona.Rpc.Server",
                "Lakona.Game.Session",
                "Lakona.Game.Timer"
            ],
            LakonaGameServerTelemetry.MeterNames);
        Assert.Equal(
            [
                "Lakona.Game.Actor",
                "Lakona.Game.Cluster"
            ],
            LakonaGameServerTelemetry.ActivitySourceNames);
    }
}
