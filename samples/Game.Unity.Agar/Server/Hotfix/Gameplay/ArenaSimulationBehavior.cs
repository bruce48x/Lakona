using Shared.Gameplay;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Agar.Sample.Hotfix.Gameplay;

[FriendOf(typeof(ArenaSimulation))]
[HotfixBehaviorOf(typeof(ArenaSimulation))]
public static class ArenaSimulationBehavior
{
    public static ArenaStepResult Tick(this ArenaSimulation self, float deltaTime)
    {
        return self.TickCore(deltaTime);
    }
}
