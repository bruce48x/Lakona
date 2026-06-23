using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

public interface IHotfixRuntimeAccessor
{
    HotfixRuntimeSnapshot Current { get; }
}

public sealed class HotfixRuntimeSnapshot
{
    public HotfixRuntimeSnapshot(IHotfixServiceInvoker invoker, IServiceProvider services)
    {
        Invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IHotfixServiceInvoker Invoker { get; }

    public IServiceProvider Services { get; }
}
