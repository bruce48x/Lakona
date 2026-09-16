using Lakona.Game.Server.Hotfix;

namespace Lakona.Game.Testing.Fixtures.App;

public interface ILifecycleProbe
{
    ValueTask<string> ProbeAsync(HotfixLifecycleCall<string> call);
}
