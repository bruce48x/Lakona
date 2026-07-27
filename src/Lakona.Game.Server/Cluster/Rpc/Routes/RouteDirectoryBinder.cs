using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class RouteDirectoryBinder
    {
        private readonly IRouteDirectory _directory;

        public RouteDirectoryBinder(IRouteDirectory directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        public void Bind(RpcServiceRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var service = registry.RegisterSingleton(
                ClusterProtocol.ServiceId,
                this,
                serviceName: nameof(RouteDirectoryBinder));
            service.Register<RouteRegisterRequest, RouteRegisterReply>(ClusterProtocol.RegisterRouteMethodId, static (binder, request, cancellationToken) => binder.RegisterAsync(request, cancellationToken), methodName: nameof(RegisterAsync));
            service.Register<RouteResolveRequest, RouteResolveReply>(ClusterProtocol.ResolveRouteMethodId, static (binder, request, cancellationToken) => binder.ResolveAsync(request, cancellationToken), methodName: nameof(ResolveAsync));
            service.Register<RouteUnregisterRequest, RouteUnregisterReply>(ClusterProtocol.UnregisterRouteMethodId, static (binder, request, cancellationToken) => binder.UnregisterAsync(request, cancellationToken), methodName: nameof(UnregisterAsync));
            service.Register<RouteRefreshLeaseRequest, RouteRefreshLeaseReply>(ClusterProtocol.RefreshRouteLeaseMethodId, static (binder, request, cancellationToken) => binder.RefreshLeaseAsync(request, cancellationToken), methodName: nameof(RefreshLeaseAsync));
            service.Register<RouteExpireRequest, RouteExpireReply>(ClusterProtocol.ExpireRoutesMethodId, static (binder, request, cancellationToken) => binder.ExpireAsync(request, cancellationToken), methodName: nameof(ExpireAsync));
            service.Register<RouteClearByNodeRequest, RouteClearReply>(ClusterProtocol.ClearRoutesByNodeMethodId, static (binder, request, cancellationToken) => binder.ClearByNodeAsync(request, cancellationToken), methodName: nameof(ClearByNodeAsync));
            service.Register<RouteClearByNodeEpochRequest, RouteClearReply>(ClusterProtocol.ClearRoutesByNodeEpochMethodId, static (binder, request, cancellationToken) => binder.ClearByNodeEpochAsync(request, cancellationToken), methodName: nameof(ClearByNodeEpochAsync));
        }

        public static void Bind(RpcServiceRegistry registry, IRouteDirectory directory)
        {
            new RouteDirectoryBinder(directory).Bind(registry);
        }

        private async ValueTask<RouteRegisterReply> RegisterAsync(
            RouteRegisterRequest request,
            CancellationToken cancellationToken)
        {
            if (request.Location is null)
            {
                throw new InvalidOperationException("Route location is required.");
            }

            var status = await _directory.RegisterAsync(
                RouteLocationConverter.ToRouteLocation(request.Location),
                cancellationToken).ConfigureAwait(false);

            return new RouteRegisterReply
            {
                Status = (int)status
            };
        }

        private async ValueTask<RouteResolveReply> ResolveAsync(
            RouteResolveRequest request,
            CancellationToken cancellationToken)
        {
            var location = await _directory.ResolveAsync(
                request.Route,
                request.Now,
                cancellationToken).ConfigureAwait(false);

            return new RouteResolveReply
            {
                Location = location is null ? null : RouteLocationConverter.ToDto(location)
            };
        }

        private async ValueTask<RouteUnregisterReply> UnregisterAsync(
            RouteUnregisterRequest request,
            CancellationToken cancellationToken)
        {
            var status = await _directory.UnregisterAsync(request.Route, cancellationToken).ConfigureAwait(false);

            return new RouteUnregisterReply
            {
                Status = (int)status
            };
        }

        private async ValueTask<RouteRefreshLeaseReply> RefreshLeaseAsync(
            RouteRefreshLeaseRequest request,
            CancellationToken cancellationToken)
        {
            if (request.ExpectedLocation is null)
            {
                throw new InvalidOperationException("Expected route location is required.");
            }

            var status = await _directory.RefreshLeaseAsync(
                RouteLocationConverter.ToRouteLocation(request.ExpectedLocation),
                request.ExpiresAt,
                request.Now,
                cancellationToken).ConfigureAwait(false);

            return new RouteRefreshLeaseReply
            {
                Status = (int)status
            };
        }

        private async ValueTask<RouteExpireReply> ExpireAsync(
            RouteExpireRequest request,
            CancellationToken cancellationToken)
        {
            var removed = await _directory.ExpireAsync(request.Now, cancellationToken).ConfigureAwait(false);
            return new RouteExpireReply
            {
                Removed = removed
            };
        }

        private async ValueTask<RouteClearReply> ClearByNodeAsync(
            RouteClearByNodeRequest request,
            CancellationToken cancellationToken)
        {
            var removed = await _directory.ClearByNodeAsync(request.Node, cancellationToken).ConfigureAwait(false);
            return new RouteClearReply
            {
                Removed = removed
            };
        }

        private async ValueTask<RouteClearReply> ClearByNodeEpochAsync(
            RouteClearByNodeEpochRequest request,
            CancellationToken cancellationToken)
        {
            var removed = await _directory.ClearByNodeEpochAsync(
                request.Node,
                request.NodeEpoch,
                cancellationToken).ConfigureAwait(false);

            return new RouteClearReply
            {
                Removed = removed
            };
        }
    }
}
