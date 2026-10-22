using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Timers;

namespace Lakona.Game.Testing.Fixtures.App;

[NodeRole("shutdown")]
public sealed class ShutdownActor : Actor<CounterId>
{
    internal TimerId Timer;
}
