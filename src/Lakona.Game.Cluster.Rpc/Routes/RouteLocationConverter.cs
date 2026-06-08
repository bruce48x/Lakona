using System;
using System.Collections.Generic;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    public static class RouteLocationConverter
    {
        public static RouteLocationDto ToDto(RouteLocation location)
        {
            if (location is null)
            {
                throw new ArgumentNullException(nameof(location));
            }

            return new RouteLocationDto
            {
                Route = location.Route.Value,
                Node = location.Node.Value,
                EndpointAddress = location.Endpoint.Address,
                EndpointMetadata = CopyDictionary(location.Endpoint.Metadata),
                ExpiresAt = location.ExpiresAt,
                NodeEpoch = location.NodeEpoch,
                Generation = location.Generation,
                Metadata = CopyDictionary(location.Metadata)
            };
        }

        public static RouteLocation ToRouteLocation(RouteLocationDto? dto)
        {
            if (dto is null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            return new RouteLocation(
                dto.Route,
                dto.Node,
                new NodeEndpoint(dto.EndpointAddress, CopyDictionary(dto.EndpointMetadata)),
                dto.ExpiresAt,
                dto.NodeEpoch,
                dto.Generation,
                CopyDictionary(dto.Metadata));
        }

        private static Dictionary<string, string> CopyDictionary(IReadOnlyDictionary<string, string>? source)
        {
            return source is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(source, StringComparer.Ordinal);
        }
    }
}
