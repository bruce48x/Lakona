using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class EmptyHotfixFeatureCommandInvoker : IHotfixFeatureCommandInvoker
{
    public static EmptyHotfixFeatureCommandInvoker Instance { get; } = new();

    private EmptyHotfixFeatureCommandInvoker()
    {
    }

    public bool TryResolve(
        string featureName,
        FeatureCommandId commandId,
        out HotfixFeatureCommandDescriptor descriptor)
    {
        descriptor = default!;
        return false;
    }

    public ValueTask<object?> InvokeAsync(
        HotfixFeatureCommandDescriptor descriptor,
        object? request,
        FeatureMessageRequest message,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = descriptor?.Key ?? "<unresolved>";
        throw new HotfixMethodNotLoadedException($"Hotfix feature command '{key}' is not loaded.");
    }
}
