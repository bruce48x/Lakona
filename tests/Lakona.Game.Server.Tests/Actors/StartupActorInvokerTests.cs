using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class StartupActorInvokerTests
{
    [Fact]
    public void Startup_affinity_catalog_fence_rejects_a_delayed_old_owner_retain()
    {
        var shard = new StartupActorAffinityDirectory.AffinityShard();
        var oldOwner = new NodeReference(
            new ClusterIncarnationId(Guid.Parse("51000000-0000-0000-0000-000000000000")),
            new NodeId("node-a"),
            new NodeIncarnationId(Guid.Parse("51000001-0000-0000-0000-000000000000")));
        var newOwner = new NodeReference(oldOwner.Cluster, new NodeId("node-b"),
            new NodeIncarnationId(Guid.Parse("51000002-0000-0000-0000-000000000000")));
        var id = ActorId.From("@startup-affinity/test/key");

        shard.FencedBind(oldOwner, new MembershipViewId(1), id, oldOwner, 1);
        var snapshot = shard.FenceAndSnapshot(newOwner, new MembershipViewId(2));

        Assert.Single(snapshot);
        Assert.Throws<ActorDirectoryUnavailableException>(() =>
            shard.FencedBind(oldOwner, new MembershipViewId(1), id, oldOwner, 2));
    }

    [Fact]
    public void Startup_affinity_pending_generation_can_only_complete_for_the_same_target()
    {
        var shard = new StartupActorAffinityDirectory.AffinityShard();
        var cluster = new ClusterIncarnationId(Guid.Parse("52000000-0000-0000-0000-000000000000"));
        var first = new NodeReference(cluster, new NodeId("node-a"),
            new NodeIncarnationId(Guid.Parse("52000001-0000-0000-0000-000000000000")));
        var other = new NodeReference(cluster, new NodeId("node-b"),
            new NodeIncarnationId(Guid.Parse("52000002-0000-0000-0000-000000000000")));
        var id = ActorId.From("@startup-affinity/test/key");

        var pending = shard.Bind(id, first, 1, pending: true);
        Assert.True(pending.Pending);
        Assert.Throws<ActorDirectoryUnavailableException>(() => shard.Bind(id, other, 1, pending: true));
        var bound = shard.Bind(id, first, 1, pending: false);

        Assert.False(bound.Pending);
        Assert.Equal(first, bound.Target);
    }

    [Fact]
    public void Startup_affinity_removed_pending_target_advances_to_a_new_generation()
    {
        var shard = new StartupActorAffinityDirectory.AffinityShard();
        var cluster = new ClusterIncarnationId(Guid.Parse("52500000-0000-0000-0000-000000000000"));
        var removed = new NodeReference(cluster, new NodeId("node-a"),
            new NodeIncarnationId(Guid.Parse("52500001-0000-0000-0000-000000000000")));
        var replacement = new NodeReference(cluster, new NodeId("node-b"),
            new NodeIncarnationId(Guid.Parse("52500002-0000-0000-0000-000000000000")));
        var id = ActorId.From("@startup-affinity/test/removed-pending");
        shard.Bind(id, removed, 4, pending: true);

        var pending = shard.ReplacePendingTarget(id, removed, replacement);
        var bound = shard.Bind(id, replacement, pending.Generation, pending: false);

        Assert.Equal(5, pending.Generation);
        Assert.True(pending.Pending);
        Assert.Equal(replacement, pending.Target);
        Assert.False(bound.Pending);
        Assert.Equal(5, bound.Generation);
    }

    [Fact]
    public async Task Pending_affinity_does_not_advance_while_exact_target_remains_in_membership()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("52600000-0000-0000-0000-000000000000"));
        var local = AffinityReference(cluster, "node-owner", 1);
        var firstTarget = AffinityReference(cluster, "node-first", 2);
        var replacement = AffinityReference(cluster, "node-replacement", 3);
        var initial = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            [
                AffinityMember(local, ClusterMemberState.Active),
                AffinityMember(firstTarget, ClusterMemberState.Active),
                AffinityMember(replacement, ClusterMemberState.Active)
            ]);
        var id = FindAffinityIdOwnedBy(initial, local);
        var membership = new ImmediateTestClusterMembership(initial);
        var client = new RetainFailingRpcClient();
        var directory = new StartupActorAffinityDirectory(
            membership,
            new FixedClusterClientFactory(client),
            new LocalActorNodeIdentity(local.Node));

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await directory.BindAsync(
                id,
                firstTarget,
                "test",
                Policy(),
                "build-1",
                TestContext.Current.CancellationToken));
        var pending = await directory.LookupAsync(id, TestContext.Current.CancellationToken);
        Assert.NotNull(pending);
        Assert.True(pending.Pending);

        membership.Current = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            [
                AffinityMember(local, ClusterMemberState.Active),
                AffinityMember(firstTarget, ClusterMemberState.Joining),
                AffinityMember(replacement, ClusterMemberState.Active)
            ]);

        await Assert.ThrowsAsync<StartupActorUnavailableException>(async () =>
            await directory.BindAsync(
                id,
                replacement,
                "test",
                Policy(),
                "build-1",
                TestContext.Current.CancellationToken));

        var retained = await directory.LookupAsync(id, TestContext.Current.CancellationToken);
        Assert.NotNull(retained);
        Assert.True(retained.Pending);
        Assert.Equal(firstTarget, retained.Target);
        Assert.Equal(pending.Generation, retained.Generation);
        Assert.Equal(1, client.RetainCalls);
    }

    [Fact]
    public void Startup_affinity_owner_handoff_is_idempotent_across_later_descriptor_views()
    {
        var shard = new StartupActorAffinityDirectory.AffinityShard();
        var cluster = new ClusterIncarnationId(Guid.Parse("53000000-0000-0000-0000-000000000000"));
        var oldOwner = new NodeReference(cluster, new NodeId("node-a"),
            new NodeIncarnationId(Guid.Parse("53000001-0000-0000-0000-000000000000")));
        var newOwner = new NodeReference(cluster, new NodeId("node-b"),
            new NodeIncarnationId(Guid.Parse("53000002-0000-0000-0000-000000000000")));
        var id = ActorId.From("@startup-affinity/test/pending");
        Assert.True(shard.TryAdvance(oldOwner, new MembershipViewId(1)) is false);
        shard.Activate(oldOwner, new MembershipViewId(1),
            [new StartupActorAffinityRecord(id, oldOwner, 4, Pending: true)]);

        var first = shard.HandoffSnapshot(newOwner, new MembershipViewId(3));
        var retry = shard.HandoffSnapshot(newOwner, new MembershipViewId(4));

        Assert.Single(first);
        Assert.True(first[0].Pending);
        Assert.Equal(first, retry);
        Assert.Throws<ActorDirectoryUnavailableException>(() =>
            shard.Bind(ActorId.From("@startup-affinity/test/late"), oldOwner, 1));
    }

    [Fact]
    public async Task Production_registrations_resolve_startup_actor_invoker()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(static context => context.Candidates[0]);
        var snapshot = new HotfixRuntimeSnapshot(new NoopHotfixInvoker(), new EmptyServiceProvider(), [declaration], "build-1");
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddLakonaGameServerActors();
        services.UseReadySingleNodeMembership();
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
            Member("node-b", 2, "blue"),
            Member("node-a", 1, "green")]);

        var result = await invoker.CallAsync<TestActor, string, Request, string>(
            "tenant", "test", "Ping", 1, new Request("hello"),
            static (id, request, _) => new ValueTask<string>($"{id.Value}:{request.Value}"),
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
        var cluster = new ClusterIncarnationId(
            Guid.Parse("50000000-0000-0000-0000-000000000000"));
        var membership = new ImmediateTestClusterMembership(CreateMembership(cluster, 1, "node-a", "node-b"));
        var directory = new TestActorDirectory();
        var affinity = new StartupActorAffinityDirectory();
        var invoker = new StartupActorInvoker(
            new StubHotfixAccessor(snapshot),
            new ClusterCapabilityIndex(membership),
            new LocalActorNodeIdentity("node-b"),
            new RecordingRemoteInvoker(),
            new RemoteActorOptions(),
            logger: null,
            affinityDirectory: affinity,
            actorDirectory: directory,
            membership: membership);

        var first = await invoker.CallAsync<TestActor, string, Request, string>(
            "tenant", "test", "Ping", 1, new Request("one"),
            static (id, _, _) => new ValueTask<string>(id.Value),
            TestContext.Current.CancellationToken);

        membership.Current = CreateMembership(cluster, 2, "node-a", "node-b", "node-c");
        var second = await invoker.CallAsync<TestActor, string, Request, string>(
            "tenant", "test", "Ping", 1, new Request("two"),
            static (id, _, _) => new ValueTask<string>(id.Value),
            TestContext.Current.CancellationToken);

        Assert.Equal("test/@startup/node-b", first);
        Assert.Equal(first, second);
        Assert.Equal(1, selectorCalls);
    }

    [Fact]
    public async Task Removed_startup_replica_is_reselected_with_a_higher_affinity_generation()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(static context => context.Candidates[0]);
        var hotfix = new HotfixRuntimeSnapshot(new NoopHotfixInvoker(), new EmptyServiceProvider(), [declaration], "build-1");
        var cluster = new ClusterIncarnationId(Guid.Parse("50000000-0000-0000-0000-000000000000"));
        var membership = new ImmediateTestClusterMembership(CreateMembership(cluster, 1, "node-a", "node-b"));
        var affinity = new StartupActorAffinityDirectory();
        var directory = new TestActorDirectory();
        var first = new StartupActorInvoker(
            new StubHotfixAccessor(hotfix), new ClusterCapabilityIndex(membership),
            new LocalActorNodeIdentity("node-a"), new RecordingRemoteInvoker(), new RemoteActorOptions(),
            affinityDirectory: affinity, actorDirectory: directory, membership: membership);

        var firstResult = await first.CallAsync<TestActor, string, Request, string>(
            "tenant", "test", "Ping", 1, new("first"),
            static (id, _, _) => new(id.Value), TestContext.Current.CancellationToken);
        membership.Current = CreateMembership(cluster, 2, "node-b");
        var remote = new RecordingRemoteInvoker();
        var second = new StartupActorInvoker(
            new StubHotfixAccessor(hotfix), new ClusterCapabilityIndex(membership),
            new LocalActorNodeIdentity("producer"), remote, new RemoteActorOptions(),
            affinityDirectory: affinity, actorDirectory: directory, membership: membership);

        await second.PostAsync<TestActor, string, Request>(
            "tenant", "test", "Ping", 1, new("second"),
            static (_, _, _) => new(ActorTellResult.Accepted), TestContext.Current.CancellationToken);

        Assert.Equal("test/@startup/node-a", firstResult);
        Assert.Equal(new NodeId("node-b"), Assert.Single(remote.Invocations).Node);
    }

    [Fact]
    public async Task Sticky_affinity_fails_closed_when_its_capability_is_withdrawn_after_candidate_discovery()
    {
        var selectorCalls = 0;
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(context =>
        {
            selectorCalls++;
            return context.Candidates[0];
        });
        var snapshot = new HotfixRuntimeSnapshot(
            new NoopHotfixInvoker(),
            new EmptyServiceProvider(),
            [declaration],
            "build-1");
        var cluster = new ClusterIncarnationId(Guid.Parse("50000000-0000-0000-0000-000000000000"));
        var initial = CreateMembership(cluster, 1, "node-a");
        var directory = new TestActorDirectory();
        var affinity = new StartupActorAffinityDirectory();
        var firstMembership = new ImmediateTestClusterMembership(initial);
        var first = new StartupActorInvoker(
            new StubHotfixAccessor(snapshot),
            new ClusterCapabilityIndex(firstMembership),
            new LocalActorNodeIdentity("node-a"),
            new RecordingRemoteInvoker(),
            new RemoteActorOptions(),
            affinityDirectory: affinity,
            actorDirectory: directory,
            membership: firstMembership);

        await first.PostAsync<TestActor, string, Request>(
            "tenant", "test", "Ping", 1, new Request("first"),
            static (_, _, _) => new ValueTask<ActorTellResult>(ActorTellResult.Accepted),
            TestContext.Current.CancellationToken);

        var withdrawn = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            [new ClusterMember(
                initial.Members.Single().Reference,
                ClusterMemberState.Active,
                new NodeEndpoint("tcp://node-a:21000"),
                labels: null,
                actorHosts: [],
                startupActors: [])]);
        var sequencedMembership = new SequencedMembership(initial, withdrawn, withdrawn);
        var remote = new RecordingRemoteInvoker();
        var second = new StartupActorInvoker(
            new StubHotfixAccessor(snapshot),
            new ClusterCapabilityIndex(sequencedMembership),
            new LocalActorNodeIdentity("node-local"),
            remote,
            new RemoteActorOptions(),
            affinityDirectory: affinity,
            actorDirectory: directory,
            membership: sequencedMembership);

        await Assert.ThrowsAsync<StartupActorUnavailableException>(async () => await second.PostAsync<TestActor, string, Request>(
            "tenant", "test", "Ping", 1, new Request("second"),
            static (_, _, _) => new ValueTask<ActorTellResult>(ActorTellResult.Accepted),
            TestContext.Current.CancellationToken));

        Assert.Equal(2, selectorCalls);
        Assert.Empty(remote.Invocations);
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
        var membership = new ImmediateTestClusterMembership(CreateMembership(cluster, 1, "node-a", "node-b"));
        var directory = new TestActorDirectory();
        var affinity = new StartupActorAffinityDirectory();
        var remote = new RecordingRemoteInvoker();
        var invoker = new StartupActorInvoker(
            new StubHotfixAccessor(snapshot),
            new ClusterCapabilityIndex(membership),
            new LocalActorNodeIdentity("node-local"),
            remote,
            new RemoteActorOptions(),
            logger: null,
            affinityDirectory: affinity,
            actorDirectory: directory,
            membership: membership);

        await invoker.PostAsync<TestActor, string, Request>(
            "tenant",
            "test",
            "Ping",
            1,
            new Request("hello"),
            static (_, _, _) => new ValueTask<ActorTellResult>(ActorTellResult.Accepted),
            TestContext.Current.CancellationToken);

        var invocation = Assert.Single(remote.Invocations);
        var expectedOwner = membership.Current.Members.Single(
            static member => member.Reference.Node == new NodeId("node-a")).Reference;
        Assert.Equal(ActorId.From("test/@startup/node-a"), invocation.ActorId);
        Assert.Equal(expectedOwner, invocation.OwnerReference);
        Assert.NotNull(invocation.ActivationId);
        var replicaActivation = await directory.ResolveAsync(
            invocation.ActorId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(replicaActivation);
        Assert.Equal(invocation.OwnerReference, replicaActivation.OwnerReference);
        Assert.Equal(invocation.ActivationId, replicaActivation.ActivationId);
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
        var invoker = CreateInvoker(declaration, [Member("node-a", 1), Member("node-b", 2)], remote, "node-local");

        await Assert.ThrowsAsync<StartupActorUnavailableException>(async () => await invoker.CallAsync<TestActor, string, Request>(
            "tenant", "test", "Ping", 1, new Request("hello"),
            static (_, _, _) => default,
            TestContext.Current.CancellationToken));

        Assert.Equal(2, remote.Invocations.Count);
        Assert.Equal([["node-a", "node-b"], ["node-b"]], selections);
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
        var invoker = CreateInvoker(declaration, [Member("node-a", 1), Member("node-b", 2)], remote, "node-local");

        await Assert.ThrowsAsync<ActorCallTimeoutException>(async () => await invoker.CallAsync<TestActor, string, Request>(
            "tenant", "test", "Ping", 1, new Request("hello"),
            static (_, _, _) => default,
            TestContext.Current.CancellationToken));

        Assert.Equal(1, selectorCalls);
        Assert.Single(remote.Invocations);
    }

    [Fact]
    public async Task CallAsync_rejects_outsider_selector_result()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(
            static _ => new StartupActorCandidate("outsider"));
        var invoker = CreateInvoker(declaration, [Member("node-a", 1)]);

        await Assert.ThrowsAsync<StartupActorSelectionException>(async () =>
            await invoker.CallAsync<TestActor, string, Request>(
                "tenant", "test", "Ping", 1, new Request("hello"),
                static (_, _, _) => default,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CallAsync_wraps_selector_failure()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(
            static _ => throw new InvalidOperationException("selector bug"));
        var invoker = CreateInvoker(declaration, [Member("node-a", 1)]);

        var exception = await Assert.ThrowsAsync<StartupActorSelectionException>(async () =>
            await invoker.CallAsync<TestActor, string, Request>(
                "tenant", "test", "Ping", 1, new Request("hello"),
                static (_, _, _) => default,
                TestContext.Current.CancellationToken));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task CallAsync_rejects_wrong_key_type()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(static context => context.Candidates[0]);
        var invoker = CreateInvoker(declaration, [Member("node-a", 1)]);

        await Assert.ThrowsAsync<StartupActorSelectionException>(async () =>
            await invoker.CallAsync<TestActor, int, Request>(
                42, "test", "Ping", 1, new Request("hello"),
                static (_, _, _) => default,
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
                static (_, _, _) => default,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CallAsync_matches_the_default_build_tag_when_source_version_is_missing()
    {
        var declaration = ActorStartupDeclaration.Create<TestActor, string>(static context => context.Candidates[0]);
        var invoker = CreateInvoker(declaration, [Member("node-a", 1, hotfixVersion: "hotfix")], sourceVersion: null);

        await invoker.CallAsync<TestActor, string, Request>(
            "tenant", "test", "Ping", 1, new Request("hello"),
            static (_, _, _) => default,
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
        var invoker = CreateInvoker(declaration, [Member("node-a", 1), Member("node-b", 2)]);
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
        IReadOnlyList<ClusterMember> members,
        RecordingRemoteInvoker? remote = null,
        string localNode = "node-a",
        string? sourceVersion = "build-1")
    {
        var snapshot = new HotfixRuntimeSnapshot(
            new NoopHotfixInvoker(),
            new EmptyServiceProvider(),
            [declaration],
            sourceVersion);
        var membership = new ImmediateTestClusterMembership(new ClusterMembershipSnapshot(
            new ClusterIncarnationId(Guid.Parse("50000000-0000-0000-0000-000000000000")),
            new MembershipViewId(1),
            members));
        return new StartupActorInvoker(
            new StubHotfixAccessor(snapshot),
            new ClusterCapabilityIndex(membership),
            new LocalActorNodeIdentity(new NodeId(localNode)),
            remote ?? new RecordingRemoteInvoker(),
            new RemoteActorOptions(),
            membership: membership);
    }

    private static ClusterMember Member(string id, long incarnation, string zone = "zone", string hotfixVersion = "build-1") => new(
        new NodeReference(
            new ClusterIncarnationId(Guid.Parse("50000000-0000-0000-0000-000000000000")),
            new NodeId(id),
            new NodeIncarnationId(Guid.Parse($"{incarnation:D8}-5000-0000-0000-000000000000"))),
        ClusterMemberState.Active,
        new NodeEndpoint($"tcp://{id}:21000"),
        labels: null,
        actorHosts: [],
        startupActors: [new StartupActorDescriptor("test", Policy(), hotfixVersion, new Dictionary<string, string> { ["zone"] = zone })]);

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
            ClusterMemberState.Active,
            new NodeEndpoint($"tcp://{node}:21000"),
            labels: null,
            actorHosts: [],
            startupActors: [new StartupActorDescriptor("test", Policy(), "build-1", new Dictionary<string, string> { ["zone"] = "zone" })])).ToArray());

    private static NodeReference AffinityReference(ClusterIncarnationId cluster, string node, int incarnation) => new(
        cluster,
        new NodeId(node),
        new NodeIncarnationId(Guid.Parse($"{incarnation:D8}-5260-0000-0000-000000000000")));

    private static ClusterMember AffinityMember(NodeReference reference, ClusterMemberState state) => new(
        reference,
        state,
        new NodeEndpoint($"tcp://{reference.Node.Value}:21000"),
        labels: null,
        actorHosts: [],
        startupActors: [new StartupActorDescriptor(
            "test",
            Policy(),
            "build-1",
            new Dictionary<string, string> { ["zone"] = "zone" })]);

    private static ActorId FindAffinityIdOwnedBy(
        ClusterMembershipSnapshot snapshot,
        NodeReference expectedOwner)
    {
        for (var index = 0; index < 10_000; index++)
        {
            var id = ActorId.From($"@startup-affinity/test/{index}");
            if (StartupActorAffinityLayout.GetOwner(StartupActorAffinityLayout.GetShard(id), snapshot) == expectedOwner)
                return id;
        }

        throw new InvalidOperationException("Could not find an affinity id owned by the test node.");
    }

    private sealed record Request(string Value);
    [ActorName("test")]
    private sealed class TestActor : IActor { }
    private sealed class EmptyServiceProvider : IServiceProvider { public object? GetService(Type serviceType) => null; }
    private sealed class NoopHotfixInvoker : IHotfixServiceInvoker
    {
        public ValueTask<TResult> InvokeHttpAsync<TArg, TResult>(int endpointSlot, TArg arg, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask InvokeAsync<TContract, TArg>(int methodId, TArg arg, CancellationToken cancellationToken = default) => default;
        public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(int methodId, TArg arg, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class StubHotfixAccessor(HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor { public HotfixRuntimeSnapshot Current => snapshot; }
    private sealed class SequencedMembership(params ClusterMembershipSnapshot[] snapshots) : IClusterMembership
    {
        private int next;

        public ClusterMembershipSnapshot Current => snapshots[Math.Min(next++, snapshots.Length - 1)];

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(MembershipViewId observedView, CancellationToken cancellationToken = default) => new(Current);
    }
    private sealed class RecordingRemoteInvoker(params RemoteActorInvocationResult[] results) : IRemoteActorInvoker
    {
        private readonly Queue<RemoteActorInvocationResult> _results = new(results.Length == 0 ? [RemoteActorInvocationResult.Accepted()] : results);
        public List<RemoteActorInvocation> Invocations { get; } = [];
        public ValueTask<RemoteActorInvocationResult> AskAsync(RemoteActorInvocation invocation, CancellationToken cancellationToken = default) { Invocations.Add(invocation); return new(_results.Count > 1 ? _results.Dequeue() : _results.Peek()); }
        public ValueTask<RemoteActorInvocationResult> TellAsync(RemoteActorInvocation invocation, CancellationToken cancellationToken = default) { Invocations.Add(invocation); return new(_results.Count > 1 ? _results.Dequeue() : _results.Peek()); }
    }

    private sealed class FixedClusterClientFactory(IRpcClient client) : IClusterClientFactory
    {
        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default) => new(client);
    }

    private sealed class RetainFailingRpcClient : IRpcClient
    {
        public int RetainCalls { get; private set; }

        public ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct = default)
        {
            if (method.MethodId == ClusterProtocol.Methods.StartupAffinityRetain)
            {
                RetainCalls++;
                throw new ActorDirectoryUnavailableException("Injected indeterminate retain failure.");
            }

            return new ValueTask<TResult>((TResult)(object)new AffinityReply());
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
        }
    }
}
