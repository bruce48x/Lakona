using Shared.Gameplay;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class MatchSettlementRulesTests
{
    [Theory]
    [InlineData(24.4f, 24.1f, "z", "a")]
    [InlineData(24.1f, 24.4f, "a", "z")]
    [InlineData(24.4f, 24.4f, "a", "z")]
    public void Ranking_uses_actual_mass_then_ordinal_id_even_when_displayed_masses_match(
        float zMass, float aMass, string winnerId, string runnerUpId)
    {
        var world = new WorldState
        {
            Players = new List<PlayerState>
            {
                new() { PlayerId = "z", Mass = zMass },
                new() { PlayerId = "a", Mass = aMass }
            }
        };

        var result = MatchSettlementRules.Settle(world);

        Assert.Equal(winnerId, result.WinnerPlayerId);
        Assert.Collection(result.Entries,
            winner =>
            {
                Assert.Equal(winnerId, winner.PlayerId);
                Assert.Equal(1, winner.Rank);
                Assert.Equal(24, winner.Mass);
                Assert.True(winner.IsWinner);
                Assert.Equal(10, winner.VictoryPoints);
            },
            runnerUp =>
            {
                Assert.Equal(runnerUpId, runnerUp.PlayerId);
                Assert.Equal(2, runnerUp.Rank);
                Assert.Equal(24, runnerUp.Mass);
                Assert.False(runnerUp.IsWinner);
                Assert.Equal(7, runnerUp.VictoryPoints);
            });
    }
}
