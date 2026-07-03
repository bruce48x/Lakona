namespace Agar.Sample.State.Contracts.Users;

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
