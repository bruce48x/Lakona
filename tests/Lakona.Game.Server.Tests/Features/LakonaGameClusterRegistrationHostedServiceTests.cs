using Lakona.Game.Cluster;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Features;

public sealed class LakonaGameClusterRegistrationHostedServiceTests
{
    [Fact]
    public async Task StartAsyncRegistersEndpointMapAndDiscoverableFeatureMetadata()
    {
        var directory = new RecordingNodeDirectory();
        var services = new ServiceCollection();
        services.AddSingleton<INodeDirectory>(directory);
        services.AddSingleton(new ClusterOptions
        {
            NodeId = "battle-1",
            AdvertisedEndpoints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cluster"] = "tcp://10.0.0.1:21001",
                ["kcp"] = "kcp://10.0.0.1:20001"
            },
            RouteLeaseSeconds = 45
        });
        services.AddSingleton(new LakonaGameFeatureCatalog(
            [
                new LakonaGameFeatureDefinition("battle-runtime", typeof(BattleRuntimeFeature)),
                new LakonaGameFeatureDefinition("database", typeof(DatabaseFeature))
            ],
            [new BattleRuntimeFeature(), new DatabaseFeature()]));
        services.AddSingleton<IHostedService, LakonaGameClusterRegistrationHostedService>();
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetRequiredService<IHostedService>();

        await hosted.StartAsync(TestContext.Current.CancellationToken);

        var registration = Assert.Single(directory.Registrations);
        Assert.Equal("local", registration.ClusterName);
        Assert.Equal(new NodeId("battle-1"), registration.NodeId);
        Assert.Equal("tcp://10.0.0.1:21001", registration.Endpoints["cluster"].Address);
        Assert.Equal("kcp://10.0.0.1:20001", registration.Endpoints["kcp"].Address);
        var feature = Assert.Single(registration.Features);
        Assert.Equal("battle-runtime", feature.Name);
        Assert.Equal("cn-east", feature.Metadata["region"]);
        Assert.Equal(NodeState.Ready, registration.State);
    }

    [Fact]
    public async Task StartAsyncNoOpsWhenNodeDirectoryIsNotRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ClusterOptions());
        services.AddSingleton(new LakonaGameFeatureCatalog([], []));
        services.AddSingleton<IHostedService, LakonaGameClusterRegistrationHostedService>();
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IHostedService>()
            .StartAsync(TestContext.Current.CancellationToken);
    }

    private sealed class BattleRuntimeFeature : LakonaGameFeature
    {
        public override IReadOnlyDictionary<string, string> Metadata { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["region"] = "cn-east"
            };
    }

    private sealed class DatabaseFeature : LakonaGameFeature
    {
        public override bool Discoverable => false;
    }

    private sealed class RecordingNodeDirectory : INodeDirectory
    {
        public List<NodeRegistration> Registrations { get; } = [];

        public ValueTask<NodeRegistrationResult> RegisterAsync(
            NodeRegistration registration,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            Registrations.Add(registration);
            var record = new NodeRecord(
                registration.ClusterName,
                registration.NodeId,
                1,
                registration.Endpoints,
                registration.Features,
                registration.Labels,
                registration.State,
                registration.LeaseExpiresAt,
                now);
            return ValueTask.FromResult(new NodeRegistrationResult(NodeRegistrationStatus.Registered, record));
        }

        public ValueTask<NodeHeartbeatStatus> HeartbeatAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            DateTimeOffset leaseExpiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<NodeStateUpdateStatus> UpdateStateAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            NodeState state,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<NodeRecord?> ResolveAsync(
            string clusterName,
            NodeId node,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyList<NodeRecord>> QueryAsync(
            NodeDirectoryQuery query,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ExpireAsync(
            string clusterName,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
