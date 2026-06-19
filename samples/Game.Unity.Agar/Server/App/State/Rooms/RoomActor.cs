using Agar.Sample.State.Contracts.Rooms;
using Lakona.Game.Server.Actors;

namespace Agar.Sample.State.Rooms;

public sealed class RoomActor : Actor
{
    internal bool RecordExists;
    internal RoomState State = new();
}
