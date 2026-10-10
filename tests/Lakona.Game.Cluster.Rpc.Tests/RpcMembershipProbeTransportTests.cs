using System.Net;
using System.Net.Sockets;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Rpc.Core;
using Lakona.Rpc.Client;
using Lakona.Rpc.Transport.Tcp;
using MemoryPack;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class RpcMembershipProbeTransportTests
{
    private static readonly NodeReference Source = new(ClusterIncarnationId.New(), new NodeId("observer"), NodeIncarnationId.New());
    private static readonly ClusterMember Target = new(
        new NodeReference(Source.Cluster, new NodeId("target"), NodeIncarnationId.New()),
        ClusterMemberState.Active, new NodeEndpoint("tcp://127.0.0.1:21001"));

    [Fact]
    public async Task Real_TCP_refusal_is_failed_directly_but_unknown_via_helper()
    {
        // Keep the port bound but not listening so no other test can claim it.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = new NodeEndpoint($"tcp://127.0.0.1:{((IPEndPoint)socket.LocalEndPoint!).Port}");
        await using var clients = new ClusterClientFactory(new ClusterRpcChannel());
        var probes = new RpcMembershipProbeTransport(clients, TimeSpan.FromSeconds(5));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(MembershipProbeStatus.Failed, await probes.ProbeAsync(Source, Target, endpoint, false,
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(MembershipProbeStatus.Unknown, await probes.ProbeAsync(Source, Target, endpoint, true,
                cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public async Task Probe_deadline_has_context_dependent_outcome(bool forward, int expected)
    {
        var clients = new ProbeClientFactory(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });
        var probes = new RpcMembershipProbeTransport(clients, TimeSpan.FromMilliseconds(50));
        Assert.Equal((MembershipProbeStatus)expected, await probes.ProbeAsync(Source, Target, Target.ClusterEndpoint,
            forward, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Refused_reconnection_after_established_connection_is_a_failed_probe()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new NodeEndpoint($"tcp://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
        await using var clients = new ClusterClientFactory(new ClusterRpcChannel());
        var connecting = clients.GetClientAsync(endpoint, ct).AsTask();
        await using var server = new TcpServerTransport(await listener.AcceptTcpClientAsync(ct));
        await ClusterRpcProtocolNegotiation.NegotiateServerAsync(server, ClusterProtocol.Identifier, ct);
        var runtime = Assert.IsType<RpcClientRuntime>(await connecting);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Disconnected += _ => disconnected.TrySetResult();
        listener.Stop();
        await server.DisposeAsync();
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var probes = new RpcMembershipProbeTransport(clients, TimeSpan.FromSeconds(5));
        Assert.Equal(MembershipProbeStatus.Failed, await probes.ProbeAsync(Source, Target, endpoint, false, cancellationToken: ct));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_probe_result()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clients = new ProbeClientFactory(async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });
        var probes = new RpcMembershipProbeTransport(clients, TimeSpan.FromSeconds(30));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = probes.ProbeAsync(Source, Target, Target.ClusterEndpoint, true, cancellationToken: caller.Token).AsTask();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task Reply_status_round_trips_and_invalid_values_are_unknown(int status)
    {
        var probes = new RpcMembershipProbeTransport(new ProbeClientFactory((_, _) =>
            new(new MembershipProbeReply { Status = (MembershipProbeStatus)status })), TimeSpan.FromSeconds(5));
        Assert.Equal(status <= 2 ? (MembershipProbeStatus)status : MembershipProbeStatus.Unknown,
            await probes.ProbeAsync(Source, Target, Target.ClusterEndpoint, true,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remote_handler_error_is_unknown_and_configuration_errors_still_throw()
    {
        var probes = new RpcMembershipProbeTransport(new ProbeClientFactory((_, _) =>
            throw new RpcException(RpcStatus.InternalError, "rejected", 1, 1, 1)), TimeSpan.FromSeconds(5));
        Assert.Equal(MembershipProbeStatus.Unknown, await probes.ProbeAsync(Source, Target, Target.ClusterEndpoint, false,
            cancellationToken: TestContext.Current.CancellationToken));
        probes = new RpcMembershipProbeTransport(new ProbeClientFactory((_, _) =>
            throw new ClusterRpcProtocolMismatchException("v6", "v5")), TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ClusterRpcProtocolMismatchException>(() => probes.ProbeAsync(Source, Target,
            Target.ClusterEndpoint, false, cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }
}

// Exercise the real request/reply codec even when the network endpoint is replaced.
internal sealed class ProbeClientFactory(
    Func<MembershipProbeRequest, CancellationToken, ValueTask<MembershipProbeReply>> handle) : IClusterClientFactory, IRpcClient
{
    public ValueTask<IRpcClient> GetClientAsync(NodeEndpoint contact, CancellationToken cancellationToken = default) => new(this);
    public ValueTask<IRpcClient> GetClientAsync(RouteLocation target, CancellationToken cancellationToken = default) => new(this);

    public async ValueTask<TResult> CallAsync<TArg, TResult>(RpcMethod<TArg, TResult> method, TArg? arg, CancellationToken ct = default)
    {
        var request = MemoryPackSerializer.Deserialize<MembershipProbeRequest>(
            MemoryPackSerializer.Serialize((MembershipProbeRequest)(object)arg!))!;
        var reply = await handle(request, ct);
        return (TResult)(object)MemoryPackSerializer.Deserialize<MembershipProbeReply>(MemoryPackSerializer.Serialize(reply))!;
    }

    public void RegisterNotificationHandler<TArg>(RpcNotificationMethod<TArg> method, Func<TArg, ValueTask> handler) =>
        throw new NotSupportedException();
}
