using Shared.Gameplay;
using Shared.Interfaces;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class ArenaSimulationRulesTests
{
    [Fact]
    public void MaxRoundSecondsZeroDisablesTimerBasedMatchEnd()
    {
        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            EnableBots = false,
            FoodTargetCount = 0,
            MinPlayersToStart = 1,
            TargetParticipantCount = 1,
            MaxRoundSeconds = 0f
        });

        simulation.UpsertPlayer(new ArenaPlayerRegistration
        {
            PlayerId = "Player"
        });

        ArenaStepResult result = null!;
        for (var i = 0; i < 400; i++)
        {
            result = simulation.Tick(0.05f);
        }

        Assert.Null(result.MatchEnd);
        Assert.Equal(0, result.WorldState.RoundRemainingSeconds);
    }

    [Fact]
    public void HighStartingMassControlsStartingMass()
    {
        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            EnableBots = false,
            FoodTargetCount = 0,
            MinPlayersToStart = 1,
            TargetParticipantCount = 1,
            MaxRoundSeconds = 0f
        });

        simulation.UpsertPlayer(new ArenaPlayerRegistration
        {
            PlayerId = "Player",
            Mass = 1000f
        });

        var player = Assert.Single(simulation.CreateWorldState().Players);
        Assert.Equal(1000f, player.Mass);
    }

    [Fact]
    public void InvertedMoveSpeedPlayerGetsFasterAsMassGrows()
    {
        var small = CreateSinglePlayerState(mass: 24f, invertedSpeed: true);
        var large = CreateSinglePlayerState(mass: 1000f, invertedSpeed: true);

        Assert.True(large.MoveSpeed > small.MoveSpeed);
    }

    [Fact]
    public void NormalMoveSpeedPlayerGetsSlowerAsMassGrows()
    {
        var small = CreateSinglePlayerState(mass: 24f, invertedSpeed: false);
        var large = CreateSinglePlayerState(mass: 1000f, invertedSpeed: false);

        Assert.True(large.MoveSpeed < small.MoveSpeed);
    }

    [Fact]
    public void MoveSpeedMultiplierAppliesOnlyToConfiguredPlayer()
    {
        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            EnableBots = false,
            FoodTargetCount = 0,
            MinPlayersToStart = 1,
            TargetParticipantCount = 2,
            MaxRoundSeconds = 0f,
            MoveSpeedMultiplierPlayerId = "Boosted",
            MoveSpeedMultiplier = 2f
        });

        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "Boosted" });
        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "Normal" });

        var players = simulation.CreateWorldState().Players;
        var boosted = players.Single(player => player.PlayerId == "Boosted");
        var normal = players.Single(player => player.PlayerId == "Normal");

        Assert.Equal(normal.MoveSpeed * 2f, boosted.MoveSpeed, precision: 4);
    }

    [Fact]
    public void DefaultPickupTypesOnlyContainMassPoint()
    {
        var options = new ArenaSimulationOptions();

        var pickupType = Assert.Single(options.EnabledPickupTypes);
        Assert.Equal(PickupType.MassPoint, pickupType);
    }

    [Fact]
    public void GeneratedFoodUsesOnlyMassPoint()
    {
        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            EnableBots = false,
            FoodTargetCount = 16,
            MinPlayersToStart = 1,
            TargetParticipantCount = 1,
            MaxRoundSeconds = 0f
        });

        var world = simulation.CreateWorldState();

        Assert.All(world.Pickups, pickup => Assert.Equal(PickupType.MassPoint, pickup.Type));
    }

    [Fact]
    public void BotChasesOtherBotWhenItMeetsConfiguredEatRatio()
    {
        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            FoodTargetCount = 0,
            MinPlayersToStart = 1,
            TargetParticipantCount = 3,
            MaxRoundSeconds = 0f,
            EatMassRatio = 1.15f
        });

        simulation.UpsertPlayer(new ArenaPlayerRegistration { PlayerId = "Player" });
        SetPlayerState(simulation, "Player", mass: 24f, position: new Vector2(0f, -8f));
        SetPlayerState(simulation, "AI01", mass: 64f, position: new Vector2(0f, 8f));
        SetPlayerState(simulation, "AI02", mass: 24f, position: new Vector2(10f, 8f));

        var result = simulation.Tick(0.1f);
        var hunter = result.WorldState.Players.Single(player => player.PlayerId == "AI01");

        Assert.True(hunter.Vx > MathF.Abs(hunter.Vy));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 7)]
    [InlineData(3, 5)]
    [InlineData(4, 3)]
    [InlineData(5, 1)]
    [InlineData(6, 0)]
    public void VictoryPointsMatchRankTable(int rank, int expectedPoints)
    {
        Assert.Equal(expectedPoints, VictoryPointAwards.GetPointsForRank(rank));
    }

    [Theory]
    [InlineData("AI01", true)]
    [InlineData("AI-bot", true)]
    [InlineData("Player", false)]
    public void VictoryPointBotFilterUsesAiPrefix(string playerId, bool expected)
    {
        Assert.Equal(expected, VictoryPointAwards.IsBotPlayer(playerId));
    }

    private static PlayerState CreateSinglePlayerState(float mass, bool invertedSpeed)
    {
        var simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            EnableBots = false,
            FoodTargetCount = 0,
            MinPlayersToStart = 1,
            TargetParticipantCount = 1,
            MaxRoundSeconds = 0f,
            InvertedMoveSpeedPlayerId = invertedSpeed ? "Player" : null
        });

        simulation.UpsertPlayer(new ArenaPlayerRegistration
        {
            PlayerId = "Player",
            Mass = mass
        });

        return Assert.Single(simulation.CreateWorldState().Players);
    }

    private static void SetPlayerState(ArenaSimulation simulation, string playerId, float mass, Vector2 position)
    {
        var playersField = typeof(ArenaSimulation).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var players = (IDictionary)playersField.GetValue(simulation)!;
        var player = players[playerId]!;

        typeof(ArenaSimulation)
            .GetMethod("SetMass", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(simulation, new object[] { player, mass });

        player.GetType().GetProperty("Position")!.SetValue(player, position);
        player.GetType().GetProperty("Velocity")!.SetValue(player, new Vector2(0f, 0f));
        player.GetType().GetProperty("Input")!.SetValue(player, new Vector2(0f, 0f));
    }
}
