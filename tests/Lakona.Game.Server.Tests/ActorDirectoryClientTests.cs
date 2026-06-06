using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ActorDirectoryClientTests
{
    [Fact]
    public async Task ResolveAsync_discovers_actor_directory_host_and_caches_node()
    {
        var discovery = new RecordingNodeDiscovery
        {
            Nodes = [new NodeId("directory-a")]
        };
        var host = new RecordingActorDirectoryHostClient
        {
            ResolveRecord = new ActorDirectoryRecord(
                ActorId.From("room/1001"),
                new NodeId("node-room"),
                1,
                DateTimeOffset.UtcNow)
        };
        var client = new ActorDirectoryClient(discovery, host);
        var actorId = ActorId.From("room/1001");

        var first = await client.ResolveAsync(actorId, TestContext.Current.CancellationToken);
        var second = await client.ResolveAsync(actorId, TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Equal(new NodeId("node-room"), first.Node);
        Assert.NotNull(second);
        Assert.Equal(1, discovery.AnyCallCount);
        Assert.Equal(new NodeId("directory-a"), host.ResolveNodes[0]);
        Assert.Equal(new NodeId("directory-a"), host.ResolveNodes[1]);
    }

    [Fact]
    public async Task ResolveAsync_rediscovers_host_and_retries_once_after_host_failure()
    {
        var discovery = new RecordingNodeDiscovery
        {
            Nodes = [new NodeId("directory-a"), new NodeId("directory-b")]
        };
        var host = new RecordingActorDirectoryHostClient
        {
            ResolveException = new InvalidOperationException("directory-a unavailable"),
            ResolveRecord = new ActorDirectoryRecord(
                ActorId.From("room/1001"),
                new NodeId("node-room"),
                2,
                DateTimeOffset.UtcNow)
        };
        var client = new ActorDirectoryClient(discovery, host);

        var record = await client.ResolveAsync(ActorId.From("room/1001"), TestContext.Current.CancellationToken);

        Assert.NotNull(record);
        Assert.Equal(new NodeId("node-room"), record.Node);
        Assert.Equal(2, discovery.AnyCallCount);
        Assert.Collection(
            host.ResolveNodes,
            node => Assert.Equal(new NodeId("directory-a"), node),
            node => Assert.Equal(new NodeId("directory-b"), node));
    }

    [Fact]
    public async Task ResolveAsync_throws_when_actor_directory_host_is_missing()
    {
        var discovery = new RecordingNodeDiscovery();
        var host = new RecordingActorDirectoryHostClient();
        var client = new ActorDirectoryClient(discovery, host);

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await client.ResolveAsync(ActorId.From("room/1001"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_uses_discovered_host()
    {
        var discovery = new RecordingNodeDiscovery
        {
            Nodes = [new NodeId("directory-a")]
        };
        var host = new RecordingActorDirectoryHostClient
        {
            RegisterStatus = ActorDirectoryRegisterStatus.Registered
        };
        var client = new ActorDirectoryClient(discovery, host);

        var status = await client.RegisterAsync(
            ActorId.From("room/1001"),
            new NodeId("node-room"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ActorDirectoryRegisterStatus.Registered, status);
        Assert.Equal(new NodeId("directory-a"), host.RegisterNodes.Single());
    }

    [Fact]
    public async Task UnregisterAsync_uses_discovered_host()
    {
        var discovery = new RecordingNodeDiscovery
        {
            Nodes = [new NodeId("directory-a")]
        };
        var host = new RecordingActorDirectoryHostClient
        {
            UnregisterStatus = ActorDirectoryUnregisterStatus.Unregistered
        };
        var client = new ActorDirectoryClient(discovery, host);

        var status = await client.UnregisterAsync(
            ActorId.From("room/1001"),
            new NodeId("node-room"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ActorDirectoryUnregisterStatus.Unregistered, status);
        Assert.Equal(new NodeId("directory-a"), host.UnregisterNodes.Single());
    }

    private sealed class RecordingNodeDiscovery : IClusterNodeDiscovery
    {
        private int _nextNode;

        public List<NodeId> Nodes { get; set; } = [];

        public int AnyCallCount { get; private set; }

        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            ClusterFeature feature,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<NodeId?> AnyAsync(
            ClusterFeature feature,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(ActorDirectoryFeatures.ActorDirectory, feature);
            AnyCallCount++;

            if (_nextNode >= Nodes.Count)
            {
                return ValueTask.FromResult<NodeId?>(null);
            }

            return ValueTask.FromResult<NodeId?>(Nodes[_nextNode++]);
        }
    }

    private sealed class RecordingActorDirectoryHostClient : IActorDirectoryHostClient
    {
        private bool _resolveExceptionThrown;

        public List<NodeId> ResolveNodes { get; } = [];

        public List<NodeId> RegisterNodes { get; } = [];

        public List<NodeId> UnregisterNodes { get; } = [];

        public ActorDirectoryRecord? ResolveRecord { get; set; }

        public Exception? ResolveException { get; set; }

        public ActorDirectoryRegisterStatus RegisterStatus { get; set; } = ActorDirectoryRegisterStatus.Registered;

        public ActorDirectoryUnregisterStatus UnregisterStatus { get; set; } = ActorDirectoryUnregisterStatus.Unregistered;

        public ValueTask<ActorDirectoryRecord?> ResolveAsync(
            NodeId directoryNode,
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            ResolveNodes.Add(directoryNode);
            if (ResolveException is not null && !_resolveExceptionThrown)
            {
                _resolveExceptionThrown = true;
                throw ResolveException;
            }

            return ValueTask.FromResult(ResolveRecord);
        }

        public ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
            NodeId directoryNode,
            ActorId actorId,
            NodeId ownerNode,
            CancellationToken cancellationToken = default)
        {
            RegisterNodes.Add(directoryNode);
            return ValueTask.FromResult(RegisterStatus);
        }

        public ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
            NodeId directoryNode,
            ActorId actorId,
            NodeId ownerNode,
            CancellationToken cancellationToken = default)
        {
            UnregisterNodes.Add(directoryNode);
            return ValueTask.FromResult(UnregisterStatus);
        }
    }
}
