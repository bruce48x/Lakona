using System.Diagnostics.Metrics;

namespace Lakona.Game.Server.ReliablePush;

internal static class ReliablePushDiagnostics
{
    private static readonly Meter Meter = new("Lakona.Game.ReliablePush");

    internal static readonly Counter<long> ContinuityLost = Meter.CreateCounter<long>(
        "lakona.game.reliable_push.continuity_lost",
        description: "Number of reliable-push generations that lost continuity.");
}
