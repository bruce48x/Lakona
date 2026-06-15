using System.Security.Cryptography;
using System.Text;
using Agar.Sample.State.Contracts.Users;
using Lakona.Game.Server.Actors;

namespace Agar.Sample.State.Users;

public sealed class UserState
{
    public string UserId { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public string SessionToken { get; set; } = "";

    public int LoginCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastLoginAtUtc { get; set; }

    public bool IsOnline { get; set; }

    public int WinCount { get; set; }

    public int VictoryPoints { get; set; }
}

public sealed class UserActor : Actor
{
    private bool _recordExists;
    private UserState _state = new();

    public Task<UserLoginResult> LoginAsync(string password)
    {
        return LoginAsync(password, reconnect: false);
    }

    public Task<UserLoginResult> LoginAsync(string password, bool reconnect)
    {
        var userId = Context.Id.Value;
        var passwordHash = ComputePasswordHash(password);
        var now = DateTime.UtcNow;

        if (!_recordExists)
        {
            _state = new UserState
            {
                UserId = userId,
                PasswordHash = passwordHash,
                CreatedAtUtc = now
            };
            _recordExists = true;
        }
        else if (!string.Equals(_state.PasswordHash, passwordHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid password.");
        }

        if (!reconnect || string.IsNullOrWhiteSpace(_state.SessionToken))
        {
            _state.SessionToken = Guid.NewGuid().ToString("N");
        }

        _state.LoginCount += 1;
        _state.LastLoginAtUtc = now;
        _state.IsOnline = true;

        return Task.FromResult(new UserLoginResult
        {
            UserId = _state.UserId,
            SessionToken = _state.SessionToken,
            LoginCount = _state.LoginCount,
            LastLoginAtUtc = _state.LastLoginAtUtc,
            WinCount = Math.Max(0, _state.WinCount),
            VictoryPoints = Math.Max(0, _state.VictoryPoints)
        });
    }

    public Task<UserProfileSnapshot> GetProfileAsync()
    {
        return Task.FromResult(new UserProfileSnapshot
        {
            UserId = _state.UserId,
            LoginCount = _state.LoginCount,
            CreatedAtUtc = _state.CreatedAtUtc,
            LastLoginAtUtc = _state.LastLoginAtUtc,
            IsOnline = _state.IsOnline,
            WinCount = Math.Max(0, _state.WinCount),
            VictoryPoints = Math.Max(0, _state.VictoryPoints)
        });
    }

    public Task SetOnlineAsync(bool isOnline)
    {
        if (_recordExists)
        {
            _state.IsOnline = isOnline;
        }

        return Task.CompletedTask;
    }

    public Task AddWinAsync()
    {
        if (_recordExists)
        {
            _state.WinCount = Math.Max(0, _state.WinCount + 1);
        }

        return Task.CompletedTask;
    }

    public Task AddVictoryPointsAsync(int points)
    {
        if (_recordExists && points > 0)
        {
            _state.VictoryPoints = Math.Max(0, _state.VictoryPoints + points);
        }

        return Task.CompletedTask;
    }

    public Task ResetVictoryPointsAsync()
    {
        if (_recordExists)
        {
            _state.VictoryPoints = 0;
        }

        return Task.CompletedTask;
    }

    private static string ComputePasswordHash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }
}
