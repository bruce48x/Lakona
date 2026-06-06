
namespace Agar.Sample.State.Contracts.Users;

public interface IUserActor
{
    Task<UserLoginResult> LoginAsync(string password);
    Task<UserLoginResult> LoginAsync(string password, bool reconnect);
    Task<UserProfileSnapshot> GetProfileAsync();
    Task SetOnlineAsync(bool isOnline);
    Task AddWinAsync();
    Task AddVictoryPointsAsync(int points);
    Task ResetVictoryPointsAsync();
}

public sealed class UserLoginResult
{
    public string UserId { get; set; } = "";

    public string SessionToken { get; set; } = "";

    public int LoginCount { get; set; }

    public DateTime LastLoginAtUtc { get; set; }

    public int WinCount { get; set; }
    public int VictoryPoints { get; set; }
}

public sealed class UserProfileSnapshot
{
    public string UserId { get; set; } = "";

    public int LoginCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastLoginAtUtc { get; set; }

    public bool IsOnline { get; set; }

    public int WinCount { get; set; }

    public int VictoryPoints { get; set; }
}

