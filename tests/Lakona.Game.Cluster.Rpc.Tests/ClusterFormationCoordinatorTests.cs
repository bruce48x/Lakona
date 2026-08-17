using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Loopback;
using System.Text.Json;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterFormationCoordinatorTests
{
    [Fact]
    public async Task Single_node_forms_one_voter_cluster_without_a_bootstrap_role()
    {
        var transport = new InMemoryFormationTransport();
        var endpoint = new NodeEndpoint("tcp://127.0.0.1:21001");
        var formation = Create("data-1", endpoint, [], transport);
        transport.Register(endpoint, formation);

        var node = await formation.FormOrJoinAsync(TestContext.Current.CancellationToken);

        var member = Assert.Single(node.Membership.Current.Members);
        Assert.Equal("data-1", member.Reference.Node.Value);
        Assert.True(member.IsVoter);
        Assert.Equal(ClusterMemberState.Recovering, member.State);
    }

    [Fact]
    public async Task Connected_inconsistent_peer_hints_converge_during_concurrent_formation()
    {
        var transport = new InMemoryFormationTransport();
        var a = new ClusterFormationPeer(
            new NodeId("a"),
            new NodeEndpoint("tcp://127.0.0.1:21001"));
        var b = new ClusterFormationPeer(
            new NodeId("b"),
            new NodeEndpoint("tcp://127.0.0.1:21002"));
        var c = new ClusterFormationPeer(
            new NodeId("c"),
            new NodeEndpoint("tcp://127.0.0.1:21003"));
        var formationA = Create(a.Node.Value, a.Endpoint, [b], transport);
        var formationB = Create(b.Node.Value, b.Endpoint, [a, c], transport);
        var formationC = Create(c.Node.Value, c.Endpoint, [b], transport);
        transport.Register(a.Endpoint, formationA);
        transport.Register(b.Endpoint, formationB);
        transport.Register(c.Endpoint, formationC);

        var nodeA = await formationA.FormOrJoinAsync(TestContext.Current.CancellationToken);
        // Membership leadership is acquired by the authority control loop, not on
        // ingress; the bootstrapped node must elect itself before peers can join.
        using var authorityCancellation = new CancellationTokenSource();
        var authorityLoop = nodeA.RunAsync(
            new NoopAuthorityListener(),
            transport,
            authorityCancellation.Token);
        await ClusterTestWait.UntilAsync(() => nodeA.IsLeader, TimeSpan.FromSeconds(2));

        var nodes = await Task.WhenAll(
            formationB.FormOrJoinAsync(TestContext.Current.CancellationToken).AsTask(),
            formationC.FormOrJoinAsync(TestContext.Current.CancellationToken).AsTask());

        var all = nodes.Append(nodeA).ToArray();
        Assert.Single(all.Select(node => node.Membership.Current.Cluster).Distinct());
        Assert.Single(all, node => node.IsLeader);
        Assert.All(
            all,
            node => Assert.Contains(
                node.Membership.Current.Members,
                member => member.Reference.Node == node.Local.Node));

        await authorityCancellation.CancelAsync();
        await authorityLoop;
    }

    [Fact]
    public async Task Slow_membership_mutation_does_not_block_consensus_ingress()
    {
        var transport = new InMemoryFormationTransport();
        var endpoint = new NodeEndpoint("tcp://127.0.0.1:21001");
        var learnerEndpoint = new NodeEndpoint("tcp://127.0.0.1:21002");
        var formation = Create("data-1", endpoint, [], transport);
        transport.Register(endpoint, formation);
        var node = await formation.FormOrJoinAsync(TestContext.Current.CancellationToken);
        using var authorityCancellation = new CancellationTokenSource();
        var authorityLoop = node.RunAsync(
            new NoopAuthorityListener(),
            transport,
            authorityCancellation.Token);
        try
        {
            await ClusterTestWait.UntilAsync(() => node.IsLeader, TimeSpan.FromSeconds(2));
            var joinResponse = MembershipWireCodec.DecodeJoinResponse(
                await formation.HandleAsync(
                    MembershipWireCodec.EncodeJoinRequest(
                        new NodeId("gateway-1"),
                        NodeIncarnationId.New(),
                        learnerEndpoint),
                    TestContext.Current.CancellationToken));
            var transferred = MembershipSnapshotCodec.Decode(joinResponse.Transfer.Payload.Span);
            transport.BlockNextRequestTo = learnerEndpoint.Address;

            var mutation = formation.HandleAsync(
                MembershipWireCodec.EncodePromoteRequest(
                    joinResponse.Local,
                    transferred.View,
                    joinResponse.Transfer.LastIncludedIndex),
                TestContext.Current.CancellationToken).AsTask();
            await transport.WaitForBlockedRequestAsync(TestContext.Current.CancellationToken);
            try
            {
                var append = formation.HandleAsync(
                    MembershipWireCodec.EncodeAppendRequest(new MembershipAppendRequest(
                        node.Local,
                        node.Local,
                        1,
                        node.Membership.Current.View,
                        1,
                        new MembershipAppendBatch(0, 0, 0, []))),
                    TestContext.Current.CancellationToken).AsTask();
                var vote = formation.HandleAsync(
                    MembershipWireCodec.EncodeVoteRequest(new MembershipVoteRequest(
                        node.Local,
                        node.Local,
                        1,
                        node.Membership.Current.View,
                        0,
                        0)),
                    TestContext.Current.CancellationToken).AsTask();
                var proof = formation.HandleAsync(
                    MembershipWireCodec.EncodeProof(new QuorumProof(
                        node.Membership.Current.Cluster,
                        term: 1,
                        node.Membership.Current.View,
                        sequence: 1,
                        validFor: TimeSpan.FromSeconds(1))),
                    TestContext.Current.CancellationToken).AsTask();

                Assert.True(
                    append.IsCompleted,
                    "Append ingress queued behind an unrelated membership mutation.");
                Assert.True(
                    vote.IsCompleted,
                    "Vote ingress queued behind an unrelated membership mutation.");
                Assert.True(
                    proof.IsCompleted,
                    "Proof ingress queued behind an unrelated membership mutation.");

                MembershipWireCodec.DecodeAppendResponse(await append);
                MembershipWireCodec.DecodeVoteResponse(await vote);
                MembershipWireCodec.DecodeProofResponse(await proof);
            }
            finally
            {
                transport.ReleaseBlockedRequest(
                    MembershipWireCodec.EncodeMembershipUnavailableResponse());
                await mutation;
            }
        }
        finally
        {
            await authorityCancellation.CancelAsync();
            await authorityLoop;
        }
    }

    [Fact]
    public async Task Unreachable_known_peer_never_shrinks_into_an_implicit_single_node_cluster()
    {
        var transport = new InMemoryFormationTransport();
        var endpoint = new NodeEndpoint("tcp://127.0.0.1:21001");
        var missing = new ClusterFormationPeer(
            new NodeId("missing"),
            new NodeEndpoint("tcp://127.0.0.1:21002"));
        var formation = Create("data-1", endpoint, [missing], transport);
        transport.Register(endpoint, formation);

        await Assert.ThrowsAsync<AggregateException>(async () =>
            await formation.FormOrJoinAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Incomplete_formation_returns_retryable_not_leader_for_membership_ingress()
    {
        var transport = new InMemoryFormationTransport();
        var endpoint = new NodeEndpoint("tcp://127.0.0.1:21001");
        var formation = Create("data-1", endpoint, [], transport);

        var member = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("gateway-1"),
            new NodeEndpoint("tcp://127.0.0.1:21002"));
        var frames = new[]
        {
            MembershipWireCodec.EncodeJoinRequest(
                new NodeId("gateway-2"), NodeIncarnationId.New(), new NodeEndpoint("tcp://127.0.0.1:21003")),
            MembershipWireCodec.EncodePromoteRequest(member.Local, member.Membership.Current.View, 1),
            MembershipWireCodec.EncodeReadyRequest(member.Membership.Current.Members[0])
        };

        foreach (var frame in frames)
        {
            var response = await formation.HandleAsync(frame, TestContext.Current.CancellationToken);
            Assert.True(MembershipWireCodec.IsNotLeaderResponse(response));
            Assert.Null(MembershipWireCodec.DecodeNotLeaderResponse(response));
        }
    }

    [Fact]
    public async Task Incomplete_formation_returns_membership_unavailable_for_control_ingress()
    {
        var transport = new InMemoryFormationTransport();
        var formation = Create("data-1", new NodeEndpoint("tcp://127.0.0.1:21001"), [], transport);
        var member = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("gateway-1"), new NodeEndpoint("tcp://127.0.0.1:21002"));
        var frames = new[]
        {
            MembershipWireCodec.EncodeAppendRequest(new MembershipAppendRequest(
                member.Local, member.Local, 1, member.Membership.Current.View, 1,
                new MembershipAppendBatch(0, 0, 0, []))),
            MembershipWireCodec.EncodeVoteRequest(new MembershipVoteRequest(
                member.Local, member.Local, 1, member.Membership.Current.View, 0, 0)),
            MembershipWireCodec.EncodeProof(new QuorumProof(
                member.Local.Cluster, 1, member.Membership.Current.View, 1, TimeSpan.FromSeconds(1))),
            MembershipWireCodec.EncodeSnapshotInstallRequest(new MembershipSnapshotInstallRequest(
                member.Local, member.Local, 1, member.Membership.Current.View, 1,
                new ClusterMembershipTransfer(0, 0, Array.Empty<byte>(), Array.Empty<byte>())))
        };

        foreach (var frame in frames)
        {
            var response = await formation.HandleAsync(frame, TestContext.Current.CancellationToken);
            Assert.True(MembershipWireCodec.IsMembershipUnavailableResponse(response));
            Assert.False(MembershipWireCodec.IsNotLeaderResponse(response));
        }
    }

    [Fact]
    public async Task Membership_frame_binder_returns_ok_with_unavailable_outcome_during_formation()
    {
        var formation = Create("data-1", new NodeEndpoint("tcp://127.0.0.1:21001"), [], new InMemoryFormationTransport());
        var node = ClusterMembershipNode.BootstrapNewCluster(new NodeId("gateway-1"), new NodeEndpoint("tcp://127.0.0.1:21002"));
        var append = MembershipWireCodec.EncodeAppendRequest(new MembershipAppendRequest(
            node.Local, node.Local, 1, node.Membership.Current.View, 1,
            new MembershipAppendBatch(0, 0, 0, [])));
        var registry = new RpcServiceRegistry();
        ClusterMembershipFrameBinder.Bind(registry, new FormationFrameHandler(formation));
        Assert.True(registry.TryGetHandler(ClusterProtocol.ServiceId, ClusterProtocol.MembershipFrameMethodId, out var handler));
        await using var session = new RpcSession(new TestTransport(), new TestSerializer());
        using var payload = session.Serializer.SerializeFrame(new ClusterMembershipFrameRequest { Payload = append.Payload.ToArray() });
        using var responseFrame = await handler!(session, new RpcRequestFrame(1, ClusterProtocol.ServiceId, ClusterProtocol.MembershipFrameMethodId, payload), TestContext.Current.CancellationToken);
        using var response = RpcEnvelopeCodec.DecodeResponse(responseFrame);
        Assert.Equal(RpcStatus.Ok, response.Status);
        var reply = session.Serializer.Deserialize<ClusterMembershipFrameReply>(response.Payload.Memory);
        Assert.True(MembershipWireCodec.IsMembershipUnavailableResponse(new ClusterMembershipTransportFrame(reply.Payload)));
    }

    [Fact]
    public async Task Membership_frame_binder_classifies_invalid_typed_requests_as_bad_request_without_invoking_handler()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new MemoryPackRpcSerializer();
        var registry = new RpcServiceRegistry();
        var calls = 0;
        ClusterMembershipFrameBinder.Bind(registry, new DelegateFrameHandler((_, _) =>
        {
            calls++;
            return new ValueTask<ClusterMembershipTransportFrame>(new ClusterMembershipTransportFrame(new byte[] { 9 }));
        }));
        await using var server = new RpcSession(serverTransport, serializer, registry, "membership-invalid-payload");
        await server.StartAsync(TestContext.Current.CancellationToken);
        await clientTransport.ConnectAsync(TestContext.Current.CancellationToken);

        var invalidPayloads = new[]
        {
            Array.Empty<byte>(),
            new byte[] { 1 },
            new byte[] { 0xff, 0xff, 0xff },
            Serialize(serializer, (ClusterMembershipFrameRequest?)null),
            Serialize(serializer, new ClusterMembershipFrameRequest { Payload = null! })
        };
        uint requestId = 1;
        foreach (var payload in invalidPayloads)
        {
            using var request = RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
            {
                RequestId = requestId++,
                ServiceId = ClusterProtocol.ServiceId,
                MethodId = ClusterProtocol.MembershipFrameMethodId,
                Payload = payload
            });
            await clientTransport.SendFrameAsync(request.Memory, TestContext.Current.CancellationToken);
            using var responseFrame = await clientTransport.ReceiveFrameAsync(TestContext.Current.CancellationToken);
            using var response = RpcEnvelopeCodec.DecodeResponse(responseFrame);
            Assert.Equal(RpcStatus.BadRequest, response.Status);
            Assert.Equal("RPC request payload is invalid.", response.ErrorMessage);
        }

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Membership_frame_binder_preserves_valid_outer_dto_and_keeps_handler_errors_distinct()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new MemoryPackRpcSerializer();
        var registry = new RpcServiceRegistry();
        byte[]? observed = null;
        var throwFromHandler = false;
        ClusterMembershipFrameBinder.Bind(registry, new DelegateFrameHandler((request, _) =>
        {
            if (throwFromHandler) throw new InvalidOperationException("Handler failure.");
            observed = request.Payload.ToArray();
            return new ValueTask<ClusterMembershipTransportFrame>(new ClusterMembershipTransportFrame(new byte[] { 9 }));
        }));
        await using var server = new RpcSession(serverTransport, serializer, registry, "membership-valid-payload");
        await server.StartAsync(TestContext.Current.CancellationToken);
        await clientTransport.ConnectAsync(TestContext.Current.CancellationToken);

        await SendMembershipRequestAsync(clientTransport, serializer, 1, new ClusterMembershipFrameRequest { Payload = [4, 5, 6] });
        using (var responseFrame = await clientTransport.ReceiveFrameAsync(TestContext.Current.CancellationToken))
        using (var response = RpcEnvelopeCodec.DecodeResponse(responseFrame))
        {
            Assert.Equal(RpcStatus.Ok, response.Status);
            Assert.Equal(new byte[] { 9 }, serializer.Deserialize<ClusterMembershipFrameReply>(response.Payload.Memory).Payload);
        }
        Assert.Equal(new byte[] { 4, 5, 6 }, observed);

        throwFromHandler = true;
        await SendMembershipRequestAsync(clientTransport, serializer, 2, new ClusterMembershipFrameRequest { Payload = [7] });
        using var failedResponseFrame = await clientTransport.ReceiveFrameAsync(TestContext.Current.CancellationToken);
        using var failedResponse = RpcEnvelopeCodec.DecodeResponse(failedResponseFrame);
        Assert.Equal(RpcStatus.HandlerError, failedResponse.Status);
    }

    private static byte[] Serialize<T>(IRpcSerializer serializer, T value)
    {
        using var frame = serializer.SerializeFrame(value);
        return frame.ToArray();
    }

    private static async Task SendMembershipRequestAsync(ITransport transport, IRpcSerializer serializer, uint requestId, ClusterMembershipFrameRequest request)
    {
        using var frame = RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = requestId,
            ServiceId = ClusterProtocol.ServiceId,
            MethodId = ClusterProtocol.MembershipFrameMethodId,
            Payload = Serialize(serializer, request)
        });
        await transport.SendFrameAsync(frame.Memory, TestContext.Current.CancellationToken);
    }

    private sealed class NoopAuthorityListener : IClusterAuthorityListener
    {
        public ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken) => default;

        public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken) => default;

        public void OnTransientFailure(Exception exception)
        {
        }
    }

    private static ClusterFormationCoordinator Create(
        string node,
        NodeEndpoint endpoint,
        IReadOnlyList<ClusterFormationPeer> peers,
        IClusterMembershipTransport transport)
    {
        return new ClusterFormationCoordinator(
            new NodeId(node),
            endpoint,
            peers,
            transport,
            new ClusterMembershipNodeOptions
            {
                MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(2),
                JoinRetryWindow = TimeSpan.FromMilliseconds(100)
            });
    }

    private sealed class InMemoryFormationTransport : IClusterMembershipTransport
    {
        private readonly Dictionary<string, ClusterFormationCoordinator> nodes =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly TaskCompletionSource blockedRequestStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<ClusterMembershipTransportFrame>? blockedRequest;

        public string? BlockNextRequestTo { get; set; }

        public void Register(NodeEndpoint endpoint, ClusterFormationCoordinator formation)
        {
            nodes.Add(endpoint.Address, formation);
        }

        public ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!nodes.TryGetValue(endpoint.Address, out var target))
            {
                if (!string.Equals(
                        BlockNextRequestTo,
                        endpoint.Address,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Peer is unreachable.");
                }

                BlockNextRequestTo = null;
                blockedRequest = new TaskCompletionSource<ClusterMembershipTransportFrame>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                blockedRequestStarted.TrySetResult();
                return new ValueTask<ClusterMembershipTransportFrame>(blockedRequest.Task);
            }

            return target.HandleAsync(request, cancellationToken);
        }

        public Task WaitForBlockedRequestAsync(CancellationToken cancellationToken) =>
            blockedRequestStarted.Task.WaitAsync(cancellationToken);

        public void ReleaseBlockedRequest(ClusterMembershipTransportFrame response)
        {
            if (blockedRequest is null || !blockedRequest.TrySetResult(response))
            {
                throw new InvalidOperationException("No formation transport request is blocked.");
            }
        }
    }

    private sealed class TestSerializer : IRpcSerializer
    {
        public void Serialize<T>(System.Buffers.IBufferWriter<byte> destination, T value)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            bytes.CopyTo(destination.GetSpan(bytes.Length)); destination.Advance(bytes.Length);
        }
        public T Deserialize<T>(ReadOnlySpan<byte> payload) => JsonSerializer.Deserialize<T>(payload)!;
        public T Deserialize<T>(ReadOnlyMemory<byte> payload) => Deserialize<T>(payload.Span);
    }

    private sealed class TestTransport : ITransport
    {
        public bool IsConnected => true;
        public ValueTask ConnectAsync(CancellationToken cancellationToken) => default;
        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken) => default;
        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken cancellationToken) => new(TransportFrame.Empty);
        public ValueTask DisposeAsync() => default;
    }

    private sealed class FormationFrameHandler(ClusterFormationCoordinator formation) : IClusterMembershipFrameHandler
    {
        public ValueTask<ClusterMembershipTransportFrame> HandleAsync(ClusterMembershipTransportFrame request, CancellationToken cancellationToken = default) =>
            formation.HandleAsync(request, cancellationToken);
    }

    private sealed class DelegateFrameHandler(
        Func<ClusterMembershipTransportFrame, CancellationToken, ValueTask<ClusterMembershipTransportFrame>> handle)
        : IClusterMembershipFrameHandler
    {
        public ValueTask<ClusterMembershipTransportFrame> HandleAsync(
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default) => handle(request, cancellationToken);
    }
}
