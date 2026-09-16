using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Testing.Fixtures.App;

namespace Lakona.Game.Testing.Fixtures.Hotfix;

[HotfixLifecycle]
public sealed class LifecycleProbe : ILifecycleProbe
{
    public ValueTask<string> ProbeAsync(HotfixLifecycleCall<string> call) => new(call.Request);
}
