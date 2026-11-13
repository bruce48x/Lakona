using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Sessions;
using Lakona.Game.Server.Observability;
using Lakona.Game.Server.Tests.Testing;
using Lakona.Rpc.Core;
using Xunit;
using Microsoft.Extensions.Time.Testing;

namespace Lakona.Game.Server.Tests.Sessions;

public sealed class ClusterClientNotificationDispatcherTests
{
    [Fact]
    public async Task Pending_byte_budget_records_backpressure()
    {
        using var metrics = new MetricReasonCollector(
            LakonaGameServerTelemetry.SessionMeterName,
            "lakona.game.notification.backpressure",
            "lakona.game.notification.reason");
        await using var dispatcher = new ClusterClientNotificationDispatcher(
            new RecordingClientFactory(new RecordingRpcClient()),
            new ClientNotificationBatchOptions { MaximumBatchBytes = 128, MaximumPendingBytesPerSession = 512 });

        var status = await dispatcher.DispatchAsync(
            CreateTarget("gateway-1", 1),
            CreateCommand("oversized", payloadBytes: 512),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Backpressure, status);
        Assert.Contains("pending_bytes", metrics.Reasons);
    }

    [Fact]
    public async Task Batch_size_groups_commands_for_one_exact_gateway_and_session_without_coalescing()
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
        Assert.Equal(ClientNotificationStatus.Failed, await second);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task Large_notification_is_sent_immediately_as_a_singleton_batch()
    {
        var client = new RecordingRpcClient();
        await using var dispatcher = new ClusterClientNotificationDispatcher(new RecordingClientFactory(client),
            new ClientNotificationBatchOptions { Window = TimeSpan.FromSeconds(1) });
        var command = CreateCommand("large", 12 * 1024 * 1024);
        Assert.Equal(ClientNotificationStatus.Accepted, await dispatcher.DispatchAsync(CreateTarget("gateway-1", 1), command, TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken));
        Assert.Same(command, Assert.Single(Assert.Single(client.Requests).Commands));
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

    [Theory]
    [InlineData("owner")]
    [InlineData("session")]
    [InlineData("node")]
    [InlineData("incarnation")]
    public async Task Batch_boundaries_preserve_exact_gateway_and_session_identity(string difference)
    {
        var clock = new FakeTimeProvider();
        var client = new RecordingRpcClient();
        await using var dispatcher = new ClusterClientNotificationDispatcher(new RecordingClientFactory(client),
            new ClientNotificationBatchOptions { Window = TimeSpan.FromSeconds(1) }, clock);
        var target = CreateTarget("gateway-1", 1);
        var otherTarget = difference switch
        {
            "node" => CreateTarget("gateway-2", 1),
            "incarnation" => CreateTarget("gateway-1", 2),
            _ => target
        };
        var first = CreateCommand("first");
        var second = CreateCommand("second");
        if (difference == "owner") second.OwnerKey = "other-owner";
        if (difference == "session") second.SessionId = "other-session";
        var firstCall = dispatcher.DispatchAsync(target, first, TestContext.Current.CancellationToken).AsTask();
        var secondCall = dispatcher.DispatchAsync(otherTarget, second, TestContext.Current.CancellationToken).AsTask();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.All(await Task.WhenAll(firstCall, secondCall).WaitAsync(TestContext.Current.CancellationToken),
            status => Assert.Equal(ClientNotificationStatus.Accepted, status));
        Assert.Equal(2, client.Requests.Count);
        Assert.All(client.Requests, request => Assert.Single(request.Commands));
    }

    [Fact]
    public async Task Inflight_bytes_remain_reserved_after_caller_cancels_and_release_on_failure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingRpcClient
        {
            BeforeReply = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); throw new IOException(); }
        };
        var command = CreateCommand("held", 1024);
        var size = checked((int)ClientNotificationSize.RetainedBytes(command));
        await using var dispatcher = new ClusterClientNotificationDispatcher(new RecordingClientFactory(client),
            new ClientNotificationBatchOptions { Window = TimeSpan.Zero,
                MaximumPendingBytesPerSession = size, MaximumPendingBytesPerProcess = size });
        using var cancel = new CancellationTokenSource();
        var first = dispatcher.DispatchAsync(CreateTarget("gateway-1", 1), command, cancel.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.Equal(ClientNotificationStatus.Backpressure, await dispatcher.DispatchAsync(
                CreateTarget("gateway-1", 1), command, TestContext.Current.CancellationToken));
        }
        finally { release.TrySetResult(); }
        // Disposal joins the actual in-flight call, even though its original caller left.
        await dispatcher.DisposeAsync();
    }

    [Fact]
    public async Task Failed_send_releases_bytes_for_the_next_batch()
    {
        var client = new RecordingRpcClient { BeforeReply = _ => throw new IOException() };
        var command = CreateCommand("held", 1024);
        var size = checked((int)ClientNotificationSize.RetainedBytes(command));
        await using var dispatcher = new ClusterClientNotificationDispatcher(new RecordingClientFactory(client),
            new ClientNotificationBatchOptions { Window = TimeSpan.Zero,
                MaximumPendingBytesPerSession = size, MaximumPendingBytesPerProcess = size });
        Assert.Equal(ClientNotificationStatus.Failed, await dispatcher.DispatchAsync(
            CreateTarget("gateway-1", 1), command, TestContext.Current.CancellationToken));
        client.BeforeReply = null;
        Assert.Equal(ClientNotificationStatus.Accepted, await dispatcher.DispatchAsync(
            CreateTarget("gateway-1", 1), command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Shutdown_joins_inflight_and_pending_session_batches()
    {
        var clock = new FakeTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingRpcClient
        {
            BeforeReply = async ct =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                finally { exited.TrySetResult(); }
            }
        };
        await using var dispatcher = new ClusterClientNotificationDispatcher(new RecordingClientFactory(client),
            new ClientNotificationBatchOptions { Window = TimeSpan.FromSeconds(1) }, clock);
        var target = CreateTarget("gateway-1", 1);
        var inflight = dispatcher.DispatchAsync(target, CreateCommand("inflight"), TestContext.Current.CancellationToken).AsTask();
        clock.Advance(TimeSpan.FromSeconds(1));
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var other = CreateCommand("pending");
        other.OwnerKey = "other-player";
        var pending = dispatcher.DispatchAsync(target, other, TestContext.Current.CancellationToken).AsTask();
        await dispatcher.DisposeAsync().AsTask().WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(exited.Task.IsCompletedSuccessfully);
        Assert.Equal(ClientNotificationStatus.Failed, await inflight);
        Assert.Equal(ClientNotificationStatus.Failed, await pending);
        Assert.Empty(client.Requests);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => dispatcher.DispatchAsync(target, other,
            TestContext.Current.CancellationToken).AsTask());
    }

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
        public Func<CancellationToken, Task>? BeforeReply { get; set; }

        public List<ClientNotificationBatchDispatchRequest> Requests { get; } = [];

        public async ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (BeforeReply is { } beforeReply) await beforeReply(ct);
            var request = Assert.IsType<ClientNotificationBatchDispatchRequest>(arg);
            lock (gate)
            {
                Requests.Add(request);
            }

            return (TResult)(object)new ClientNotificationBatchDispatchReply
            {
                Statuses = Enumerable.Repeat(
                    (int)ClientNotificationStatus.Accepted,
                    request.Commands.Count).ToArray()
            };
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
        }
    }
}
