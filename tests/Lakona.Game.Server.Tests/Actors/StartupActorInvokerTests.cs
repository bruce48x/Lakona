using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class StartupActorInvokerTests
{
    [Fact]
    public async Task Production_registrations_resolve_startup_actor_invoker()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(static context => context.Candidates[0]);
        var snapshot = new HotfixRuntimeSnapshot(new NoopHotfixInvoker(), new EmptyServiceProvider(), [declaration], "build-1");
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddLakonaGameServerActors();
        services.AddLakonaGameClusterEndpoint();
        services.AddSingleton<IHotfixRuntimeAccessor>(new StubHotfixAccessor(snapshot));
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<StartupActorInvoker>(provider.GetRequiredService<IStartupActorInvoker>());
    }

    [Fact]
    public async Task CallAsync_sorts_candidates_and_preserves_metadata()
    {
        IReadOnlyList<StartupActorCandidate>? observed = null;
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(context =>
        {
            observed = context.Candidates;
            return context.Candidates[0];
        });
        var invoker = CreateInvoker(declaration, [
            Node("node-b", 2, "blue"),
            Node("node-a", 1, "green")]);

        var result = await invoker.CallAsync<TestActor, string, Request, string>(
            "tenant", "test", "Ping", 1, new Request("hello"),
            static (id, request, _) => ValueTask.FromResult($"{id.Value}:{request.Value}"),
            TestContext.Current.CancellationToken);

        Assert.Equal("test/@startup/node-a:hello", result);
        Assert.Equal(["node-a", "node-b"], observed!.Select(static candidate => candidate.NodeId));
        Assert.Equal("green", observed![0].Metadata["zone"]);
    }

    [Fact]
    public async Task Adding_a_node_does_not_move_an_existing_startup_key_affinity()
    {
        var selectorCalls = 0;
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(context =>
        {
            selectorCalls++;
            return context.Candidates[^1];
        });
        var snapshot = new HotfixRuntimeSnapshot(
            new NoopHotfixInvoker(),
            new EmptyServiceProvider(),
            [declaration],
            "build-1");
        var nodes = new MutableNodeDirectory([Node("node-a", 1), Node("node-b", 2)]);
        var cluster = new ClusterIncarnationId(
            Guid.Parse("50000000-0000-0000-0000-000000000000"));
        var membership = new MutableMembership(CreateMembership(cluster, 1, "node-a", "node-b"));
        var directory = new InMemoryActorDirectory();
        var invoker = new StartupActorInvoker(
            new StubHotfixAccessor(snapshot),
            nodes,
            new LocalActorNodeIdentity("node-b"),
            new RecordingRemoteInvoker(),
            new ClusterNodeSenderOptions { ClusterName = "local" },
            new RemoteActorOptions(),
            logger: null,
            activationDirectory: directory,
            actorDirectory: directory,
            membership: membership);

        var first = await invoker.CallAsync<TestActor, string, Request, string>(
            "tenant", "test", "Ping", 1, new Request("one"),
            static (id, _, _) => ValueTask.FromResult(id.Value),
            TestContext.Current.CancellationToken);

        nodes.Nodes = [Node("node-a", 1), Node("node-b", 2), Node("node-c", 3)];
        membership.Current = CreateMembership(cluster, 2, "node-a", "node-b", "node-c");
        var second = await invoker.CallAsync<TestActor, string, Request, string>(
            "tenant", "test", "Ping", 1, new Request("two"),
            static (id, _, _) => ValueTask.FromResult(id.Value),
            TestContext.Current.CancellationToken);

        Assert.Equal("test/@startup/node-b", first);
        Assert.Equal(first, second);
        Assert.Equal(1, selectorCalls);
    }

    [Fact]
    public async Task Sticky_remote_startup_call_carries_the_exact_replica_activation()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(
            static context => context.Candidates[0]);
        var snapshot = new HotfixRuntimeSnapshot(
            new NoopHotfixInvoker(),
            new EmptyServiceProvider(),
            [declaration],
            "build-1");
        var cluster = new ClusterIncarnationId(
            Guid.Parse("50000000-0000-0000-0000-000000000000"));
        var membership = new MutableMembership(CreateMembership(cluster, 1, "node-a", "node-b"));
        var directory = new InMemoryActorDirectory();
        var remote = new RecordingRemoteInvoker();
        var invoker = new StartupActorInvoker(
            new StubHotfixAccessor(snapshot),
            new StubNodeDirectory([Node("node-a", 0), Node("node-b", 0)]),
            new LocalActorNodeIdentity("node-local"),
            remote,
            new ClusterNodeSenderOptions { ClusterName = "local" },
            new RemoteActorOptions(),
            logger: null,
            activationDirectory: directory,
            actorDirectory: directory,
            membership: membership);

        await invoker.PostAsync<TestActor, string, Request>(
            "tenant",
            "test",
            "Ping",
            1,
            new Request("hello"),
            static (_, _, _) => ValueTask.FromResult(ActorTellResult.Accepted),
            TestContext.Current.CancellationToken);

        var invocation = Assert.Single(remote.Invocations);
        var expectedOwner = membership.Current.Members.Single(
            static member => member.Reference.Node == new NodeId("node-a")).Reference;
        Assert.Equal(ActorId.From("test/@startup/node-a"), invocation.ActorId);
        Assert.Equal(expectedOwner, invocation.OwnerReference);
        Assert.NotNull(invocation.ActivationId);
        Assert.True(invocation.ActivationVersion > 0);
        var replicaActivation = await directory.ResolveAsync(
            invocation.ActorId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(replicaActivation);
        Assert.Equal(invocation.OwnerReference, replicaActivation.OwnerReference);
        Assert.Equal(invocation.ActivationId, replicaActivation.ActivationId);
        Assert.Equal(invocation.ActivationVersion, replicaActivation.Version);
    }

    [Fact]
    public async Task CallAsync_reselects_only_after_definitely_not_executed_remote_attempt()
    {
        var selections = new List<IReadOnlyList<string>>();
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(context =>
        {
            selections.Add(context.Candidates.Select(static candidate => candidate.NodeId).ToArray());
            return context.Candidates[0];
        });
        var remote = new RecordingRemoteInvoker(
            RemoteActorInvocationResult.Failed(
                RemoteActorStatus.NodeUnavailable,
                "stale",
                RemoteActorRetrySafety.DefinitelyNotExecuted));
        var invoker = CreateInvoker(declaration, [Node("node-a", 1), Node("node-b", 2)], remote, "node-local");

        await Assert.ThrowsAsync<StartupActorUnavailableException>(async () => await invoker.CallAsync<TestActor, string, Request>(
            "tenant", "test", "Ping", 1, new Request("hello"),
            static (_, _, _) => ValueTask.CompletedTask,
            TestContext.Current.CancellationToken));

        Assert.Equal(2, remote.Invocations.Count);
        Assert.Equal([["node-a", "node-b"], ["node-b"]], selections);
        Assert.Equal([1L, 2L], remote.Invocations.Select(static invocation => invocation.ExpectedNodeEpoch));
        Assert.Equal(1UL, remote.Invocations[0].MethodId);
    }

    [Fact]
    public async Task CallAsync_does_not_reselect_indeterminate_remote_failure()
    {
        var selectorCalls = 0;
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(context =>
        {
            selectorCalls++;
            return context.Candidates[0];
        });
        var remote = new RecordingRemoteInvoker(RemoteActorInvocationResult.Failed(
            RemoteActorStatus.Timeout, "ambiguous", RemoteActorRetrySafety.Indeterminate));
        var invoker = CreateInvoker(declaration, [Node("node-a", 1), Node("node-b", 2)], remote, "node-local");

        await Assert.ThrowsAsync<ActorCallTimeoutException>(async () => await invoker.CallAsync<TestActor, string, Request>(
            "tenant", "test", "Ping", 1, new Request("hello"),
            static (_, _, _) => ValueTask.CompletedTask,
            TestContext.Current.CancellationToken));

        Assert.Equal(1, selectorCalls);
        Assert.Single(remote.Invocations);
    }

    [Fact]
    public async Task CallAsync_rejects_outsider_selector_result()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(
            static _ => new StartupActorCandidate("outsider", 9));
        var invoker = CreateInvoker(declaration, [Node("node-a", 1)]);

        await Assert.ThrowsAsync<StartupActorSelectionException>(async () =>
            await invoker.CallAsync<TestActor, string, Request>(
                "tenant", "test", "Ping", 1, new Request("hello"),
                static (_, _, _) => ValueTask.CompletedTask,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CallAsync_wraps_selector_failure()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(
            static _ => throw new InvalidOperationException("selector bug"));
        var invoker = CreateInvoker(declaration, [Node("node-a", 1)]);

        var exception = await Assert.ThrowsAsync<StartupActorSelectionException>(async () =>
            await invoker.CallAsync<TestActor, string, Request>(
                "tenant", "test", "Ping", 1, new Request("hello"),
                static (_, _, _) => ValueTask.CompletedTask,
                TestContext.Current.CancellationToken));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task CallAsync_rejects_wrong_key_type()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(static context => context.Candidates[0]);
        var invoker = CreateInvoker(declaration, [Node("node-a", 1)]);

        await Assert.ThrowsAsync<StartupActorSelectionException>(async () =>
            await invoker.CallAsync<TestActor, int, Request>(
                42, "test", "Ping", 1, new Request("hello"),
                static (_, _, _) => ValueTask.CompletedTask,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CallAsync_returns_unavailable_when_no_ready_compatible_descriptor_exists()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(static context => context.Candidates[0]);
        var invoker = CreateInvoker(declaration, []);

        await Assert.ThrowsAsync<StartupActorUnavailableException>(async () =>
            await invoker.CallAsync<TestActor, string, Request>(
                "tenant", "test", "Ping", 1, new Request("hello"),
                static (_, _, _) => ValueTask.CompletedTask,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CallAsync_matches_the_default_build_tag_when_source_version_is_missing()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(static context => context.Candidates[0]);
        var invoker = CreateInvoker(declaration, [Node("node-a", 1, buildTag: "hotfix")], sourceVersion: null);

        await invoker.CallAsync<TestActor, string, Request>(
            "tenant", "test", "Ping", 1, new Request("hello"),
            static (_, _, _) => ValueTask.CompletedTask,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CallAsync_does_not_reselect_business_actor_not_found_exception()
    {
        var selectorCalls = 0;
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(context =>
        {
            selectorCalls++;
            return context.Candidates[0];
        });
        var invoker = CreateInvoker(declaration, [Node("node-a", 1), Node("node-b", 2)]);
        var businessFailure = new ActorNotFoundException(
            ActorId.From("business/reference"), "dependency", "Load", "missing business entity");

        var actual = await Assert.ThrowsAsync<ActorNotFoundException>(async () =>
            await invoker.CallAsync<TestActor, string, Request>(
                "tenant", "test", "Ping", 1, new Request("hello"),
                (_, _, _) => ValueTask.FromException(businessFailure),
                TestContext.Current.CancellationToken));

        Assert.Same(businessFailure, actual);
        Assert.Equal(1, selectorCalls);
    }

    private static StartupActorInvoker CreateInvoker(
        ActorStartupDeclaration declaration,
        IReadOnlyList<NodeRecord> nodes,
        RecordingRemoteInvoker? remote = null,
        string localNode = "node-a",
        string? sourceVersion = "build-1")
    {
        var snapshot = new HotfixRuntimeSnapshot(
            new NoopHotfixInvoker(),
            new EmptyServiceProvider(),
            [declaration],
            sourceVersion);
        return new StartupActorInvoker(
            new StubHotfixAccessor(snapshot),
            new StubNodeDirectory(nodes),
            new LocalActorNodeIdentity(new NodeId(localNode)),
            remote ?? new RecordingRemoteInvoker(),
            new ClusterNodeSenderOptions { ClusterName = "local" },
            new RemoteActorOptions());
    }

    private static NodeRecord Node(string id, long epoch, string zone = "zone", string buildTag = "build-1") => new(
        "local", new NodeId(id), epoch,
        new Dictionary<string, NodeEndpoint> { ["cluster"] = new($"tcp://{id}:21000") },
        [], [new StartupActorDescriptor("test", Policy(), buildTag, new Dictionary<string, string> { ["zone"] = zone })],
        null, NodeState.Ready, DateTimeOffset.UtcNow.AddMinutes(1), DateTimeOffset.UtcNow);

    private static string Policy() => $"startup:v1:{typeof(TestActor).FullName}:{typeof(string).FullName}";

    private static ClusterMembershipSnapshot CreateMembership(
        ClusterIncarnationId cluster,
        long view,
        params string[] nodes) => new(
        cluster,
        new MembershipViewId(view),
        nodes.Select((node, index) => new ClusterMember(
            new NodeReference(
                cluster,
                new NodeId(node),
                new NodeIncarnationId(Guid.Parse($"{index + 1:D8}-5000-0000-0000-000000000000"))),
            ClusterMemberState.Ready,
            new NodeEndpoint($"tcp://{node}:21000"),
            isVoter: true)).ToArray());

    private sealed record Request(string Value);
    [ActorName("test")]
    private sealed class TestActor : IActor { }
    private sealed class EmptyServiceProvider : IServiceProvider { public object? GetService(Type serviceType) => null; }
    private sealed class NoopHotfixInvoker : IHotfixServiceInvoker
    {
        public ValueTask<TResult> InvokeHttpAsync<TArg, TResult>(int endpointSlot, TArg arg, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask InvokeAsync<TContract, TArg>(int methodId, TArg arg, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(int methodId, TArg arg, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class StubHotfixAccessor(HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor { public HotfixRuntimeSnapshot Current => snapshot; }
    private sealed class StubNodeDirectory(IReadOnlyList<NodeRecord> nodes) : INodeDirectory
    {
        public ValueTask<IReadOnlyList<NodeRecord>> QueryAsync(NodeDirectoryQuery query, DateTimeOffset now, CancellationToken cancellationToken = default) => ValueTask.FromResult(nodes);
        public ValueTask<NodeRegistrationResult> RegisterAsync(NodeRegistration registration, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<NodeHeartbeatStatus> HeartbeatAsync(string clusterName, NodeId node, long nodeEpoch, DateTimeOffset leaseExpiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<NodeStateUpdateStatus> UpdateStateAsync(string clusterName, NodeId node, long nodeEpoch, NodeState state, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<NodeRecord?> ResolveAsync(string clusterName, NodeId node, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> ExpireAsync(string clusterName, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class MutableNodeDirectory(IReadOnlyList<NodeRecord> nodes) : INodeDirectory
    {
        public IReadOnlyList<NodeRecord> Nodes { get; set; } = nodes;
        public ValueTask<IReadOnlyList<NodeRecord>> QueryAsync(NodeDirectoryQuery query, DateTimeOffset now, CancellationToken cancellationToken = default) => ValueTask.FromResult(Nodes);
        public ValueTask<NodeRegistrationResult> RegisterAsync(NodeRegistration registration, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<NodeHeartbeatStatus> HeartbeatAsync(string clusterName, NodeId node, long nodeEpoch, DateTimeOffset leaseExpiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<NodeStateUpdateStatus> UpdateStateAsync(string clusterName, NodeId node, long nodeEpoch, NodeState state, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<NodeRecord?> ResolveAsync(string clusterName, NodeId node, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> ExpireAsync(string clusterName, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class MutableMembership(ClusterMembershipSnapshot current) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; set; } = current;
        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(MembershipViewId observedView, CancellationToken cancellationToken = default) => ValueTask.FromResult(Current);
    }
    private sealed class RecordingRemoteInvoker(params RemoteActorInvocationResult[] results) : IRemoteActorInvoker
    {
        private readonly Queue<RemoteActorInvocationResult> _results = new(results.Length == 0 ? [RemoteActorInvocationResult.Accepted()] : results);
        public List<RemoteActorInvocation> Invocations { get; } = [];
        public ValueTask<RemoteActorInvocationResult> AskAsync(RemoteActorInvocation invocation, CancellationToken cancellationToken = default) { Invocations.Add(invocation); return ValueTask.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek()); }
        public ValueTask<RemoteActorInvocationResult> TellAsync(RemoteActorInvocation invocation, CancellationToken cancellationToken = default) { Invocations.Add(invocation); return ValueTask.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek()); }
    }
}
