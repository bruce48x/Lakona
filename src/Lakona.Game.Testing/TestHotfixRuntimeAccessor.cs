using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Testing;

internal sealed class TestHotfixRuntimeAccessor : IHotfixRuntimeAccessor
{
    public TestHotfixRuntimeAccessor(IServiceProvider services) =>
        Current = new HotfixRuntimeSnapshot(new HotfixServiceInvoker(), services);

    public HotfixRuntimeSnapshot Current { get; }
}
