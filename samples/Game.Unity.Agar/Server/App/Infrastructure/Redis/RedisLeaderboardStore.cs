using StackExchange.Redis;
using Server.App.Leaderboard;

namespace Server.App.Infrastructure.Redis;

public sealed class RedisLeaderboardStore(
    RedisLeaderboardOptions options,
    IDatabase database) :
    ILeaderboardStore
{
    public async ValueTask<string?> GetCurrentPeriodAsync(
        CancellationToken cancellationToken = default)
    {
        var value = await database
            .StringGetAsync(CurrentPeriodKey())
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return value.HasValue ? value.ToString() : null;
    }

    public async ValueTask SetCurrentPeriodAsync(
        string periodStartLocalDate,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(periodStartLocalDate);
        _ = await database
            .StringSetAsync(CurrentPeriodKey(), periodStartLocalDate)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<LeaderboardPlayerState>> LoadPlayersAsync(
        string periodStartLocalDate,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(periodStartLocalDate);
        var scores = await database
            .SortedSetRangeByRankWithScoresAsync(
                ScoresKey(periodStartLocalDate),
                order: Order.Descending)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (scores.Length == 0)
        {
            return [];
        }

        var playerIds = scores
            .Select(static entry => (RedisValue)entry.Element.ToString())
            .ToArray();
        var wins = await database
            .HashGetAsync(WinsKey(periodStartLocalDate), playerIds)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        var players = new LeaderboardPlayerState[scores.Length];
        for (var index = 0; index < scores.Length; index++)
        {
            players[index] = new LeaderboardPlayerState
            {
                PlayerId = scores[index].Element.ToString(),
                VictoryPoints = Math.Max(0, checked((int)scores[index].Score)),
                WinCount = wins[index].TryParse(out long winCount)
                    ? Math.Max(0, checked((int)winCount))
                    : 0
            };
        }

        return players;
    }

    public async ValueTask UpsertPlayerAsync(
        string periodStartLocalDate,
        LeaderboardPlayerState player,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(periodStartLocalDate);
        ArgumentNullException.ThrowIfNull(player);
        if (string.IsNullOrWhiteSpace(player.PlayerId))
        {
            throw new ArgumentException("Leaderboard player id is required.", nameof(player));
        }

        var transaction = database.CreateTransaction();
        _ = transaction.SortedSetAddAsync(
            ScoresKey(periodStartLocalDate),
            player.PlayerId,
            Math.Max(0, player.VictoryPoints));
        _ = transaction.HashSetAsync(
            WinsKey(periodStartLocalDate),
            player.PlayerId,
            Math.Max(0, player.WinCount));
        var committed = await transaction
            .ExecuteAsync()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!committed)
        {
            throw new InvalidOperationException(
                $"Redis did not commit leaderboard update for '{player.PlayerId}'.");
        }
    }

    private string KeyPrefix => options.KeyPrefix;

    private RedisKey CurrentPeriodKey() => $"{KeyPrefix}:leaderboard:current-period";

    private RedisKey ScoresKey(string period) => $"{KeyPrefix}:leaderboard:{period}:scores";

    private RedisKey WinsKey(string period) => $"{KeyPrefix}:leaderboard:{period}:wins";

    private static void ValidatePeriod(string periodStartLocalDate)
    {
        if (string.IsNullOrWhiteSpace(periodStartLocalDate))
        {
            throw new ArgumentException(
                "Leaderboard period start date is required.",
                nameof(periodStartLocalDate));
        }
    }
}

public sealed record RedisLeaderboardOptions(
    string ConnectionString,
    string KeyPrefix);
