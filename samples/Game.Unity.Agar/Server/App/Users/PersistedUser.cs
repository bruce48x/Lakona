namespace Server.App.Users;

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
