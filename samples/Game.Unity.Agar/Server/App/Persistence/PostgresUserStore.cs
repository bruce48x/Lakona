using Dapper;
using Npgsql;

namespace Server.App.Persistence;

public sealed class PostgresUserStore(NpgsqlDataSource dataSource) :
    IUserStore,
    IDisposable
{
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

    private const string SaveSql =
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
            @WinCount,
            @VictoryPoints,
            CURRENT_TIMESTAMP)
        ON CONFLICT (user_id) DO UPDATE
        SET password_hash = EXCLUDED.password_hash,
            login_count = EXCLUDED.login_count,
            last_login_at_utc = EXCLUDED.last_login_at_utc,
            win_count = EXCLUDED.win_count,
            victory_points = EXCLUDED.victory_points,
            updated_at_utc = CURRENT_TIMESTAMP;
        """;

    private readonly SemaphoreSlim schemaGate = new(1, 1);
    private int schemaReady;

    internal async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
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

    public async ValueTask SaveAsync(
        PersistedUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ValidateUserId(user.UserId);

        await using var connection = await dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                SaveSql,
                user,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public void Dispose()
    {
        schemaGate.Dispose();
    }

    private async Task EnsureSchemaAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref schemaReady) != 0)
        {
            return;
        }

        await schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref schemaReady) != 0)
            {
                return;
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    EnsureSchemaSql,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            Volatile.Write(ref schemaReady, 1);
        }
        finally
        {
            schemaGate.Release();
        }
    }

    private static void ValidateUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (userId.Length > 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(userId),
                "User id cannot exceed 128 characters.");
        }
    }
}
