using Lakona.Game.Cluster;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;
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
    public async Task StartAsyncRegistersDiscoverableHotfixFeatureDescriptors()
    {
        var directory = new RecordingNodeDirectory();
        var services = new ServiceCollection();
        services.AddSingleton<INodeDirectory>(directory);
        services.AddSingleton(new ClusterOptions
        {
            NodeId = "battle-1",
            AdvertisedEndpoints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cluster"] = "tcp://10.0.0.1:21001"
            },
            RouteLeaseSeconds = 45
        });
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Feature = ["battle-runtime"]
        });
        services.AddSingleton(new LakonaGameFeatureCatalog([], []));
        services.AddSingleton<IHotfixManager>(new HotfixFeatureManager(
            new HotfixFeatureDeclaration(
                "battle-runtime",
                typeof(object),
                Discoverable: true,
                new Dictionary<string, string>(StringComparer.Ordinal),
                [],
                [],
                [])));
        services.AddSingleton<IHostedService, LakonaGameClusterRegistrationHostedService>();
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetRequiredService<IHostedService>();

        await hosted.StartAsync(TestContext.Current.CancellationToken);

        var registration = Assert.Single(directory.Registrations);
        var feature = Assert.Single(registration.Features);
        Assert.Equal("battle-runtime", feature.Name);
    }

    [Fact]
    public async Task SuccessfulHotfixReloadRefreshesAdvertisedFeatures()
    {
        var directory = new RecordingNodeDirectory();
        var manager = new ReloadableHotfixFeatureManager(CreateHotfixSnapshot(
            new HotfixFeatureDeclaration(
                "battle-runtime",
                typeof(object),
                Discoverable: true,
                new Dictionary<string, string>(StringComparer.Ordinal),
                [],
                [],
                [])));
        var services = new ServiceCollection();
        services.AddSingleton<INodeDirectory>(directory);
        services.AddSingleton(new ClusterOptions
        {
            NodeId = "battle-1",
            AdvertisedEndpoints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cluster"] = "tcp://10.0.0.1:21001"
            },
            RouteLeaseSeconds = 45
        });
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Feature = ["battle-runtime", "state-store"]
        });
        services.AddSingleton(new LakonaGameFeatureCatalog([], []));
        services.AddSingleton<IHotfixManager>(manager);
        services.AddSingleton<IHostedService, LakonaGameClusterRegistrationHostedService>();
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetRequiredService<IHostedService>();

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        manager.RaiseSucceeded(CreateHotfixSnapshot(
            new HotfixFeatureDeclaration(
                "state-store",
                typeof(object),
                Discoverable: true,
                new Dictionary<string, string>(StringComparer.Ordinal),
                [],
                [],
                [])));
        await WaitUntilAsync(
            () => directory.Registrations.Count > 1,
            TestContext.Current.CancellationToken);

        var query = await directory.QueryAsync(
            new NodeDirectoryQuery("local", featureName: "state-store"),
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);
        var record = Assert.Single(query);
        var feature = Assert.Single(record.Features);
        Assert.Equal("state-store", feature.Name);
    }

    [Fact]
    public async Task FailedHotfixReloadDoesNotRefreshAdvertisedFeatures()
    {
        var directory = new RecordingNodeDirectory();
        var manager = new ReloadableHotfixFeatureManager(CreateHotfixSnapshot(
            new HotfixFeatureDeclaration(
                "battle-runtime",
                typeof(object),
                Discoverable: true,
                new Dictionary<string, string>(StringComparer.Ordinal),
                [],
                [],
                [])));
        var services = new ServiceCollection();
        services.AddSingleton<INodeDirectory>(directory);
        services.AddSingleton(new ClusterOptions
        {
            NodeId = "battle-1",
            AdvertisedEndpoints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cluster"] = "tcp://10.0.0.1:21001"
            },
            RouteLeaseSeconds = 45
        });
        services.AddSingleton(new LakonaGameFeatureCatalog([], []));
        services.AddSingleton<IHotfixManager>(manager);
        services.AddSingleton<IHostedService, LakonaGameClusterRegistrationHostedService>();
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetRequiredService<IHostedService>();

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        manager.RaiseFailed(CreateHotfixSnapshot(
            new HotfixFeatureDeclaration(
                "state-store",
                typeof(object),
                Discoverable: true,
                new Dictionary<string, string>(StringComparer.Ordinal),
                [],
                [],
                [])));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var registration = Assert.Single(directory.Registrations);
        var feature = Assert.Single(registration.Features);
        Assert.Equal("battle-runtime", feature.Name);
    }

    [Fact]
    public async Task StartAsyncRefreshesLeaseBeforeItExpires()
    {
        var directory = new RecordingNodeDirectory();
        var services = CreateRegistrationServices(directory, routeLeaseSeconds: 1);
        services.AddSingleton(new LakonaGameClusterRegistrationOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(10)
        });
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetRequiredService<IHostedService>();

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(
            () => directory.Heartbeats.Count > 0,
            TestContext.Current.CancellationToken);
        await hosted.StopAsync(TestContext.Current.CancellationToken);

        var heartbeat = Assert.Single(directory.Heartbeats.Take(1));
        var registration = Assert.Single(directory.Registrations);
        Assert.Equal("local", heartbeat.ClusterName);
        Assert.Equal(new NodeId("battle-1"), heartbeat.Node);
        Assert.Equal(1, heartbeat.NodeEpoch);
        Assert.True(heartbeat.LeaseExpiresAt > registration.LeaseExpiresAt);
    }

    [Fact]
    public async Task StopAsyncMarksRegisteredNodeDead()
    {
        var directory = new RecordingNodeDirectory();
        var services = CreateRegistrationServices(directory);
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetRequiredService<IHostedService>();

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await hosted.StopAsync(TestContext.Current.CancellationToken);

        var update = Assert.Single(directory.StateUpdates);
        Assert.Equal("local", update.ClusterName);
        Assert.Equal(new NodeId("battle-1"), update.Node);
        Assert.Equal(1, update.NodeEpoch);
        Assert.Equal(NodeState.Dead, update.State);
    }

    [Fact]
    public async Task ExpiredHeartbeatReRegistersNode()
    {
        var directory = new RecordingNodeDirectory
        {
            HeartbeatStatus = NodeHeartbeatStatus.Expired
        };
        var services = CreateRegistrationServices(directory, routeLeaseSeconds: 1);
        services.AddSingleton(new LakonaGameClusterRegistrationOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(10)
        });
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetRequiredService<IHostedService>();

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(
            () => directory.Registrations.Count > 1,
            TestContext.Current.CancellationToken);
        await hosted.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(directory.HeartbeatFailures.Count > 0);
        Assert.True(directory.Registrations.Count >= 2);
    }

    [Fact]
    public async Task EpochMismatchHeartbeatStopsHeartbeatLoop()
    {
        var directory = new RecordingNodeDirectory
        {
            HeartbeatStatus = NodeHeartbeatStatus.EpochMismatch
        };
        var services = CreateRegistrationServices(directory, routeLeaseSeconds: 1);
        services.AddSingleton(new LakonaGameClusterRegistrationOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(10)
        });
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetRequiredService<IHostedService>();

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(
            () => directory.HeartbeatFailures.Count > 0,
            TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        await hosted.StopAsync(TestContext.Current.CancellationToken);

        Assert.Single(directory.Registrations);
        Assert.Single(directory.HeartbeatFailures);
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

    private static ServiceCollection CreateRegistrationServices(
        RecordingNodeDirectory directory,
        int routeLeaseSeconds = 45)
    {
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
            RouteLeaseSeconds = routeLeaseSeconds
        });
        services.AddSingleton(new LakonaGameFeatureCatalog(
            [
                new LakonaGameFeatureDefinition("battle-runtime", typeof(BattleRuntimeFeature)),
                new LakonaGameFeatureDefinition("database", typeof(DatabaseFeature))
            ],
            [new BattleRuntimeFeature(), new DatabaseFeature()]));
        services.AddSingleton<IHostedService, LakonaGameClusterRegistrationHostedService>();
        return services;
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static HotfixSnapshot CreateHotfixSnapshot(params HotfixFeatureDeclaration[] features)
    {
        return new HotfixSnapshot(
            Version: "test",
            SourcePath: null,
            LoadedAtUtc: DateTimeOffset.UtcNow,
            DispatchTableVersion: 1,
            Methods: [],
            LastReloadStatus: HotfixReloadStatus.Succeeded,
            LastFailureMessage: null,
            LastFailureExceptionType: null,
            Features: features);
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
        public List<HeartbeatCall> Heartbeats { get; } = [];
        public List<HeartbeatCall> HeartbeatFailures { get; } = [];
        public List<StateUpdateCall> StateUpdates { get; } = [];
        public NodeHeartbeatStatus HeartbeatStatus { get; init; } = NodeHeartbeatStatus.Refreshed;

        public ValueTask<NodeRegistrationResult> RegisterAsync(
            NodeRegistration registration,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            Registrations.Add(registration);
            var record = new NodeRecord(
                registration.ClusterName,
                registration.NodeId,
                Registrations.Count,
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
            var call = new HeartbeatCall(clusterName, node, nodeEpoch, leaseExpiresAt);
            if (HeartbeatStatus == NodeHeartbeatStatus.Refreshed)
            {
                Heartbeats.Add(call);
            }
            else
            {
                HeartbeatFailures.Add(call);
            }

            return ValueTask.FromResult(HeartbeatStatus);
        }

        public ValueTask<NodeStateUpdateStatus> UpdateStateAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            NodeState state,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            StateUpdates.Add(new StateUpdateCall(clusterName, node, nodeEpoch, state));
            return ValueTask.FromResult(NodeStateUpdateStatus.Updated);
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
            var latest = Registrations
                .Select((registration, index) => new NodeRecord(
                    registration.ClusterName,
                    registration.NodeId,
                    index + 1,
                    registration.Endpoints,
                    registration.Features,
                    registration.Labels,
                    registration.State,
                    registration.LeaseExpiresAt,
                    now))
                .LastOrDefault(record =>
                    string.Equals(record.ClusterName, query.ClusterName, StringComparison.Ordinal) &&
                    (query.FeatureName is null || record.HasFeature(query.FeatureName)));
            return new ValueTask<IReadOnlyList<NodeRecord>>(latest is null ? [] : [latest]);
        }

        public ValueTask<int> ExpireAsync(
            string clusterName,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed record HeartbeatCall(
        string ClusterName,
        NodeId Node,
        long NodeEpoch,
        DateTimeOffset LeaseExpiresAt);

    private sealed record StateUpdateCall(
        string ClusterName,
        NodeId Node,
        long NodeEpoch,
        NodeState State);

    private sealed class HotfixFeatureManager(params HotfixFeatureDeclaration[] features) : IHotfixManager
    {
        public event EventHandler<HotfixReloadResult>? Reloaded
        {
            add { }
            remove { }
        }

        public HotfixSnapshot Current { get; } = new(
            Version: "test",
            SourcePath: null,
            LoadedAtUtc: DateTimeOffset.UtcNow,
            DispatchTableVersion: 1,
            Methods: [],
            LastReloadStatus: HotfixReloadStatus.Succeeded,
            LastFailureMessage: null,
            LastFailureExceptionType: null,
            Features: features);

        public ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult());
        }

        public ValueTask<HotfixReloadResult> ValidateAsync(
            IHotfixAssemblySource source,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult());
        }

        public ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult());
        }

        private HotfixReloadResult CreateResult()
        {
            return new HotfixReloadResult(
                HotfixReloadStatus.Succeeded,
                Current,
                Current.Version,
                Current.SourcePath,
                [],
                null);
        }
    }

    private sealed class ReloadableHotfixFeatureManager(HotfixSnapshot current) : IHotfixManager
    {
        public event EventHandler<HotfixReloadResult>? Reloaded;

        public HotfixSnapshot Current { get; private set; } = current;

        public ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult(HotfixReloadStatus.Succeeded, Current));
        }

        public ValueTask<HotfixReloadResult> ValidateAsync(
            IHotfixAssemblySource source,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult(HotfixReloadStatus.Succeeded, Current));
        }

        public ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult(HotfixReloadStatus.Succeeded, Current));
        }

        public void RaiseSucceeded(HotfixSnapshot snapshot)
        {
            Current = snapshot;
            Reloaded?.Invoke(this, CreateResult(HotfixReloadStatus.Succeeded, snapshot));
        }

        public void RaiseFailed(HotfixSnapshot failedSnapshot)
        {
            Reloaded?.Invoke(this, CreateResult(HotfixReloadStatus.Failed, failedSnapshot));
        }

        private static HotfixReloadResult CreateResult(HotfixReloadStatus status, HotfixSnapshot snapshot)
        {
            return new HotfixReloadResult(
                status,
                snapshot,
                snapshot.Version,
                snapshot.SourcePath,
                [],
                status == HotfixReloadStatus.Failed ? "reload failed" : null);
        }
    }
}
