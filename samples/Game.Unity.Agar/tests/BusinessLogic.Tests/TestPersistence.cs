using Server.App.Leaderboard;
using Server.App.Users;

namespace Agar.Unity.Tests;

internal sealed class InMemoryUserStore : IUserStore
{
    private readonly Dictionary<string, PersistedUser> users =
        new(StringComparer.Ordinal);

    public ValueTask<PersistedUser?> LoadAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<PersistedUser?>(
            users.TryGetValue(userId, out var user) ? Clone(user) : null);
    }

    public ValueTask SaveAsync(
        PersistedUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        users[user.UserId] = Clone(user);
        return default;
    }

    private static PersistedUser Clone(PersistedUser user)
    {
        return new PersistedUser
        {
            UserId = user.UserId,
            PasswordHash = user.PasswordHash,
            LoginCount = user.LoginCount,
            CreatedAtUtc = user.CreatedAtUtc,
            LastLoginAtUtc = user.LastLoginAtUtc,
            WinCount = user.WinCount,
            VictoryPoints = user.VictoryPoints
        };
    }
}

internal sealed class InMemoryLeaderboardStore : ILeaderboardStore
{
    private readonly Dictionary<string, Dictionary<string, LeaderboardPlayerState>> periods =
        new(StringComparer.Ordinal);

    public string? CurrentPeriod { get; private set; }

    public ValueTask<string?> GetCurrentPeriodAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<string?>(CurrentPeriod);
    }

    public ValueTask SetCurrentPeriodAsync(
        string periodStartLocalDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CurrentPeriod = periodStartLocalDate;
        return default;
    }

    public ValueTask<IReadOnlyList<LeaderboardPlayerState>> LoadPlayersAsync(
        string periodStartLocalDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<LeaderboardPlayerState> result =
            periods.TryGetValue(periodStartLocalDate, out var players)
                ? players.Values.Select(Clone).ToArray()
                : [];
        return new ValueTask<IReadOnlyList<LeaderboardPlayerState>>(result);
    }

    public ValueTask UpsertPlayerAsync(
        string periodStartLocalDate,
        LeaderboardPlayerState player,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!periods.TryGetValue(periodStartLocalDate, out var players))
        {
            players = new Dictionary<string, LeaderboardPlayerState>(StringComparer.Ordinal);
            periods[periodStartLocalDate] = players;
        }

        players[player.PlayerId] = Clone(player);
        return default;
    }

    private static LeaderboardPlayerState Clone(LeaderboardPlayerState player)
    {
        return new LeaderboardPlayerState
        {
            PlayerId = player.PlayerId,
            VictoryPoints = player.VictoryPoints,
            WinCount = player.WinCount
        };
    }
}
