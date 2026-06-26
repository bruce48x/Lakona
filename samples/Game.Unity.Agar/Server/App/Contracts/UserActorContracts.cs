using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Users;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Agar.Sample.State.Contracts.Users;

[HotfixActorContract(typeof(UserActor))]
public interface IUserActorContract
{
    ValueTask<UserLoginResult> LoginAsync(UserLoginRequest request, CancellationToken cancellationToken = default);

    ValueTask<UserProfileSnapshot> GetProfileAsync(UserProfileRequest request, CancellationToken cancellationToken = default);

    ValueTask SetOnlineAsync(UserOnlineStatusRequest request, CancellationToken cancellationToken = default);

    ValueTask AddWinAsync(UserWinRequest request, CancellationToken cancellationToken = default);

    ValueTask AddVictoryPointsAsync(UserVictoryPointsRequest request, CancellationToken cancellationToken = default);

    ValueTask ResetVictoryPointsAsync(UserVictoryPointsResetRequest request, CancellationToken cancellationToken = default);

    ValueTask<PlayerSessionSnapshot> AttachAsync(PlayerSessionAttachRequest request, CancellationToken cancellationToken = default);

    ValueTask<PlayerSessionSnapshot> ReconnectAsync(PlayerSessionReconnectRequest request, CancellationToken cancellationToken = default);

    ValueTask<PlayerSessionSnapshot> AttachRealtimeAsync(PlayerRealtimeAttachRequest request, CancellationToken cancellationToken = default);

    ValueTask<PlayerSessionSnapshot> ClearRealtimeAsync(PlayerRealtimeClearRequest request, CancellationToken cancellationToken = default);

    ValueTask<PlayerSessionSnapshot> MarkQueuedAsync(PlayerSessionQueueRequest request, CancellationToken cancellationToken = default);

    ValueTask<PlayerSessionSnapshot> ClearQueueAsync(PlayerSessionQueueClearRequest request, CancellationToken cancellationToken = default);

    ValueTask<PlayerSessionSnapshot> AssignRoomAsync(PlayerRoomAssignment request, CancellationToken cancellationToken = default);

    ValueTask<PlayerSessionSnapshot> ClearRoomAsync(PlayerRoomClearRequest request, CancellationToken cancellationToken = default);

    ValueTask<PlayerSessionSnapshot> MarkDisconnectedAsync(PlayerSessionDisconnectRequest request, CancellationToken cancellationToken = default);

    ValueTask<PlayerSessionSnapshot> HeartbeatAsync(PlayerSessionHeartbeatRequest request, CancellationToken cancellationToken = default);

    ValueTask<PlayerSessionSnapshot> GetSnapshotAsync(PlayerSessionSnapshotRequest request, CancellationToken cancellationToken = default);
}

public sealed class UserLoginRequest
{
    public string Password { get; set; } = "";

    public bool Reconnect { get; set; }
}

public sealed class UserProfileRequest
{
}

public sealed class UserOnlineStatusRequest
{
    public bool IsOnline { get; set; }
}

public sealed class UserWinRequest
{
}

public sealed class UserVictoryPointsRequest
{
    public int Points { get; set; }
}

public sealed class UserVictoryPointsResetRequest
{
}

public sealed class PlayerSessionSnapshotRequest
{
}
