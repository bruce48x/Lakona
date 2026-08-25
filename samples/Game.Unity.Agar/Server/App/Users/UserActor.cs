using Server.App.Routing;
using Server.App.Users;
using Server.App.Sessions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server;

namespace Server.App.Users;

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

    public List<string> AppliedSettlementIds { get; set; } = new();

    public PlayerSessionState Session { get; set; } = new();
}

[NodeRole("data")]
public sealed class UserActor : Actor<UserId>
{
    internal bool RecordLoaded;
    internal bool RecordExists;
    internal bool SessionRecordExists;
    internal UserState State = new();
}
