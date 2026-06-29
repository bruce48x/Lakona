using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix;

public interface IHotfixRuntimeAccessor
{
    HotfixRuntimeSnapshot Current { get; }
}

public sealed class HotfixRuntimeSnapshot
{
    public HotfixRuntimeSnapshot(IHotfixServiceInvoker invoker, IServiceProvider services)
        : this(invoker, EmptyHotfixFeatureCommandInvoker.Instance, services)
    {
    }

    public HotfixRuntimeSnapshot(
        IHotfixServiceInvoker invoker,
        IHotfixFeatureCommandInvoker featureCommands,
        IServiceProvider services)
    {
        Invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        FeatureCommands = featureCommands ?? throw new ArgumentNullException(nameof(featureCommands));
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IHotfixServiceInvoker Invoker { get; }

    public IHotfixFeatureCommandInvoker FeatureCommands { get; }

    public IServiceProvider Services { get; }
}
