using Server.App.State.Leaderboard;

namespace Server.App.Persistence;

public interface ILeaderboardStore
{
    ValueTask<string?> GetCurrentPeriodAsync(
        CancellationToken cancellationToken = default);

    ValueTask SetCurrentPeriodAsync(
        string periodStartLocalDate,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<LeaderboardPlayerState>> LoadPlayersAsync(
        string periodStartLocalDate,
        CancellationToken cancellationToken = default);

    ValueTask UpsertPlayerAsync(
        string periodStartLocalDate,
        LeaderboardPlayerState player,
        CancellationToken cancellationToken = default);
}
