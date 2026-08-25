using Server.App.Routing;
using Server.App.Rooms;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;

namespace Server.App.Rooms;

[NodeRole("battle")]
public sealed class RoomActor : Actor<RoomId>
{
    internal bool RecordExists;
    internal RoomState State = new();
    internal TimerId FrameRelayTimerId;
}
