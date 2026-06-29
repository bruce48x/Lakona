using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixFeatureCommandInvoker : IHotfixFeatureCommandInvoker
{
    private readonly Func<HotfixDispatchTable> _current;

    public HotfixFeatureCommandInvoker()
        : this(static () => HotfixDispatch.Current)
    {
    }

    public HotfixFeatureCommandInvoker(HotfixDispatchTable table)
        : this(() => table)
    {
        ArgumentNullException.ThrowIfNull(table);
    }

    private HotfixFeatureCommandInvoker(Func<HotfixDispatchTable> current)
    {
        _current = current;
    }

    public bool TryResolve(
        string featureName,
        FeatureCommandId commandId,
        out HotfixFeatureCommandDescriptor descriptor)
    {
        return _current().TryResolveFeatureCommand(featureName, commandId, out descriptor);
    }

    public ValueTask<object?> InvokeAsync(
        HotfixFeatureCommandDescriptor descriptor,
        object? request,
        FeatureMessageRequest message,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _current().InvokeFeatureCommandAsync(
            descriptor,
            request,
            message,
            services,
            cancellationToken);
    }
}
