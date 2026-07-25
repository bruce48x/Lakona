namespace Server.App.Users;

public interface IUserStore
{
    ValueTask<PersistedUser?> LoadAsync(
        string userId,
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        PersistedUser user,
        CancellationToken cancellationToken = default);
}

internal sealed class UnconfiguredUserStore : IUserStore
{
    public ValueTask<PersistedUser?> LoadAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        throw CreateException();
    }

    public ValueTask SaveAsync(
        PersistedUser user,
        CancellationToken cancellationToken = default)
    {
        throw CreateException();
    }

    private static InvalidOperationException CreateException()
    {
        return new InvalidOperationException(
            "Agar user persistence is not configured on this node. " +
            "User Actors must be routed to a node with an Agar PostgreSQL connection string.");
    }
}

public sealed class PersistedUser
{
    public const int MaximumUserIdLength = 128;

    public string UserId { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public int LoginCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastLoginAtUtc { get; set; }

    public int WinCount { get; set; }

    public int VictoryPoints { get; set; }
}
