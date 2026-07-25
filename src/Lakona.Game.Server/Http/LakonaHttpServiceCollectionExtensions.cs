using System.ComponentModel;
using Lakona.Game.Server.Hotfix;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Http;

/// <summary>
/// Registration seam used by generated stable Application HTTP binders.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class LakonaHttpServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaHttpEndpoint<TContract>(
        this IServiceCollection services,
        string service,
        string method,
        string routePattern,
        int methodId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(routePattern);
        if (methodId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(methodId));
        }

        services.AddSingleton(
            new LakonaHttpEndpointDescriptor(
                service,
                method.ToUpperInvariant(),
                routePattern,
                methodId,
                static (lease, id, call, cancellationToken) =>
                    lease.Invoker.InvokeAsync<TContract, LakonaHttpCall, LakonaHttpResponse>(
                        id,
                        call,
                        cancellationToken)));
        return services;
    }
}

internal sealed record LakonaHttpEndpointDescriptor(
    string Service,
    string Method,
    string RoutePattern,
    int MethodId,
    Func<
        HotfixRuntimeSnapshotLease,
        int,
        LakonaHttpCall,
        CancellationToken,
        ValueTask<LakonaHttpResponse>> Dispatch);
