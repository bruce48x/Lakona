using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Timers;
using Lakona.Game.Testing.Fixtures.App;

namespace Lakona.Game.Testing.Fixtures.Hotfix;

[HotfixBehaviorOf(typeof(ShutdownActor))]
public sealed partial class ShutdownBehavior(CompositionProbe probe, IClusterMembership membership, IMembershipTable table)
{
    [ActorStart]
    public ValueTask StartAsync(ShutdownActor self, ActorStartCall call)
    {
        self.Timer = self.CreatePeriodicTimer(
            static (ShutdownBehavior behavior) => behavior.TickAsync,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(10), new WaitCounterRequest());
        return default;
    }

    [ActorTimer]
    private ValueTask TickAsync(ShutdownActor self, TimerTick<WaitCounterRequest> tick)
    {
        probe.RecordTimerTick();
        return default;
    }

    [ActorStop]
    public async ValueTask StopAsync(ShutdownActor self, ActorStopCall call)
    {
        self.DestroyTimer(self.Timer);
        probe.RecordStartupStop(membership.Current, await table.ReadOrCreateAsync(call.CancellationToken));
    }
}
