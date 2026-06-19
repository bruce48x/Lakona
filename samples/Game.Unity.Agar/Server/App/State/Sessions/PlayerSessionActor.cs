using Agar.Sample.State.Contracts.Sessions;
using Lakona.Game.Server.Actors;

namespace Agar.Sample.State.Sessions;

public sealed class PlayerSessionActor : Actor
{
    internal bool RecordExists;
    internal PlayerSessionState State = new();
}
