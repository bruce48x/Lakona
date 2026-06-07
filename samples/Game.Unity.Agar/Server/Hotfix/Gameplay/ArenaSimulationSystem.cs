using Shared.Gameplay;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Agar.Sample.Hotfix.Gameplay;

[FriendOf(typeof(ArenaSimulation))]
[HotfixSystemOf(typeof(ArenaSimulation))]
public static class ArenaSimulationSystem
{
    public static ArenaStepResult Tick(this ArenaSimulation self, float deltaTime)
    {
        return self.TickCore(deltaTime);
    }
}
