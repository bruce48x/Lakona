using Shared.Gameplay;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class ArenaStableStateTests
{
    [Fact]
    public void Arena_simulation_keeps_player_state_after_registration()
    {
        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            EnableBots = false,
            FoodTargetCount = 0
        });

        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "p1", Mass = 25 });

        Assert.True(simulation.TryGetPlayerSnapshot("p1", out var snapshot));
        Assert.Equal("p1", snapshot.PlayerId);
        Assert.True(snapshot.Mass >= 25);
    }
}
