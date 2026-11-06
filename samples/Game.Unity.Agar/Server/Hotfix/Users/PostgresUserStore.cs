using Dapper;
using Lakona.Game.Server;
using Lakona.Game.Server.Hotfix.Abstractions;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Server.App.Users;

namespace Server.Hotfix.Users;

[HotfixComponent]
[NodeRole("data")]
public sealed class PostgresUserStore(NpgsqlDataSource dataSource)
{
    [ComponentConfigureServices]
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var name = configuration["Agar:Persistence:Postgres:ConnectionStringName"] ?? "AgarGamePostgres";
        var connectionString = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"ConnectionStrings:{name} is required on data nodes.");
        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));
    }

    private const string EnsureSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS agar_users (
            user_id varchar(128) PRIMARY KEY,
            password_hash varchar(128) NOT NULL,
            login_count integer NOT NULL,
            created_at_utc timestamp with time zone NOT NULL,
            last_login_at_utc timestamp with time zone NOT NULL,
            win_count integer NOT NULL,
            victory_points integer NOT NULL,
            updated_at_utc timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        CREATE TABLE IF NOT EXISTS agar_user_settlements (
            user_id varchar(128) NOT NULL REFERENCES agar_users(user_id) ON DELETE CASCADE,
            settlement_id text NOT NULL,
            PRIMARY KEY (user_id, settlement_id)
        );
        """;

    private const string LoadSql =
        """
        SELECT user_id AS "UserId",
               password_hash AS "PasswordHash",
               login_count AS "LoginCount",
               created_at_utc AS "CreatedAtUtc",
               last_login_at_utc AS "LastLoginAtUtc",
               win_count AS "WinCount",
               victory_points AS "VictoryPoints"
        FROM agar_users
        WHERE user_id = @UserId;
        """;

    private const string SaveLoginSql =
        """
        INSERT INTO agar_users (
            user_id,
            password_hash,
            login_count,
            created_at_utc,
            last_login_at_utc,
            win_count,
            victory_points,
            updated_at_utc)
        VALUES (
            @UserId,
            @PasswordHash,
            @LoginCount,
            @CreatedAtUtc,
            @LastLoginAtUtc,
            0,
            0,
            CURRENT_TIMESTAMP)
        ON CONFLICT (user_id) DO UPDATE
        SET password_hash = EXCLUDED.password_hash,
            login_count = EXCLUDED.login_count,
            last_login_at_utc = EXCLUDED.last_login_at_utc,
            updated_at_utc = CURRENT_TIMESTAMP
        RETURNING win_count, victory_points;
        """;

    private const string ApplySettlementSql =
        """
        WITH recorded AS (
            INSERT INTO agar_user_settlements (user_id, settlement_id)
            SELECT user_id, @SettlementId FROM agar_users WHERE user_id = @UserId
            ON CONFLICT (user_id, settlement_id) DO NOTHING
            RETURNING user_id
        )
        UPDATE agar_users AS u
        SET win_count = GREATEST(0, u.win_count) + @WinDelta,
            victory_points = GREATEST(0, u.victory_points) + @PointsDelta,
            updated_at_utc = CURRENT_TIMESTAMP
        FROM recorded
        WHERE u.user_id = recorded.user_id
        RETURNING u.win_count, u.victory_points;
        """;

    [ComponentStart]
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(EnsureSchemaSql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        _ = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1;", cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async ValueTask<PersistedUser?> LoadAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        await using var connection = await dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<PersistedUser>(
            new CommandDefinition(
                LoadSql,
                new { UserId = userId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    internal async ValueTask<(int WinCount, int VictoryPoints)> SaveLoginAsync(
        PersistedUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ValidateUserId(user.UserId);

        await using var connection = await dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        return await connection.QuerySingleAsync<(int, int)>(
            new CommandDefinition(
                SaveLoginSql,
                user,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    internal async ValueTask<(int WinCount, int VictoryPoints, bool Applied)?> ApplySettlementAsync(
        string userId,
        string settlementId,
        int winDelta,
        int pointsDelta,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(settlementId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var applied = await connection.QuerySingleOrDefaultAsync<(int WinCount, int VictoryPoints)?>(
            new CommandDefinition(
                ApplySettlementSql,
                new { UserId = userId, SettlementId = settlementId, WinDelta = winDelta, PointsDelta = pointsDelta },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (applied is { } totals)
            return (totals.WinCount, totals.VictoryPoints, true);

        // A concurrent insert can win after our statement snapshot. Read its committed totals in a new statement.
        const string readTotalsSql = """
            SELECT win_count, victory_points FROM agar_users WHERE user_id = @UserId;
            """;
        var current = await connection.QuerySingleOrDefaultAsync<(int WinCount, int VictoryPoints)?>(
            new CommandDefinition(readTotalsSql, new { UserId = userId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return current is { } existing ? (existing.WinCount, existing.VictoryPoints, false) : null;
    }

    internal async ValueTask ResetVictoryPointsAsync(string userId, CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        const string sql = """
            UPDATE agar_users SET victory_points = 0, updated_at_utc = CURRENT_TIMESTAMP WHERE user_id = @UserId;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private static void ValidateUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (userId.Length > PersistedUser.MaximumUserIdLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(userId),
                $"User id cannot exceed {PersistedUser.MaximumUserIdLength} characters.");
        }
    }
}
