using System.Security.Cryptography;
using System.Text;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Users;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.State.Users;

[HotfixBehaviorOf(typeof(UserActor))]
public static partial class UserBehavior
{
    public static ValueTask<UserLoginResult> LoginAsync(this UserActor self, UserLoginRequest request, CancellationToken cancellationToken = default)
    {
        var userId = self.Context.Id.Value;
        var passwordHash = ComputePasswordHash(request.Password);
        var now = DateTime.UtcNow;

        if (!self.RecordExists)
        {
            self.State = new UserState
            {
                UserId = userId,
                PasswordHash = passwordHash,
                CreatedAtUtc = now
            };
            self.RecordExists = true;
        }
        else if (!string.Equals(self.State.PasswordHash, passwordHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid password.");
        }

        if (!request.Reconnect || string.IsNullOrWhiteSpace(self.State.SessionToken))
        {
            self.State.SessionToken = Guid.NewGuid().ToString("N");
        }

        self.State.LoginCount += 1;
        self.State.LastLoginAtUtc = now;
        self.State.IsOnline = true;

        return new ValueTask<UserLoginResult>(new UserLoginResult
        {
            UserId = self.State.UserId,
            SessionToken = self.State.SessionToken,
            LoginCount = self.State.LoginCount,
            LastLoginAtUtc = self.State.LastLoginAtUtc,
            WinCount = Math.Max(0, self.State.WinCount),
            VictoryPoints = Math.Max(0, self.State.VictoryPoints)
        });
    }

    public static ValueTask<UserProfileSnapshot> GetProfileAsync(this UserActor self, UserProfileRequest request, CancellationToken cancellationToken = default)
    {
        var session = self.State.Session;
        return new ValueTask<UserProfileSnapshot>(new UserProfileSnapshot
        {
            UserId = self.State.UserId,
            LoginCount = self.State.LoginCount,
            CreatedAtUtc = self.State.CreatedAtUtc,
            LastLoginAtUtc = self.State.LastLoginAtUtc,
            IsOnline = self.State.IsOnline,
            WinCount = Math.Max(0, self.State.WinCount),
            VictoryPoints = Math.Max(0, self.State.VictoryPoints),
            SessionToken = session.SessionToken,
            ControlConnectionId = session.ConnectionId,
            ControlGatewayNodeId = session.ControlGateway.InstanceId,
            RealtimeGatewayNodeId = session.RuntimeGateway.InstanceId,
            CurrentRoomId = session.CurrentRoomId,
            CurrentMatchId = session.CurrentMatchId,
            SeatIndex = session.SeatIndex,
            MatchmakingTicketId = session.MatchmakingTicketId
        });
    }

    public static ValueTask SetOnlineAsync(this UserActor self, UserOnlineStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (self.RecordExists)
        {
            self.State.IsOnline = request.IsOnline;
        }

        return default;
    }

    public static ValueTask AddWinAsync(this UserActor self, UserWinRequest request, CancellationToken cancellationToken = default)
    {
        if (self.RecordExists)
        {
            self.State.WinCount = Math.Max(0, self.State.WinCount + 1);
        }

        return default;
    }

    public static ValueTask AddVictoryPointsAsync(this UserActor self, UserVictoryPointsRequest request, CancellationToken cancellationToken = default)
    {
        if (self.RecordExists && request.Points > 0)
        {
            self.State.VictoryPoints = Math.Max(0, self.State.VictoryPoints + request.Points);
        }

        return default;
    }

    public static ValueTask ResetVictoryPointsAsync(this UserActor self, UserVictoryPointsResetRequest request, CancellationToken cancellationToken = default)
    {
        if (self.RecordExists)
        {
            self.State.VictoryPoints = 0;
        }

        return default;
    }

    private static string ComputePasswordHash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }
}
