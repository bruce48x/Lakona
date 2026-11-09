using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed partial class HotfixUnloadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Route_Post_returns_after_acceptance_and_survives_caller_cancellation(bool remote)
    {
        var unloaded = await RunHotfixCallAndUnloadAsync(async hotfix =>
        {
            hotfix.ResetGate();
            using var caller = new CancellationTokenSource();
            var post = hotfix.PostRouteAsync(-1, caller.Token);
            try
            {
                await hotfix.WaitForGateEnteredAsync();
                await post.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                caller.Cancel();
                hotfix.ReleaseGate();
                Assert.Equal(-1, await hotfix.ReadLastTickAsync());
            }
            finally { hotfix.ReleaseGate(); }
        }, remote: remote);
        await ForceFullCollectionAsync(unloaded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Route_Post_reports_backpressure_without_waiting_for_capacity(bool remote)
    {
        var unloaded = await RunHotfixCallAndUnloadAsync(async hotfix =>
        {
            hotfix.ResetGate();
            var blocking = hotfix.CallRouteNoResultAsync(hotfix.RunTickAsync, -1);
            try
            {
                await hotfix.WaitForGateEnteredAsync();
                using var caller = new CancellationTokenSource();
                await hotfix.PostRouteAsync(7, caller.Token).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                caller.Cancel();
                var exception = await Assert.ThrowsAnyAsync<ActorCallException>(() =>
                    hotfix.PostRouteAsync(8, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                Assert.Equal(ActorCallStatus.Backpressure, exception.Status);
                hotfix.ReleaseGate();
                await blocking;
                Assert.Equal(7, await hotfix.ReadLastTickAsync());
            }
            finally { hotfix.ReleaseGate(); await blocking; }
        }, remote: remote);
        await ForceFullCollectionAsync(unloaded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Route_Post_does_not_return_behavior_errors_but_Call_does(bool remote)
    {
        var unloaded = await RunHotfixCallAndUnloadAsync(async hotfix =>
        {
            await hotfix.PostRouteAsync(-2);
            Assert.Equal(-2, await hotfix.ReadLastTickAsync());
            var exception = await Record.ExceptionAsync(() => hotfix.CallRouteNoResultAsync(hotfix.RunTickAsync, -2));
            if (remote) Assert.IsAssignableFrom<ActorCallException>(exception);
            else Assert.IsType<InvalidOperationException>(exception);
            await hotfix.PostRouteAsync(7);
            Assert.Equal(7, await hotfix.ReadLastTickAsync());
            Assert.True(await hotfix.Admission.CloseAndDrainAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }, remote: remote);
        await ForceFullCollectionAsync(unloaded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Route_Post_canceled_before_acceptance_does_not_execute(bool remote)
    {
        var unloaded = await RunHotfixCallAndUnloadAsync(async hotfix =>
        {
            using var caller = new CancellationTokenSource();
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hotfix.PostRouteAsync(7, caller.Token));
            Assert.Equal(0, await hotfix.ReadLastTickAsync());
            Assert.True(await hotfix.Admission.CloseAndDrainAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }, remote: remote);
        await ForceFullCollectionAsync(unloaded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Route_Post_to_self_can_complete_inside_the_current_turn(bool remote)
    {
        var unloaded = await RunHotfixCallAndUnloadAsync(async hotfix =>
        {
            await hotfix.InActorAsync(async () =>
                {
                    await hotfix.PostRouteAsync(7);
                    Assert.Equal(0, await hotfix.ReadLastTickAsync());
                })
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(7, await hotfix.ReadLastTickAsync());
        }, remote: remote);
        await ForceFullCollectionAsync(unloaded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Route_Post_accepted_work_is_drained_before_actor_shutdown(bool remote)
    {
        var unloaded = await RunHotfixCallAndUnloadAsync(async hotfix =>
        {
            hotfix.ResetGate();
            var post = hotfix.PostRouteAsync(-1);
            Task? stop = null;
            try
            {
                await hotfix.WaitForGateEnteredAsync();
                await post.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                await hotfix.PostRouteAsync(7);
                stop = hotfix.DestroyAsync();
                Assert.False(stop.IsCompleted);
                await Assert.ThrowsAnyAsync<ActorCallException>(() => hotfix.PostRouteAsync(8, TestContext.Current.CancellationToken));
            }
            finally
            {
                hotfix.ReleaseGate();
                if (stop is not null) await stop.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            }
            Assert.Equal(7, hotfix.LastCompletedTick);
        }, remote: remote);
        await ForceFullCollectionAsync(unloaded);
    }

    [Fact]
    public async Task Route_Post_holds_local_admission_until_execution_finishes()
    {
        var unloaded = await RunHotfixCallAndUnloadAsync(async hotfix =>
        {
            hotfix.ResetGate();
            var post = hotfix.PostRouteAsync(-1);
            Task<bool>? drain = null;
            try
            {
                await hotfix.WaitForGateEnteredAsync();
                await post.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                drain = hotfix.Admission.CloseAndDrainAsync(Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken).AsTask();
                Assert.False(drain.IsCompleted);
                await Assert.ThrowsAnyAsync<ActorCallException>(() => hotfix.PostRouteAsync(7, TestContext.Current.CancellationToken));
            }
            finally
            {
                hotfix.ReleaseGate();
                if (drain is not null)
                    Assert.True(await drain.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            }
            Assert.Equal(-1, await hotfix.ReadLastTickAsync());
        });
        await ForceFullCollectionAsync(unloaded);
    }

    private static IRemoteActorInvoker CreateRemoteInvoker(IServiceProvider provider, NodeReference owner)
    {
        var membership = new FixedMembership(new ClusterMembershipSnapshot(owner.Cluster, new MembershipViewId(1),
            [new ClusterMember(owner, ClusterMemberState.Active, new NodeEndpoint("tcp://127.0.0.1:24001"))]));
        var handler = new HotfixActorClusterHandler(
            (IActorActivationDispatcher)provider.GetRequiredService<IActorRuntime>(),
            provider.GetRequiredService<LocalActorNodeIdentity>(), membership, provider);
        return new RemoteActorInvoker(new WireActorTransport(handler, membership.Current),
            provider.GetRequiredService<IActorDirectory>(), provider.GetRequiredService<IActorDirectoryCache>());
    }

    // Only the network boundary is replaced: generated routing, directory proof,
    // wire serialization, the production RPC handler and the target mailbox are real.
    private sealed class WireActorTransport(HotfixActorClusterHandler handler, ClusterMembershipSnapshot membership)
        : IClusterActorTransport
    {
        public ValueTask<RemoteActorInvocationResult> AskAsync(RemoteActorInvocation invocation, CancellationToken token) =>
            InvokeAsync(invocation, false, token);
        public ValueTask<RemoteActorInvocationResult> TellAsync(RemoteActorInvocation invocation, CancellationToken token) =>
            InvokeAsync(invocation, true, token);

        private async ValueTask<RemoteActorInvocationResult> InvokeAsync(RemoteActorInvocation invocation, bool tell, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var member = Assert.Single(membership.Members);
            var location = new RouteLocation(ClusterActorRouteKeys.ForActor(invocation.ActorId.Value),
                member.Reference, membership.View, member.ClusterEndpoint);
            using var request = ClusterActorWireCodec.EncodeRequest(invocation, location, TimeSpan.FromMinutes(1));
            using var response = await handler.HandleActorRpcAsync(request.Memory, tell, token);
            var reply = ClusterActorWireCodec.DecodeReply(response.Memory);
            return reply.Status == RemoteActorStatus.Accepted ? RemoteActorInvocationResult.Accepted()
                : reply.Status == RemoteActorStatus.Replied ? RemoteActorInvocationResult.Replied(invocation.DeserializeReply(reply.Body))
                : RemoteActorInvocationResult.Failed(reply.Status, reply.Message ?? "Rejected", reply.RetrySafety);
        }
    }

    private sealed class FixedMembership(ClusterMembershipSnapshot snapshot) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current => snapshot;
        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(MembershipViewId after, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This fixture has a fixed membership view.");
    }
}
