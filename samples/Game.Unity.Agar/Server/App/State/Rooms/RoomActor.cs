using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Rooms;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;

namespace Agar.Sample.State.Rooms;

public sealed class RoomActor : Actor<RoomId>
{
    internal bool RecordExists;
    internal RoomState State = new();
    internal TimerId BattleRuntimeTimerId;
}
