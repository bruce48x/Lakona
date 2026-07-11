using System.Text.Json;
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
        var services = new ServiceCollection();
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
        string localNode = "node-a")
    {
        var snapshot = new HotfixRuntimeSnapshot(
            new NoopHotfixInvoker(),
            new EmptyServiceProvider(),
            [declaration],
            "build-1");
        return new StartupActorInvoker(
            new StubHotfixAccessor(snapshot),
            new StubNodeDirectory(nodes),
            new LocalActorNodeIdentity(new NodeId(localNode)),
            remote ?? new RecordingRemoteInvoker(),
            new JsonRemoteSerializer(),
            new ClusterNodeSenderOptions { ClusterName = "local" },
            new RemoteActorOptions());
    }

    private static NodeRecord Node(string id, long epoch, string zone = "zone") => new(
        "local", new NodeId(id), epoch,
        new Dictionary<string, NodeEndpoint> { ["cluster"] = new($"tcp://{id}:21000") },
        [], [new StartupActorDescriptor("test", Policy(), "build-1", new Dictionary<string, string> { ["zone"] = zone })],
        null, NodeState.Ready, DateTimeOffset.UtcNow.AddMinutes(1), DateTimeOffset.UtcNow);

    private static string Policy() => $"startup:v1:{typeof(TestActor).FullName}:{typeof(string).FullName}";

    private sealed record Request(string Value);
    [ActorName("test")]
    private sealed class TestActor : IActor { }
    private sealed class EmptyServiceProvider : IServiceProvider { public object? GetService(Type serviceType) => null; }
    private sealed class NoopHotfixInvoker : IHotfixServiceInvoker
    {
        public ValueTask InvokeAsync<TContract, TArg>(int methodId, TArg arg, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(int methodId, TArg arg, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask InvokeAsync<TContract, TArg>(string methodName, TArg arg, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(string methodName, TArg arg, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
    private sealed class RecordingRemoteInvoker(params RemoteActorInvocationResult[] results) : IRemoteActorInvoker
    {
        private readonly Queue<RemoteActorInvocationResult> _results = new(results.Length == 0 ? [RemoteActorInvocationResult.Accepted()] : results);
        public List<RemoteActorInvocation> Invocations { get; } = [];
        public ValueTask<RemoteActorInvocationResult> AskAsync(RemoteActorInvocation invocation, CancellationToken cancellationToken = default) { Invocations.Add(invocation); return ValueTask.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek()); }
        public ValueTask<RemoteActorInvocationResult> TellAsync(RemoteActorInvocation invocation, CancellationToken cancellationToken = default) { Invocations.Add(invocation); return ValueTask.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek()); }
    }
    private sealed class JsonRemoteSerializer : IRemoteActorSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public T Deserialize<T>(ReadOnlyMemory<byte> payload) => JsonSerializer.Deserialize<T>(payload.Span)!;
        public ReadOnlyMemory<byte> Serialize(object? value, Type type) => JsonSerializer.SerializeToUtf8Bytes(value, type);
        public object? Deserialize(ReadOnlyMemory<byte> payload, Type type) => JsonSerializer.Deserialize(payload.Span, type);
    }
}
