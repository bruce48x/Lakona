using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public sealed class InMemoryRouteDirectory : IRouteDirectory
    {
        private readonly ConcurrentDictionary<RouteKey, RouteLocation> _routes = new ConcurrentDictionary<RouteKey, RouteLocation>();

        public ValueTask<RouteRegistrationStatus> RegisterAsync(
            RouteLocation location,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (true)
            {
                if (_routes.TryGetValue(location.Route, out var existing))
                {
                    if (!existing.IsExpired(DateTimeOffset.UtcNow) && IsStaleRegistration(existing, location))
                    {
                        return new ValueTask<RouteRegistrationStatus>(RouteRegistrationStatus.StaleLocation);
                    }

                    if (_routes.TryUpdate(location.Route, location, existing))
                    {
                        return new ValueTask<RouteRegistrationStatus>(RouteRegistrationStatus.Registered);
                    }

                    continue;
                }

                if (_routes.TryAdd(location.Route, location))
                {
                    return new ValueTask<RouteRegistrationStatus>(RouteRegistrationStatus.Registered);
                }
            }
        }

        public ValueTask<RouteUnregisterStatus> UnregisterAsync(
            RouteKey route,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return new ValueTask<RouteUnregisterStatus>(_routes.TryRemove(route, out _)
                ? RouteUnregisterStatus.Removed
                : RouteUnregisterStatus.NotFound);
        }

        public ValueTask<RouteLocation?> ResolveAsync(
            RouteKey route,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_routes.TryGetValue(route, out var location))
            {
                return new ValueTask<RouteLocation?>((RouteLocation?)null);
            }

            if (location.IsExpired(now))
            {
                RemoveExact(route, location);
                return new ValueTask<RouteLocation?>((RouteLocation?)null);
            }

            return new ValueTask<RouteLocation?>(location);
        }

        public ValueTask<RouteLeaseRefreshStatus> RefreshLeaseAsync(
            RouteLocation expectedLocation,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (true)
            {
                if (!_routes.TryGetValue(expectedLocation.Route, out var current))
                {
                    return new ValueTask<RouteLeaseRefreshStatus>(RouteLeaseRefreshStatus.RouteNotFound);
                }

                if (current.IsExpired(now))
                {
                    RemoveExact(expectedLocation.Route, current);
                    return new ValueTask<RouteLeaseRefreshStatus>(RouteLeaseRefreshStatus.Expired);
                }

                if (!current.HasSameOwner(expectedLocation))
                {
                    return new ValueTask<RouteLeaseRefreshStatus>(RouteLeaseRefreshStatus.StaleLocation);
                }

                if (_routes.TryUpdate(expectedLocation.Route, current.WithExpiresAt(expiresAt), current))
                {
                    return new ValueTask<RouteLeaseRefreshStatus>(RouteLeaseRefreshStatus.Refreshed);
                }
            }
        }

        public ValueTask<int> ExpireAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var expired = 0;
            foreach (var route in _routes)
            {
                if (route.Value.IsExpired(now) && RemoveExact(route.Key, route.Value))
                {
                    expired++;
                }
            }

            return new ValueTask<int>(expired);
        }

        public ValueTask<int> ClearByNodeAsync(
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var removed = 0;
            foreach (var route in _routes)
            {
                if (route.Value.Node == node && RemoveExact(route.Key, route.Value))
                {
                    removed++;
                }
            }

            return new ValueTask<int>(removed);
        }

        public ValueTask<int> ClearByNodeEpochAsync(
            NodeId node,
            long nodeEpoch,
            CancellationToken cancellationToken = default)
        {
            if (nodeEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeEpoch), "Node epoch cannot be negative.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var removed = 0;
            foreach (var route in _routes)
            {
                if (route.Value.Node == node && route.Value.NodeEpoch == nodeEpoch &&
                    RemoveExact(route.Key, route.Value))
                {
                    removed++;
                }
            }

            return new ValueTask<int>(removed);
        }

        private bool RemoveExact(RouteKey route, RouteLocation location) =>
            ((ICollection<KeyValuePair<RouteKey, RouteLocation>>)_routes)
                .Remove(new KeyValuePair<RouteKey, RouteLocation>(route, location));

        private static bool IsStaleRegistration(RouteLocation existing, RouteLocation candidate)
        {
            if (candidate.Generation < existing.Generation)
            {
                return true;
            }

            if (candidate.Generation > existing.Generation)
            {
                return false;
            }

            if (candidate.Node != existing.Node)
            {
                return true;
            }

            return candidate.NodeEpoch < existing.NodeEpoch;
        }
    }
}
