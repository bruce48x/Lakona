using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Management;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;

namespace Lakona.Game.Server.Http;

internal sealed class LakonaApplicationHttpEndpointRegistry :
    IHotfixRuntimePublicationParticipant
{
    private readonly LakonaGameRuntimeOptions _runtime;
    private readonly IReadOnlyDictionary<string, LakonaApplicationHttpEndpointDataSource> _sources;
    private IReadOnlyList<HotfixHttpEndpointDescriptor>? _manifest;

    public LakonaApplicationHttpEndpointRegistry(LakonaGameRuntimeOptions runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _sources = runtime.Http.Listeners.ToDictionary(
            static listener => listener.Id,
            static _ => new LakonaApplicationHttpEndpointDataSource(),
            StringComparer.OrdinalIgnoreCase);
    }

    public LakonaApplicationHttpEndpointDataSource GetSource(string listenerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listenerId);
        return _sources.TryGetValue(listenerId, out var source)
            ? source
            : throw new InvalidOperationException(
                $"Application HTTP listener '{listenerId}' has no endpoint data source.");
    }

    public ValueTask ValidateAsync(
        HotfixRuntimeSnapshot previous,
        HotfixRuntimeSnapshot candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateCandidateManifest(candidate.HttpEndpoints);
        return default;
    }

    public ValueTask<IHotfixRuntimePublicationTransaction> PrepareAsync(
        HotfixRuntimeSnapshot previous,
        HotfixRuntimeSnapshot candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        var candidateManifest = candidate.HttpEndpoints.ToArray();
        ValidateCandidateManifest(candidateManifest);
        if (_manifest is not null)
        {
            return new ValueTask<IHotfixRuntimePublicationTransaction>(
                NoopHotfixRuntimePublicationTransaction.Instance);
        }

        var nextEndpoints = BuildEndpoints(candidateManifest);
        return new ValueTask<IHotfixRuntimePublicationTransaction>(
            new PublicationTransaction(this, candidateManifest, nextEndpoints));
    }

    private void ValidateCandidateManifest(
        IReadOnlyList<HotfixHttpEndpointDescriptor> candidateManifest)
    {
        ValidateManifest(candidateManifest);
        if (_manifest is not null
            && !ManifestEquals(_manifest, candidateManifest))
        {
            throw new InvalidOperationException(
                "Application HTTP route manifest differs from the initial Hotfix generation. " +
                "Adding, removing, or changing an HTTP service, method, or route requires a process restart.");
        }
    }

    private void ValidateManifest(IReadOnlyList<HotfixHttpEndpointDescriptor> manifest)
    {
        foreach (var endpoint in manifest)
        {
            if (IsManagementRoute(endpoint.RoutePattern))
            {
                throw new InvalidOperationException(
                    $"Application HTTP service '{endpoint.Service}' attempts to use reserved route '{endpoint.RoutePattern}'.");
            }

            try
            {
                _ = RoutePatternFactory.Parse(endpoint.RoutePattern);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Application HTTP service '{endpoint.Service}' has invalid route pattern '{endpoint.RoutePattern}': {exception.Message}",
                    exception);
            }
        }

        var knownServices = manifest
            .Select(static endpoint => endpoint.Service)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var listener in _runtime.Http.Listeners)
        {
            foreach (var service in listener.Services)
            {
                if (!knownServices.Contains(service))
                {
                    throw new InvalidOperationException(
                        $"HTTP listener '{listener.Id}' references unknown service '{service}'.");
                }
            }

            var duplicate = manifest
                .Where(endpoint => listener.Services.Contains(
                    endpoint.Service,
                    StringComparer.OrdinalIgnoreCase))
                .GroupBy(
                    static endpoint => (endpoint.Method, endpoint.RoutePattern),
                    HttpRouteKeyComparer.Instance)
                .FirstOrDefault(static group => group.Count() > 1);
            if (duplicate is not null)
            {
                throw new InvalidOperationException(
                    $"HTTP listener '{listener.Id}' has duplicate route {duplicate.Key.Method} {duplicate.Key.RoutePattern}.");
            }
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyList<Endpoint>> BuildEndpoints(
        IReadOnlyList<HotfixHttpEndpointDescriptor> manifest)
    {
        var endpoints = new Dictionary<string, IReadOnlyList<Endpoint>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var listener in _runtime.Http.Listeners)
        {
            var listenerEndpoints = new List<Endpoint>();
            for (var slot = 0; slot < manifest.Count; slot++)
            {
                var descriptor = manifest[slot];
                if (!listener.Services.Contains(
                        descriptor.Service,
                        StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var endpointSlot = slot;
                var builder = new RouteEndpointBuilder(
                    context => LakonaHttpHosting.DispatchApplicationAsync(
                        context,
                        listener,
                        endpointSlot),
                    RoutePatternFactory.Parse(descriptor.RoutePattern),
                    order: 0)
                {
                    DisplayName =
                        $"{descriptor.Service} {descriptor.Method} {descriptor.RoutePattern}"
                };
                builder.Metadata.Add(new HttpMethodMetadata([descriptor.Method]));
                listenerEndpoints.Add(builder.Build());
            }

            endpoints.Add(listener.Id, listenerEndpoints);
        }

        return endpoints;
    }

    private static bool ManifestEquals(
        IReadOnlyList<HotfixHttpEndpointDescriptor> left,
        IReadOnlyList<HotfixHttpEndpointDescriptor> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!left[index].Service.Equals(
                    right[index].Service,
                    StringComparison.OrdinalIgnoreCase)
                || !left[index].Method.Equals(
                    right[index].Method,
                    StringComparison.OrdinalIgnoreCase)
                || !left[index].RoutePattern.Equals(
                    right[index].RoutePattern,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsManagementRoute(string routePattern)
    {
        return routePattern.Equals("/_lakona", StringComparison.OrdinalIgnoreCase)
            || routePattern.StartsWith("/_lakona/", StringComparison.OrdinalIgnoreCase);
    }

    private void Activate(
        IReadOnlyList<HotfixHttpEndpointDescriptor> manifest,
        IReadOnlyDictionary<string, IReadOnlyList<Endpoint>> endpoints)
    {
        _manifest = manifest;
        foreach (var (listenerId, source) in _sources)
        {
            source.SetEndpoints(endpoints[listenerId]);
        }
    }

    private void Rollback()
    {
        _manifest = null;
        foreach (var source in _sources.Values)
        {
            source.SetEndpoints([]);
        }
    }

    private sealed class PublicationTransaction(
        LakonaApplicationHttpEndpointRegistry owner,
        IReadOnlyList<HotfixHttpEndpointDescriptor> manifest,
        IReadOnlyDictionary<string, IReadOnlyList<Endpoint>> endpoints) :
        IHotfixRuntimePublicationTransaction
    {
        private bool _activated;

        public ValueTask ActivateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _activated = true;
            owner.Activate(manifest, endpoints);
            return default;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_activated)
            {
                owner.Rollback();
                _activated = false;
            }

            return default;
        }

        public ValueTask DisposeAsync() => default;
    }

    private sealed class HttpRouteKeyComparer :
        IEqualityComparer<(string Method, string RoutePattern)>
    {
        public static HttpRouteKeyComparer Instance { get; } = new();

        public bool Equals(
            (string Method, string RoutePattern) x,
            (string Method, string RoutePattern) y)
        {
            return x.Method.Equals(y.Method, StringComparison.OrdinalIgnoreCase)
                && x.RoutePattern.Equals(y.RoutePattern, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Method, string RoutePattern) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Method),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RoutePattern));
        }
    }
}

internal sealed class LakonaApplicationHttpEndpointDataSource : EndpointDataSource
{
    private IReadOnlyList<Endpoint> _endpoints = [];
    private CancellationTokenSource _change = new();

    public override IReadOnlyList<Endpoint> Endpoints => Volatile.Read(ref _endpoints);

    public override IChangeToken GetChangeToken()
    {
        return new CancellationChangeToken(Volatile.Read(ref _change).Token);
    }

    public void SetEndpoints(IReadOnlyList<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        Volatile.Write(ref _endpoints, endpoints);
        var previous = Interlocked.Exchange(ref _change, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }
}
