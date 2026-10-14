using StackExchange.Redis;
using Lakona.Game.Server;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Configuration;
using Server.App.Infrastructure.Redis;
using Server.App.Leaderboard;

namespace Server.Hotfix.Leaderboard;

[HotfixComponent]
[NodeRole("data")]
public sealed class RedisLeaderboardStore(RedisLeaderboardState state, IConfiguration configuration)
{
    private IDatabase Database => (state.Connection
        ?? throw new InvalidOperationException("Leaderboard component has not started.")).GetDatabase();

    [ComponentStart]
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = configuration["Agar:Persistence:Redis:ConnectionStringName"] ?? "AgarGameRedis";
        var connectionString = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"ConnectionStrings:{name} is required on data nodes.");
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = true;
        ConnectionMultiplexer? candidate = null;
        try
        {
            // ConnectAsync has its own finite timeout. Await completion so cancellation cannot orphan a connection.
            candidate = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
            _ = await candidate.GetDatabase().PingAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = configuration["Agar:Persistence:Redis:KeyPrefix"];
            state.KeyPrefix = string.IsNullOrWhiteSpace(prefix) ? "agar" : prefix.Trim();
            state.Connection = candidate;
            candidate = null;
        }
        catch (Exception startupError)
        {
            try { candidate?.Dispose(); }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Redis startup and candidate cleanup failed.", startupError, cleanupError);
            }
            throw;
        }
    }

    [ComponentStop]
    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (state.Connection is { } connection)
            await connection.CloseAsync(allowCommandsToComplete: true).ConfigureAwait(false);
        // The stable state owns final disposal, after all component Stop hooks.
    }
    public async ValueTask<string?> GetCurrentPeriodAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await Database
            .StringGetAsync(CurrentPeriodKey())
            .ConfigureAwait(false);
        return value.HasValue ? value.ToString() : null;
    }

    public async ValueTask SetCurrentPeriodAsync(
        string periodStartLocalDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePeriod(periodStartLocalDate);
        _ = await Database
            .StringSetAsync(CurrentPeriodKey(), periodStartLocalDate)
            .ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<LeaderboardPlayerState>> LoadPlayersAsync(
        string periodStartLocalDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePeriod(periodStartLocalDate);
        var scores = await Database
            .SortedSetRangeByRankWithScoresAsync(
                ScoresKey(periodStartLocalDate),
                order: Order.Descending)
            .ConfigureAwait(false);
        if (scores.Length == 0)
        {
            return [];
        }

        var playerIds = scores
            .Select(static entry => (RedisValue)entry.Element.ToString())
            .ToArray();
        var wins = await Database
            .HashGetAsync(WinsKey(periodStartLocalDate), playerIds)
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
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePeriod(periodStartLocalDate);
        ArgumentNullException.ThrowIfNull(player);
        if (string.IsNullOrWhiteSpace(player.PlayerId))
        {
            throw new ArgumentException("Leaderboard player id is required.", nameof(player));
        }

        var transaction = Database.CreateTransaction();
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
            .ConfigureAwait(false);
        if (!committed)
        {
            throw new InvalidOperationException(
                $"Redis did not commit leaderboard update for '{player.PlayerId}'.");
        }
    }

    private string KeyPrefix => state.KeyPrefix;

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
