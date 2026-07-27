using System.Reflection;
using MemoryPack;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterRpcMemoryPackContractTests
{
    private static readonly Type[] ProtocolTypes =
    [
        typeof(NodeEndpointDto),
        typeof(NodeActorHostDto),
        typeof(NodeRegistrationDto),
        typeof(NodeRecordDto),
        typeof(NodeDirectoryClientQueryDto),
        typeof(NodeRegisterRequest),
        typeof(NodeRegisterReply),
        typeof(NodeHeartbeatRequest),
        typeof(NodeHeartbeatReply),
        typeof(NodeUpdateStateRequest),
        typeof(NodeUpdateStateReply),
        typeof(NodeResolveRequest),
        typeof(NodeResolveReply),
        typeof(NodeQueryRequest),
        typeof(NodeQueryReply),
        typeof(NodeExpireRequest),
        typeof(NodeExpireReply),
        typeof(RouteLocationDto),
        typeof(RouteRegisterRequest),
        typeof(RouteRegisterReply),
        typeof(RouteResolveRequest),
        typeof(RouteResolveReply),
        typeof(RouteUnregisterRequest),
        typeof(RouteUnregisterReply),
        typeof(RouteRefreshLeaseRequest),
        typeof(RouteRefreshLeaseReply),
        typeof(RouteExpireRequest),
        typeof(RouteExpireReply),
        typeof(RouteClearByNodeRequest),
        typeof(RouteClearByNodeEpochRequest),
        typeof(RouteClearReply),
        typeof(ClusterSendRequest),
        typeof(ClusterSendReply),
        typeof(ClientNotificationDispatchRequest),
        typeof(ClientNotificationDispatchReply),
        typeof(ClientNotificationBatchDispatchRequest),
        typeof(ClientNotificationBatchDispatchReply),
        typeof(ClientNotificationCommand),
        typeof(ClientNotificationArgument),
        typeof(ClientNotificationMetadata),
        typeof(StartupActorDto),
        typeof(ClusterMembershipFrameRequest),
        typeof(ClusterMembershipFrameReply)
    ];

    [Fact]
    public void Framework_protocol_dtos_use_version_tolerant_memorypack_with_explicit_orders()
    {
        foreach (var type in ProtocolTypes)
        {
            var memoryPackable = type.GetCustomAttributesData()
                .SingleOrDefault(static attribute => attribute.AttributeType == typeof(MemoryPackableAttribute));
            Assert.NotNull(memoryPackable);
            Assert.Equal((int)GenerateType.VersionTolerant, (int)memoryPackable.ConstructorArguments[0].Value!);

            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var orders = properties
                .Select(property => property.GetCustomAttributesData()
                    .SingleOrDefault(static attribute => attribute.AttributeType == typeof(MemoryPackOrderAttribute)))
                .ToArray();
            Assert.DoesNotContain(orders, static order => order is null);
            Assert.Equal(
                Enumerable.Range(0, properties.Length),
                orders.Select(static order => (int)order!.ConstructorArguments[0].Value!));
        }
    }
}
