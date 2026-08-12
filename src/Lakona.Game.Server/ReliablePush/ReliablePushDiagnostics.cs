using System.Diagnostics.Metrics;
using Lakona.Game.Server.Observability;

namespace Lakona.Game.Server.ReliablePush;

internal static class ReliablePushDiagnostics
{
    private static readonly Meter Meter = new(LakonaGameServerTelemetry.ReliablePushMeterName);

    internal static readonly Counter<long> ContinuityLost = Meter.CreateCounter<long>(
        "lakona.game.reliable_push.continuity_lost",
        description: "Number of reliable-push generations that lost continuity.");
}
