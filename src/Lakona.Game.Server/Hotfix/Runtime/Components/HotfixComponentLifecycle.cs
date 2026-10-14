using Lakona.Game.Server.Health;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

// Stable bookkeeping contains strings only. Component instances, hooks and Types stay in their generation.
internal sealed class HotfixComponentLifecycle
{
    private readonly List<(string Identity, string? State)> activated = [];
    private bool attempted;
    private bool committed;
    private bool stopped;

    internal void Validate(HotfixRuntimeSnapshot candidate)
    {
        if (!committed) return;
        var components = Catalog(candidate);
        var positions = activated.Select((item, index) => (item.Identity, index))
            .ToDictionary(item => item.Identity, item => item.index, StringComparer.Ordinal);
        if (components.Count != activated.Count || components.Any(component => !positions.ContainsKey(component.Identity)))
            throw new InvalidOperationException("Hotfix component set is fixed until restart, including stateless helpers.");
        foreach (var component in components)
        {
            var position = positions[component.Identity];
            if (activated[position].State != component.State?.AssemblyQualifiedName)
                throw new InvalidOperationException($"Component '{component.Identity}' cannot change its stable state binding without restart.");
            if (component.Dependencies.Any(dependency => positions[dependency.FullName!] >= position))
                throw new InvalidOperationException($"Component '{component.Identity}' changes the recorded lifecycle dependency order. Restart is required.");
        }
    }

    internal async ValueTask StartAsync(HotfixRuntimeSnapshot runtime, IServiceProvider? root, CancellationToken token)
    {
        Validate(runtime);
        if (committed) return;
        if (attempted) throw new InvalidOperationException("Component startup already failed; restart the node before retrying activation.");
        attempted = true;
        foreach (var component in Catalog(runtime))
        {
            try
            {
                token.ThrowIfCancellationRequested();
                await component.InvokeAsync(runtime.Services, start: true, token).ConfigureAwait(false);
                activated.Add((component.Identity, component.State?.AssemblyQualifiedName));
            }
            catch (Exception error)
            {
                root?.GetService<LakonaServerReadinessState>()?.MarkFailed(component.Type, error);
                throw;
            }
        }
    }

    internal void Commit() => committed = true;

    internal ValueTask<IReadOnlyList<Exception>> RollbackAsync(HotfixRuntimeSnapshot runtime, CancellationToken token) =>
        committed || !attempted ? new ValueTask<IReadOnlyList<Exception>>(Array.Empty<Exception>()) : StopAsync(runtime, token);

    internal async ValueTask<IReadOnlyList<Exception>> StopAsync(HotfixRuntimeSnapshot runtime, CancellationToken token)
    {
        if (stopped) return Array.Empty<Exception>();
        stopped = true;
        var failures = new List<Exception>();
        var components = Catalog(runtime).ToDictionary(component => component.Identity, StringComparer.Ordinal);
        foreach (var (identity, _) in activated.AsEnumerable().Reverse())
        {
            try { await components[identity].InvokeAsync(runtime.Services, start: false, token).ConfigureAwait(false); }
            catch (Exception error)
            {
                failures.Add(new InvalidOperationException($"Component '{identity}' failed to stop.", error));
            }
        }
        activated.Clear();
        return failures;
    }

    private static IReadOnlyList<HotfixComponentDescriptor> Catalog(HotfixRuntimeSnapshot runtime) =>
        runtime.Services.GetService<HotfixComponentCatalog>()?.Ordered ?? [];
}
