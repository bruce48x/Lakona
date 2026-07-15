using Shared.Gameplay;
using Shared.Interfaces;
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

    [Fact]
    public void Repeated_input_reuses_stable_simulation_state_objects()
    {
        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            EnableBots = false,
            FoodTargetCount = 96
        });
        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "p1", Mass = 25 });
        var input = new InputMessage
        {
            PlayerId = "p1",
            MoveX = 0.5f,
            MoveY = -0.25f,
            Tick = 1
        };

        for (var index = 0; index < 32; index++)
        {
            simulation.SubmitInput(input);
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000; index++)
        {
            input.Tick = index + 2;
            simulation.SubmitInput(input);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.True(
            allocated <= 64 * 1024,
            $"Expected repeated input to allocate at most 64 KiB, but it allocated {allocated:N0} bytes.");
    }
}
