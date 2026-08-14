using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class RemoteActorInvokerTests
{
    [Fact]
    public async Task AskAsync_delegates_the_typed_invocation_without_serializing_it()
    {
        var transport = new RecordingTransport
        {
            AskResult = RemoteActorInvocationResult.Replied("pong")
        };
        var invoker = new RemoteActorInvoker(transport);
        var request = new TestRequest("hello");
        var invocation = CreateInvocation<TestRequest, string>(request);

        var result = await invoker.AskAsync(
            invocation,
            TestContext.Current.CancellationToken);

        Assert.Same(invocation, transport.LastAsk);
        Assert.Same(request, transport.LastAsk!.GetRequest<TestRequest>());
        Assert.Equal(
            "pong",
            RemoteActorCall.GetReply<string>(
                result,
                invocation.ActorId,
                invocation.ActorName,
                invocation.MethodName,
                invocation.Node));
    }

    [Fact]
    public async Task TellAsync_delegates_the_typed_invocation()
    {
        var transport = new RecordingTransport();
        var invoker = new RemoteActorInvoker(transport);
        var invocation = CreateInvocation(new TestRequest("notice"));

        var result = await invoker.TellAsync(
            invocation,
            TestContext.Current.CancellationToken);

        Assert.Equal(RemoteActorStatus.Accepted, result.Status);
        Assert.Same(invocation, transport.LastTell);
    }

    [Fact]
    public async Task AskAsync_attaches_the_exact_activation_once_before_transport()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("40000000-0000-0000-0000-000000000000"));
        var owner = new NodeReference(
            cluster,
            new NodeId("node-b"),
            new NodeIncarnationId(
                Guid.Parse("40000000-0000-0000-0000-000000000002")));
        var directory = new TestActorDirectory();
        var activation = new ActorActivationId(
            Guid.Parse("40000000-0000-0000-0000-000000000003"));
        var acquired = await directory.AcquireAsync(
            ActorId.From("room/1001"),
            owner,
            activation,
            TestContext.Current.CancellationToken);
        var transport = new RecordingTransport
        {
            AskResult = RemoteActorInvocationResult.Replied("pong")
        };
        var cache = new InMemoryActorDirectoryCache();
        var invoker = new RemoteActorInvoker(transport, directory, cache);

        await invoker.AskAsync(
            CreateInvocation<TestRequest, string>(new TestRequest("hello")),
            TestContext.Current.CancellationToken);

        Assert.NotNull(transport.LastAsk);
        Assert.Equal(owner, transport.LastAsk.OwnerReference);
        Assert.Equal(activation, transport.LastAsk.ActivationId);
        Assert.True(cache.TryGetRecord(ActorId.From("room/1001"), out var cached));
        Assert.Equal(acquired.Record, cached);
    }

    [Fact]
    public async Task TellAsync_preserves_an_explicit_activation_without_directory_lookup()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("50000000-0000-0000-0000-000000000000"));
        var owner = new NodeReference(
            cluster,
            new NodeId("node-b"),
            new NodeIncarnationId(
                Guid.Parse("50000000-0000-0000-0000-000000000002")));
        var activation = new ActorActivationId(
            Guid.Parse("50000000-0000-0000-0000-000000000003"));
        var directory = new ThrowingDirectory();
        var transport = new RecordingTransport();
        var invoker = new RemoteActorInvoker(transport, directory);
        var invocation = RemoteActorInvocation.Create(
            owner.Node,
            ActorId.From("room/1001"),
            "room",
            "notify",
            methodId: 7,
            new TestRequest("hello"),
            DateTimeOffset.UtcNow.AddMinutes(1),
            ownerReference: owner,
            activationId: activation);

        await invoker.TellAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Same(invocation, transport.LastTell);
    }

    [Fact]
    public async Task Missing_or_wrong_node_activation_does_not_change_the_invocation()
    {
        var directory = new TestActorDirectory();
        await directory.RegisterAsync(
            ActorId.From("room/1001"),
            new NodeId("node-c"),
            TestContext.Current.CancellationToken);
        var transport = new RecordingTransport();
        var invoker = new RemoteActorInvoker(
            transport,
            directory,
            new InMemoryActorDirectoryCache());
        var invocation = CreateInvocation(new TestRequest("hello"));

        await invoker.TellAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Same(invocation, transport.LastTell);
        Assert.Null(transport.LastTell!.OwnerReference);
    }

    [Fact]
    public void RemoteActorInvocation_keeps_the_typed_request_until_wire_encoding()
    {
        var request = new TestRequest("original");
        var invocation = CreateInvocation(request);

        Assert.Same(request, invocation.GetRequest<TestRequest>());
        Assert.Null(typeof(RemoteActorInvocation).GetProperty("Payload"));
        Assert.Null(typeof(RemoteActorInvocation).GetProperty("Metadata"));
        Assert.Null(typeof(RemoteActorInvocation).GetProperty("CorrelationId"));
    }

    [Fact]
    public void RemoteActorOptions_only_exposes_actor_call_options()
    {
        Assert.NotNull(
            typeof(RemoteActorOptions).GetProperty(
                nameof(RemoteActorOptions.DefaultTimeout)));
        Assert.Null(typeof(RemoteActorOptions).GetProperty("ClusterName"));
        Assert.Null(typeof(RemoteActorOptions).GetProperty("EndpointName"));
    }

    [Fact]
    public void RemoteActorException_preserves_structured_failure_fields()
    {
        var exception = new RemoteActorException(
            RemoteActorStatus.RouteNotFound,
            ActorId.From("room/1001"),
            "room",
            "join",
            "The route was not found.",
            new NodeId("node-a"),
            "corr-1");

        Assert.Equal(RemoteActorStatus.RouteNotFound, exception.Status);
        Assert.Equal(ActorId.From("room/1001"), exception.ActorId);
        Assert.Equal("room", exception.ActorName);
        Assert.Equal("join", exception.MethodName);
        Assert.Equal(new NodeId("node-a"), exception.Node);
        Assert.Equal("corr-1", exception.CorrelationId);
    }

    private static RemoteActorInvocation CreateInvocation<TRequest>(TRequest request)
    {
        return RemoteActorInvocation.Create(
            new NodeId("node-b"),
            ActorId.From("room/1001"),
            "room",
            "notify",
            methodId: 7,
            request,
            DateTimeOffset.UtcNow.AddMinutes(1));
    }

    private static RemoteActorInvocation CreateInvocation<TRequest, TResult>(
        TRequest request)
    {
        return RemoteActorInvocation.Create<TRequest, TResult>(
            new NodeId("node-b"),
            ActorId.From("room/1001"),
            "room",
            "ping",
            methodId: 8,
            request,
            DateTimeOffset.UtcNow.AddMinutes(1));
    }

    private sealed record TestRequest(string Value);

    private sealed class RecordingTransport : IClusterActorTransport
    {
        public RemoteActorInvocationResult AskResult { get; init; } =
            RemoteActorInvocationResult.Accepted();

        public RemoteActorInvocation? LastAsk { get; private set; }

        public RemoteActorInvocation? LastTell { get; private set; }

        public ValueTask<RemoteActorInvocationResult> AskAsync(
            RemoteActorInvocation invocation,
            CancellationToken cancellationToken)
        {
            LastAsk = invocation;
            return ValueTask.FromResult(AskResult);
        }

        public ValueTask<RemoteActorInvocationResult> TellAsync(
            RemoteActorInvocation invocation,
            CancellationToken cancellationToken)
        {
            LastTell = invocation;
            return ValueTask.FromResult(RemoteActorInvocationResult.Accepted());
        }
    }

    private sealed class ThrowingDirectory : IActorDirectory
    {
        public ValueTask<ActorDirectoryRecord?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Directory must not be queried.");

        public ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
            ActorId actorId,
            NodeId node,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
            ActorId actorId,
            NodeId node,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
