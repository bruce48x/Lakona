using Server.App.State.Contracts;
using Server.App.State.Contracts.Rooms;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;

namespace Server.App.State.Rooms;

public sealed class RoomActor : Actor<RoomId>
{
    internal bool RecordExists;
    internal RoomState State = new();
    internal TimerId BattleRuntimeTimerId;
}
