using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Server.Tests.Sessions;

public sealed class ClusterClientNotificationDispatcherTests
{
    [Fact]
    public async Task Batch_size_groups_commands_for_one_exact_gateway_without_coalescing()
    {
        var client = new RecordingRpcClient();
        var factory = new RecordingClientFactory(client);
        await using var dispatcher = new ClusterClientNotificationDispatcher(
            factory,
            new ClientNotificationBatchOptions
            {
                Window = TimeSpan.FromSeconds(1),
                MaximumBatchSize = 2
            });
        var target = CreateTarget("gateway-1", 1);

        var first = dispatcher.DispatchAsync(target, CreateCommand("first"),
            TestContext.Current.CancellationToken).AsTask();
        var second = dispatcher.DispatchAsync(target, CreateCommand("second"),
            TestContext.Current.CancellationToken).AsTask();

        Assert.Equal(ClientNotificationStatus.Accepted, await first);
        Assert.Equal(ClientNotificationStatus.Accepted, await second);
        var request = Assert.Single(client.Requests);
        Assert.Equal(["first", "second"], request.Commands.Select(static command => command.MethodName));
    }

    [Fact]
    public async Task Byte_budget_flushes_a_batch_before_the_window_expires()
    {
        var client = new RecordingRpcClient();
        var factory = new RecordingClientFactory(client);
        var dispatcher = new ClusterClientNotificationDispatcher(
            factory,
            new ClientNotificationBatchOptions
            {
                Window = TimeSpan.FromSeconds(1),
                MaximumBatchSize = 16,
                MaximumBatchBytes = 200
            });
        var target = CreateTarget("gateway-1", 1);

        var first = dispatcher.DispatchAsync(target, CreateCommand("first", 64),
            TestContext.Current.CancellationToken).AsTask();
        var second = dispatcher.DispatchAsync(target, CreateCommand("second", 64),
            TestContext.Current.CancellationToken).AsTask();

        Assert.Equal(ClientNotificationStatus.Accepted,
            await first.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken));
        Assert.Single(client.Requests);
        Assert.Single(client.Requests[0].Commands);

        await dispatcher.DisposeAsync();
        Assert.Equal(ClientNotificationStatus.Accepted, await second);
        Assert.Equal(2, client.Requests.Count);
        Assert.Equal("second", Assert.Single(client.Requests[1].Commands).MethodName);
    }

    private static ClientNotificationCommand CreateCommand(string method, int payloadBytes = 0) => new()
    {
        OwnerKey = "player-1",
        SessionId = "session-1",
        CallbackContractType = "Callback",
        MethodName = method,
        ServiceId = 1,
        MethodId = 2,
        Payload = new byte[payloadBytes]
    };

    private static RouteLocation CreateTarget(string node, int incarnation) => new(
        new RouteKey("client-session:test"),
        new NodeReference(
            new ClusterIncarnationId(Guid.Parse("30000000-0000-0000-0000-000000000000")),
            new NodeId(node),
            new NodeIncarnationId(Guid.Parse($"{incarnation:D8}-0000-0000-0000-000000000000"))),
        new MembershipViewId(1),
        new NodeEndpoint("tcp://127.0.0.1:23001"));

    private sealed class RecordingClientFactory(IRpcClient client) : IClusterClientFactory
    {
        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(client);
    }

    private sealed class RecordingRpcClient : IRpcClient
    {
        private readonly object gate = new();

        public List<ClientNotificationBatchDispatchRequest> Requests { get; } = [];

        public ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct = default)
        {
            var request = Assert.IsType<ClientNotificationBatchDispatchRequest>(arg);
            lock (gate)
            {
                Requests.Add(request);
            }

            return ValueTask.FromResult((TResult)(object)new ClientNotificationBatchDispatchReply
            {
                Statuses = Enumerable.Repeat(
                    (int)ClientNotificationStatus.Accepted,
                    request.Commands.Count).ToArray()
            });
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
        }
    }
}
