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

internal sealed class UnconfiguredLeaderboardStore : ILeaderboardStore
{
    public ValueTask<string?> GetCurrentPeriodAsync(
        CancellationToken cancellationToken = default)
    {
        throw CreateException();
    }

    public ValueTask SetCurrentPeriodAsync(
        string periodStartLocalDate,
        CancellationToken cancellationToken = default)
    {
        throw CreateException();
    }

    public ValueTask<IReadOnlyList<LeaderboardPlayerState>> LoadPlayersAsync(
        string periodStartLocalDate,
        CancellationToken cancellationToken = default)
    {
        throw CreateException();
    }

    public ValueTask UpsertPlayerAsync(
        string periodStartLocalDate,
        LeaderboardPlayerState player,
        CancellationToken cancellationToken = default)
    {
        throw CreateException();
    }

    private static InvalidOperationException CreateException()
    {
        return new InvalidOperationException(
            "Agar leaderboard persistence is not configured on this node. " +
            "Leaderboard Actors must be routed to a node with an Agar Redis connection string.");
    }
}
