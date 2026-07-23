using Server.App.State.Contracts;
using Server.App.State.Contracts.Users;
using Server.App.State.Contracts.Sessions;
using Lakona.Game.Server.Actors;

namespace Server.App.State.Users;

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

    public PlayerSessionState Session { get; set; } = new();
}

public sealed class UserActor : Actor<UserId>
{
    internal bool RecordLoaded;
    internal bool RecordExists;
    internal bool SessionRecordExists;
    internal UserState State = new();
}
