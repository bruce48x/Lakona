using System;
using System.Collections.Generic;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    public static class NodeDirectoryRecordConverter
    {
        public static NodeRegistrationDto ToDto(NodeRegistration registration)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            return new NodeRegistrationDto
            {
                ClusterName = registration.ClusterName,
                Node = registration.NodeId.Value,
                Endpoints = CopyEndpoints(registration.Endpoints),
                Features = CopyFeatures(registration.Features),
                Labels = CopyDictionary(registration.Labels),
                State = (int)registration.State,
                LeaseExpiresAt = registration.LeaseExpiresAt
            };
        }

        public static NodeRegistration ToNodeRegistration(NodeRegistrationDto? dto)
        {
            if (dto is null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            return new NodeRegistration(
                dto.ClusterName,
                dto.Node,
                ToEndpoints(dto.Endpoints),
                ToFeatures(dto.Features),
                dto.LeaseExpiresAt,
                ToNodeState(dto.State),
                CopyDictionary(dto.Labels));
        }

        public static NodeRecordDto ToDto(NodeRecord record)
        {
            if (record is null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            return new NodeRecordDto
            {
                ClusterName = record.ClusterName,
                Node = record.NodeId.Value,
                NodeEpoch = record.NodeEpoch,
                Endpoints = CopyEndpoints(record.Endpoints),
                Features = CopyFeatures(record.Features),
                Labels = CopyDictionary(record.Labels),
                State = (int)record.State,
                LeaseExpiresAt = record.LeaseExpiresAt,
                UpdatedAt = record.UpdatedAt
            };
        }

        public static NodeRecord ToNodeRecord(NodeRecordDto? dto)
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
                ToFeatures(dto.Features),
                CopyDictionary(dto.Labels),
                ToNodeState(dto.State),
                dto.LeaseExpiresAt,
                dto.UpdatedAt);
        }

        public static NodeDirectoryClientQueryDto ToDto(NodeDirectoryQuery query)
        {
            if (query is null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return new NodeDirectoryClientQueryDto
            {
                ClusterName = query.ClusterName,
                FeatureName = query.FeatureName,
                State = query.State.HasValue ? (int)query.State.Value : (int?)null,
                Labels = CopyDictionary(query.Labels),
                IncludeExpired = query.IncludeExpired
            };
        }

        public static NodeDirectoryQuery ToNodeDirectoryQuery(NodeDirectoryClientQueryDto? dto)
        {
            if (dto is null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            return new NodeDirectoryQuery(
                dto.ClusterName,
                dto.FeatureName,
                dto.State.HasValue ? ToNodeState(dto.State.Value) : (NodeState?)null,
                CopyDictionary(dto.Labels),
                dto.IncludeExpired);
        }

        private static Dictionary<string, NodeEndpointDto> CopyEndpoints(
            IReadOnlyDictionary<string, NodeEndpoint>? source)
        {
            var copy = new Dictionary<string, NodeEndpointDto>(StringComparer.Ordinal);
            if (source is null)
            {
                return copy;
            }

            foreach (var endpoint in source)
            {
                copy[endpoint.Key] = new NodeEndpointDto
                {
                    Address = endpoint.Value.Address,
                    Metadata = CopyDictionary(endpoint.Value.Metadata)
                };
            }

            return copy;
        }

        private static Dictionary<string, NodeEndpoint> ToEndpoints(
            IReadOnlyDictionary<string, NodeEndpointDto>? source)
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

        private static List<NodeFeatureDto> CopyFeatures(
            IReadOnlyList<NodeFeatureDescriptor>? source)
        {
            var copy = new List<NodeFeatureDto>();
            if (source is null)
            {
                return copy;
            }

            for (var i = 0; i < source.Count; i++)
            {
                copy.Add(new NodeFeatureDto
                {
                    Name = source[i].Name,
                    Metadata = CopyDictionary(source[i].Metadata)
                });
            }

            return copy;
        }

        private static List<NodeFeatureDescriptor> ToFeatures(
            IReadOnlyList<NodeFeatureDto>? source)
        {
            var copy = new List<NodeFeatureDescriptor>();
            if (source is null)
            {
                return copy;
            }

            for (var i = 0; i < source.Count; i++)
            {
                copy.Add(new NodeFeatureDescriptor(
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
