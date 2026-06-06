using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public sealed class InMemoryRouteDirectory : IRouteDirectory
    {
        private readonly object _gate = new object();
        private readonly Dictionary<RouteKey, RouteLocation> _routes = new Dictionary<RouteKey, RouteLocation>();

        public ValueTask<RouteRegistrationStatus> RegisterAsync(
            RouteLocation location,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_routes.TryGetValue(location.Route, out var existing))
                {
                    if (existing.IsExpired(DateTimeOffset.UtcNow))
                    {
                        _routes.Remove(location.Route);
                    }
                    else if (IsStaleRegistration(existing, location))
                    {
                        return new ValueTask<RouteRegistrationStatus>(RouteRegistrationStatus.StaleLocation);
                    }
                }

                _routes[location.Route] = location;
            }

            return new ValueTask<RouteRegistrationStatus>(RouteRegistrationStatus.Registered);
        }

        public ValueTask<RouteLocation?> ResolveAsync(
            RouteKey route,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (!_routes.TryGetValue(route, out var location))
                {
                    return new ValueTask<RouteLocation?>((RouteLocation?)null);
                }

                if (location.IsExpired(now))
                {
                    _routes.Remove(route);
                    return new ValueTask<RouteLocation?>((RouteLocation?)null);
                }

                return new ValueTask<RouteLocation?>(location);
            }
        }

        public ValueTask<RouteLeaseRefreshStatus> RefreshLeaseAsync(
            RouteLocation expectedLocation,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (!_routes.TryGetValue(expectedLocation.Route, out var current))
                {
                    return new ValueTask<RouteLeaseRefreshStatus>(RouteLeaseRefreshStatus.RouteNotFound);
                }

                if (current.IsExpired(now))
                {
                    _routes.Remove(expectedLocation.Route);
                    return new ValueTask<RouteLeaseRefreshStatus>(RouteLeaseRefreshStatus.Expired);
                }

                if (!current.HasSameOwner(expectedLocation))
                {
                    return new ValueTask<RouteLeaseRefreshStatus>(RouteLeaseRefreshStatus.StaleLocation);
                }

                _routes[expectedLocation.Route] = current.WithExpiresAt(expiresAt);
                return new ValueTask<RouteLeaseRefreshStatus>(RouteLeaseRefreshStatus.Refreshed);
            }
        }

        public ValueTask<int> ExpireAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                var expired = _routes
                    .Where(route => route.Value.IsExpired(now))
                    .Select(route => route.Key)
                    .ToArray();

                foreach (var route in expired)
                {
                    _routes.Remove(route);
                }

                return new ValueTask<int>(expired.Length);
            }
        }

        public ValueTask<int> ClearByNodeAsync(
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                var stale = _routes
                    .Where(route => route.Value.Node == node)
                    .Select(route => route.Key)
                    .ToArray();

                foreach (var route in stale)
                {
                    _routes.Remove(route);
                }

                return new ValueTask<int>(stale.Length);
            }
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

            lock (_gate)
            {
                var stale = _routes
                    .Where(route => route.Value.Node == node && route.Value.NodeEpoch == nodeEpoch)
                    .Select(route => route.Key)
                    .ToArray();

                foreach (var route in stale)
                {
                    _routes.Remove(route);
                }

                return new ValueTask<int>(stale.Length);
            }
        }

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
