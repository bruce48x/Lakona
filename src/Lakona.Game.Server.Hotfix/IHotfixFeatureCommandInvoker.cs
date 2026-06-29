using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix;

public interface IHotfixFeatureCommandInvoker
{
    bool TryResolve(
        string featureName,
        FeatureCommandId commandId,
        out HotfixFeatureCommandDescriptor descriptor);

    ValueTask<object?> InvokeAsync(
        HotfixFeatureCommandDescriptor descriptor,
        object? request,
        FeatureMessageRequest message,
        IServiceProvider services,
        CancellationToken cancellationToken = default);
}
