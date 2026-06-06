using System;
using System.Collections.Generic;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    public static class ULinkRpcNodeDirectoryRecordConverter
    {
        public static ULinkRpcNodeRegistrationDto ToDto(NodeRegistration registration)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            return new ULinkRpcNodeRegistrationDto
            {
                ClusterName = registration.ClusterName,
                Node = registration.NodeId.Value,
                Endpoints = CopyEndpoints(registration.Endpoints),
                Services = CopyServices(registration.Services),
                Labels = CopyDictionary(registration.Labels),
                State = (int)registration.State,
                LeaseExpiresAt = registration.LeaseExpiresAt
            };
        }

        public static NodeRegistration ToNodeRegistration(ULinkRpcNodeRegistrationDto? dto)
        {
            if (dto is null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            return new NodeRegistration(
                dto.ClusterName,
                dto.Node,
                ToEndpoints(dto.Endpoints),
                ToServices(dto.Services),
                dto.LeaseExpiresAt,
                ToNodeState(dto.State),
                CopyDictionary(dto.Labels));
        }

        public static ULinkRpcNodeRecordDto ToDto(NodeRecord record)
        {
            if (record is null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            return new ULinkRpcNodeRecordDto
            {
                ClusterName = record.ClusterName,
                Node = record.NodeId.Value,
                NodeEpoch = record.NodeEpoch,
                Endpoints = CopyEndpoints(record.Endpoints),
                Services = CopyServices(record.Services),
                Labels = CopyDictionary(record.Labels),
                State = (int)record.State,
                LeaseExpiresAt = record.LeaseExpiresAt,
                UpdatedAt = record.UpdatedAt
            };
        }

        public static NodeRecord ToNodeRecord(ULinkRpcNodeRecordDto? dto)
        {
            if (dto is null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            return new NodeRecord(
                dto.ClusterName,
                dto.Node,
                dto.NodeEpoch,
                ToEndpoints(dto.Endpoints),
                ToServices(dto.Services),
                CopyDictionary(dto.Labels),
                ToNodeState(dto.State),
                dto.LeaseExpiresAt,
                dto.UpdatedAt);
        }

        public static ULinkRpcNodeDirectoryQueryDto ToDto(NodeDirectoryQuery query)
        {
            if (query is null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return new ULinkRpcNodeDirectoryQueryDto
            {
                ClusterName = query.ClusterName,
                ServiceKind = query.ServiceKind,
                ServiceName = query.ServiceName,
                State = query.State.HasValue ? (int)query.State.Value : (int?)null,
                Labels = CopyDictionary(query.Labels),
                IncludeExpired = query.IncludeExpired
            };
        }

        public static NodeDirectoryQuery ToNodeDirectoryQuery(ULinkRpcNodeDirectoryQueryDto? dto)
        {
            if (dto is null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            return new NodeDirectoryQuery(
                dto.ClusterName,
                dto.ServiceKind,
                dto.ServiceName,
                dto.State.HasValue ? ToNodeState(dto.State.Value) : (NodeState?)null,
                CopyDictionary(dto.Labels),
                dto.IncludeExpired);
        }

        private static Dictionary<string, ULinkRpcNodeEndpointDto> CopyEndpoints(
            IReadOnlyDictionary<string, NodeEndpoint>? source)
        {
            var copy = new Dictionary<string, ULinkRpcNodeEndpointDto>(StringComparer.Ordinal);
            if (source is null)
            {
                return copy;
            }

            foreach (var endpoint in source)
            {
                copy[endpoint.Key] = new ULinkRpcNodeEndpointDto
                {
                    Address = endpoint.Value.Address,
                    Metadata = CopyDictionary(endpoint.Value.Metadata)
                };
            }

            return copy;
        }

        private static Dictionary<string, NodeEndpoint> ToEndpoints(
            IReadOnlyDictionary<string, ULinkRpcNodeEndpointDto>? source)
        {
            var copy = new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal);
            if (source is null)
            {
                return copy;
            }

            foreach (var endpoint in source)
            {
                copy[endpoint.Key] = new NodeEndpoint(
                    endpoint.Value.Address,
                    CopyDictionary(endpoint.Value.Metadata));
            }

            return copy;
        }

        private static List<ULinkRpcNodeServiceDto> CopyServices(
            IReadOnlyList<NodeServiceDescriptor>? source)
        {
            var copy = new List<ULinkRpcNodeServiceDto>();
            if (source is null)
            {
                return copy;
            }

            for (var i = 0; i < source.Count; i++)
            {
                copy.Add(new ULinkRpcNodeServiceDto
                {
                    Kind = source[i].Kind,
                    Name = source[i].Name,
                    Metadata = CopyDictionary(source[i].Metadata)
                });
            }

            return copy;
        }

        private static List<NodeServiceDescriptor> ToServices(
            IReadOnlyList<ULinkRpcNodeServiceDto>? source)
        {
            var copy = new List<NodeServiceDescriptor>();
            if (source is null)
            {
                return copy;
            }

            for (var i = 0; i < source.Count; i++)
            {
                copy.Add(new NodeServiceDescriptor(
                    source[i].Kind,
                    source[i].Name,
                    CopyDictionary(source[i].Metadata)));
            }

            return copy;
        }

        private static Dictionary<string, string> CopyDictionary(IReadOnlyDictionary<string, string>? source)
        {
            return source is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(source, StringComparer.Ordinal);
        }

        private static NodeState ToNodeState(int value)
        {
            if (!Enum.IsDefined(typeof(NodeState), value))
            {
                throw new InvalidOperationException("Node state value is invalid.");
            }

            return (NodeState)value;
        }
    }
}
