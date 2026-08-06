using System.Diagnostics.Metrics;
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Actors.Internal;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

[Collection(ActorPopulationMetricsCollectionNames.Diagnostics)]
public sealed class ReplicatedActorActivationDirectoryTests
{
    [Fact]
    public async Task Rejected_exact_replica_send_retries_only_until_a_reply_is_accepted()
    {
        var fixture = CreateDiagnosticCluster(Guid.Parse("60900000-0000-0000-0000-000000000000"), 3, 14);
        fixture.Network.InjectExactStatusesFrom(new NodeId("data-1"));
        fixture.Network.QueueExactStatuses(ClusterSendStatus.Rejected, ClusterSendStatus.Rejected, ClusterSendStatus.Accepted);

        for (var i = 0; i < 64 && fixture.Network.ExactSendCount == 0; i++)
        {
            await fixture.Directories[0].ResolveAsync(ActorId.From($"player:retry-{i}"), TestContext.Current.CancellationToken);
        }

        Assert.Equal(3, fixture.Network.ExactSendCount);
        Assert.Equal(0, fixture.Gateway.PendingCount);
    }

    [Fact]
    public async Task Rejected_exact_replica_send_honors_cancellation_before_a_retry()
    {
        var fixture = CreateDiagnosticCluster(Guid.Parse("61000000-0000-0000-0000-000000000000"), 3, 15);
        fixture.Network.InjectExactStatusesFrom(new NodeId("data-1"));
        fixture.Network.QueueExactStatuses(ClusterSendStatus.Rejected);
        using var cancellation = new CancellationTokenSource();
        fixture.Network.OnExactSend = cancellation.Cancel;

        for (var i = 0; i < 64 && fixture.Network.ExactSendCount == 0; i++)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await fixture.Directories[0].ResolveAsync(ActorId.From($"player:cancel-{i}"), cancellation.Token));
        }

        Assert.Equal(1, fixture.Network.ExactSendCount);
        Assert.Equal(0, fixture.Gateway.PendingCount);
    }

    [Fact]
    public async Task Three_rejected_exact_replica_sends_fail_closed()
    {
        var fixture = CreateDiagnosticCluster(Guid.Parse("61100000-0000-0000-0000-000000000000"), 3, 16);
        fixture.Network.InjectExactStatusesFrom(new NodeId("data-1"));
        fixture.Network.QueueExactStatuses(ClusterSendStatus.Rejected, ClusterSendStatus.Rejected, ClusterSendStatus.Rejected);

        for (var i = 0; i < 64 && fixture.Network.ExactSendCount == 0; i++)
        {
            await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
                await fixture.Directories[0].ResolveAsync(ActorId.From($"player:fail-{i}"), TestContext.Current.CancellationToken));
        }

        Assert.Equal(3, fixture.Network.ExactSendCount);
        Assert.Equal(0, fixture.Gateway.PendingCount);
    }

    [Fact]
    public async Task Executed_replica_request_does_not_become_retryable_when_its_reply_send_is_rejected()
    {
        var fixture = CreateDiagnosticCluster(
            Guid.Parse("61200000-0000-0000-0000-000000000000"),
            memberCount: 2,
            membershipView: 17);
        fixture.Network.InjectExactStatusesFrom(new NodeId("data-1"));
        fixture.Network.QueueReplyStatuses(ClusterSendStatus.Rejected);

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await fixture.Directories[0].ResolveAsync(
                ActorId.From("player:reply-rejected"),
                TestContext.Current.CancellationToken));

        // The receiver executed once, but the response could not be delivered.
        // Its handler reports Failed, not Rejected, so the sender immediately
        // fails closed instead of replaying a request that may have mutated
        // replica state.
        Assert.Equal(1, fixture.Network.ExactSendCount);
        Assert.Equal(1, fixture.Network.InjectedReplyStatusCount);
        Assert.Equal(0, fixture.Gateway.PendingCount);
    }

    [Fact]
    public async Task Replica_reports_active_retained_and_released_activation_population()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("70000000-0000-0000-0000-000000000000"));
        var member = CreateMember(cluster, 1);
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            [member]));
        var network = new InProcessClusterNetwork();
        var gateway = new RemoteActorGateway();
        var directory = new ReplicatedActorActivationDirectory(
            membership,
            network,
            network,
            gateway,
            new LocalActorNodeIdentity(member.Reference.Node),
            new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) });
        network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());
        using var metrics = new ReplicatedPopulationMetricCollector();
        using var diagnostics = new ActorActivationPopulationDiagnostics(directory);
        var first = await directory.AcquireAsync(
            ActorId.From("player:first-observed"),
            member.Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);
        await directory.AcquireAsync(
            ActorId.From("player:second-observed"),
            member.Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);
        Assert.True(await directory.ReleaseAsync(
            first.Record.ActorId,
            first.Record.ActivationId!.Value,
            first.Record.Version,
            TestContext.Current.CancellationToken));

        var population = metrics.Observe();

        AssertPopulationMeasurement(population, "lakona-actor.activation.active", 1);
        AssertPopulationMeasurement(population, "lakona-actor.activation.metadata", 2);
        AssertPopulationMeasurement(population, "lakona-actor.activation.released", 1);
    }

    [Fact]
    public async Task Closed_authority_gate_rejects_new_activation_work()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("20000000-0000-0000-0000-000000000000"));
        var member = CreateMember(cluster, 1);
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            [member]));
        var network = new InProcessClusterNetwork();
        var gateway = new RemoteActorGateway();
        var directory = new ReplicatedActorActivationDirectory(
            membership,
            network,
            network,
            gateway,
            new LocalActorNodeIdentity(member.Reference.Node),
            new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) },
            new ClosedAdmissionGate());
        network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await directory.AcquireAsync(
                ActorId.From("player:fenced"),
                member.Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Adding_a_node_keeps_existing_actor_activations_sticky()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("10000000-0000-0000-0000-000000000000"));
        var members = Enumerable.Range(1, 4)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            members[..3]));
        var network = new InProcessClusterNetwork();
        var gateways = new List<RemoteActorGateway>();
        var directories = members.Select(member =>
        {
            var gateway = new RemoteActorGateway();
            gateways.Add(gateway);
            var directory = new ReplicatedActorActivationDirectory(
                membership,
                network,
                network,
                gateway,
                new LocalActorNodeIdentity(member.Reference.Node),
                new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) });
            network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());
            return directory;
        }).ToArray();

        var actorIds = Enumerable.Range(1, 64)
            .Select(index => ActorId.From($"player:{index}"))
            .ToArray();
        foreach (var actorId in actorIds)
        {
            var acquired = await directories[0].AcquireAsync(
                actorId,
                members[0].Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken);
            Assert.True(acquired.Acquired);
        }

        membership.Publish(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            members));

        foreach (var actorId in actorIds)
        {
            var resolved = await directories[3].ResolveAsync(
                actorId,
                TestContext.Current.CancellationToken);
            Assert.NotNull(resolved);
            Assert.Equal(members[0].Reference, resolved.OwnerReference);

            var reacquired = await directories[3].AcquireAsync(
                actorId,
                members[3].Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken);
            Assert.False(reacquired.Acquired);
            Assert.Equal(members[0].Reference, reacquired.Record.OwnerReference);
        }
    }

    [Fact]
    public async Task Expanding_a_single_node_cluster_preserves_the_existing_activation()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("40000000-0000-0000-0000-000000000000"));
        var members = Enumerable.Range(1, 3)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            members[..1]));
        var network = new InProcessClusterNetwork();
        var directories = members.Select(member =>
        {
            var gateway = new RemoteActorGateway();
            var directory = new ReplicatedActorActivationDirectory(
                membership,
                network,
                network,
                gateway,
                new LocalActorNodeIdentity(member.Reference.Node),
                new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) });
            network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());
            return directory;
        }).ToArray();
        var actorId = ActorId.From("matchmaking/@startup/data-1");
        var original = await directories[0].AcquireAsync(
            actorId,
            members[0].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        membership.Publish(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            members));

        var resolved = await directories[1].ResolveAsync(
            actorId,
            TestContext.Current.CancellationToken);
        var reacquired = await directories[2].AcquireAsync(
            actorId,
            members[2].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(original.Record.OwnerReference, resolved.OwnerReference);
        Assert.Equal(original.Record.ActivationId, resolved.ActivationId);
        Assert.Equal(original.Record.Version, resolved.Version);
        Assert.False(reacquired.Acquired);
        Assert.Equal(original.Record.OwnerReference, reacquired.Record.OwnerReference);
        Assert.Equal(original.Record.ActivationId, reacquired.Record.ActivationId);
        Assert.Equal(original.Record.Version, reacquired.Record.Version);
    }

    [Fact]
    public async Task Repeated_release_and_reacquire_remains_monotonic_across_expansion()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("50000000-0000-0000-0000-000000000000"));
        var members = Enumerable.Range(1, 3)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            members[..1]));
        var network = new InProcessClusterNetwork();
        var directories = members.Select(member =>
        {
            var gateway = new RemoteActorGateway();
            var directory = new ReplicatedActorActivationDirectory(
                membership,
                network,
                network,
                gateway,
                new LocalActorNodeIdentity(member.Reference.Node),
                new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) });
            network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());
            return directory;
        }).ToArray();
        var actorId = ActorId.From("player:repeated-lifecycle");
        var first = await directories[0].AcquireAsync(
            actorId,
            members[0].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        Assert.True(await directories[0].ReleaseAsync(
            actorId,
            first.Record.ActivationId!.Value,
            first.Record.Version,
            TestContext.Current.CancellationToken));
        Assert.Null(await directories[0].ResolveAsync(
            actorId,
            TestContext.Current.CancellationToken));

        membership.Publish(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            members));

        Assert.Null(await directories[1].ResolveAsync(
            actorId,
            TestContext.Current.CancellationToken));
        var second = await directories[1].AcquireAsync(
            actorId,
            members[1].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        Assert.True(second.Acquired);
        Assert.NotEqual(first.Record.ActivationId, second.Record.ActivationId);
        Assert.True(second.Record.Version > first.Record.Version);
        Assert.True(await directories[2].ReleaseAsync(
            actorId,
            second.Record.ActivationId!.Value,
            second.Record.Version,
            TestContext.Current.CancellationToken));

        var third = await directories[0].AcquireAsync(
            actorId,
            members[2].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        Assert.True(third.Acquired);
        Assert.NotEqual(second.Record.ActivationId, third.Record.ActivationId);
        Assert.True(third.Record.Version > second.Record.Version);
        foreach (var directory in directories)
        {
            var resolved = await directory.ResolveAsync(
                actorId,
                TestContext.Current.CancellationToken);
            Assert.NotNull(resolved);
            Assert.Equal(third.Record.OwnerReference, resolved.OwnerReference);
            Assert.Equal(third.Record.ActivationId, resolved.ActivationId);
            Assert.Equal(third.Record.Version, resolved.Version);
        }
    }

    [Fact]
    public async Task Authoritative_read_failure_reports_bounded_targeted_diagnostics_without_actor_identity()
    {
        var fixture = CreateDiagnosticCluster(
            Guid.Parse("60000000-0000-0000-0000-000000000000"),
            memberCount: 3,
            membershipView: 7);
        fixture.Network.SetKindAvailable("_activation_replica_resolve_v2", available: false);
        const string actorIdentity = "player:diagnostic-secret";

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await fixture.Directories[0].ResolveAsync(
                ActorId.From(actorIdentity),
                TestContext.Current.CancellationToken));

        var entry = Assert.Single(fixture.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(4101, entry.EventId.Id);
        Assert.Equal("ActorActivationReplicaFailure", entry.EventId.Name);
        Assert.Equal("authoritative-read", entry.Properties["Phase"]);
        Assert.Contains(
            entry.Properties["TargetNode"]?.ToString(),
            fixture.Members.Select(static member => member.Reference.Node.Value));
        var targetNode = entry.Properties["TargetNode"]?.ToString();
        var target = Assert.Single(
            fixture.Members,
            member => string.Equals(member.Reference.Node.Value, targetNode, StringComparison.Ordinal));
        Assert.Equal(
            target.Reference.Incarnation.Value.ToString(),
            entry.Properties["TargetNodeIncarnation"]?.ToString());
        Assert.Equal("7", entry.Properties["MembershipView"]?.ToString());
        Assert.Equal("exception", entry.Properties["Result"]);
        Assert.Equal("unavailable", entry.Properties["ExceptionCategory"]);
        Assert.Equal(nameof(ActorDirectoryUnavailableException), entry.Properties["ExceptionType"]);
        Assert.Equal("0", entry.Properties["SuppressedCount"]?.ToString());
        Assert.DoesNotContain(actorIdentity, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            entry.Properties.Values,
            value => string.Equals(value?.ToString(), actorIdentity, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authoritative_read_protocol_rejection_reports_a_categorized_result()
    {
        var fixture = CreateDiagnosticCluster(
            Guid.Parse("60500000-0000-0000-0000-000000000000"),
            memberCount: 3,
            membershipView: 11);
        fixture.Network.RejectKind("_activation_replica_resolve_v2");

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await fixture.Directories[0].ResolveAsync(
                ActorId.From("player:protocol-rejection"),
                TestContext.Current.CancellationToken));

        var entry = Assert.Single(fixture.Logger.Entries);
        Assert.Equal("authoritative-read", entry.Properties["Phase"]);
        Assert.Equal("rejected", entry.Properties["Result"]);
        Assert.Equal("none", entry.Properties["ExceptionCategory"]);
        Assert.Equal("none", entry.Properties["ExceptionType"]);
    }

    [Fact]
    public async Task Repeated_authoritative_read_failures_are_aggregated_per_reporting_window()
    {
        var fixture = CreateDiagnosticCluster(
            Guid.Parse("60600000-0000-0000-0000-000000000000"),
            memberCount: 3,
            membershipView: 12);
        fixture.Network.SetKindAvailable("_activation_replica_resolve_v2", available: false);
        var actorId = ActorId.From("player:bounded-diagnostic");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
                await fixture.Directories[0].ResolveAsync(
                    actorId,
                    TestContext.Current.CancellationToken));
        }

        Assert.Single(fixture.Logger.Entries);
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await fixture.Directories[0].ResolveAsync(
                actorId,
                TestContext.Current.CancellationToken));

        Assert.Equal(2, fixture.Logger.Entries.Count);
        Assert.Equal("7", fixture.Logger.Entries[1].Properties["SuppressedCount"]?.ToString());
    }

    [Fact]
    public async Task Caller_cancellation_does_not_report_an_activation_replica_failure()
    {
        var fixture = CreateDiagnosticCluster(
            Guid.Parse("60700000-0000-0000-0000-000000000000"),
            memberCount: 3,
            membershipView: 13);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Directories[0].ResolveAsync(
                ActorId.From("player:canceled-diagnostic"),
                cancellation.Token));

        Assert.Empty(fixture.Logger.Entries);
    }

    [Fact]
    public async Task Replica_repair_failure_reports_degradation_without_failing_the_read()
    {
        var fixture = CreateDiagnosticCluster(
            Guid.Parse("61000000-0000-0000-0000-000000000000"),
            memberCount: 3,
            membershipView: 8);
        var actorId = ActorId.From("player:repair-diagnostic");
        var acquired = await fixture.Directories[0].AcquireAsync(
            actorId,
            fixture.Members[0].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);
        fixture.Network.SetKindAvailable("_activation_replicate_record_v2", available: false);

        var resolved = await fixture.Directories[1].ResolveAsync(
            actorId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(acquired.Record.ActivationId, resolved.ActivationId);
        var entry = Assert.Single(fixture.Logger.Entries);
        Assert.Equal("replica-repair", entry.Properties["Phase"]);
        Assert.Equal("8", entry.Properties["MembershipView"]?.ToString());
    }

    [Fact]
    public async Task Quorum_commit_failure_reports_the_failed_target_and_preserves_fail_closed_behavior()
    {
        var fixture = CreateDiagnosticCluster(
            Guid.Parse("62000000-0000-0000-0000-000000000000"),
            memberCount: 3,
            membershipView: 9);
        fixture.Network.SetKindAvailable("_activation_replicate_record_v2", available: false);

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await fixture.Directories[0].AcquireAsync(
                ActorId.From("player:quorum-diagnostic"),
                fixture.Members[0].Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken));

        var entry = Assert.Single(fixture.Logger.Entries);
        Assert.Equal("quorum-commit", entry.Properties["Phase"]);
        Assert.Equal("9", entry.Properties["MembershipView"]?.ToString());
        Assert.Equal("unavailable", entry.Properties["ExceptionCategory"]);
    }

    [Fact]
    public async Task Additional_propagation_failure_reports_degradation_after_a_successful_commit()
    {
        var cluster = Guid.Parse("63000000-0000-0000-0000-000000000000");
        LogEntry? reported = null;
        NodeId? proposedOwner = null;

        for (var ownerIndex = 0; ownerIndex < 4 && reported is null; ownerIndex++)
        {
            var fixture = CreateDiagnosticCluster(
                cluster,
                memberCount: 4,
                membershipView: 10);
            fixture.Network.FailKindAfterSuccessfulSends("_activation_replicate_record_v2", 2);

            var acquired = await fixture.Directories[0].AcquireAsync(
                ActorId.From("player:additional-propagation-diagnostic"),
                fixture.Members[ownerIndex].Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken);

            Assert.True(acquired.Acquired);
            if (fixture.Logger.Entries.Count > 0)
            {
                reported = Assert.Single(fixture.Logger.Entries);
                proposedOwner = fixture.Members[ownerIndex].Reference.Node;
            }
        }

        Assert.NotNull(reported);
        Assert.Equal("additional-propagation", reported.Properties["Phase"]);
        Assert.Equal(proposedOwner!.Value.Value, reported.Properties["TargetNode"]);
        Assert.Equal("10", reported.Properties["MembershipView"]?.ToString());
    }

    [Fact]
    public async Task Resolve_fails_closed_when_a_ready_member_cannot_reconcile_the_record()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("60000000-0000-0000-0000-000000000000"));
        var members = Enumerable.Range(1, 3)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            members));
        var network = new InProcessClusterNetwork();
        var directories = members.Select(member =>
        {
            var gateway = new RemoteActorGateway();
            var directory = new ReplicatedActorActivationDirectory(
                membership,
                network,
                network,
                gateway,
                new LocalActorNodeIdentity(member.Reference.Node),
                new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) });
            network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());
            return directory;
        }).ToArray();
        var actorId = ActorId.From("player:reconciliation-failure");
        await directories[0].AcquireAsync(
            actorId,
            members[0].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        for (var blockedIndex = 0; blockedIndex < members.Length; blockedIndex++)
        {
            network.SetAvailable(members[blockedIndex].Reference.Node, available: false);
            for (var callerIndex = 0; callerIndex < directories.Length; callerIndex++)
            {
                if (callerIndex == blockedIndex)
                {
                    continue;
                }

                await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
                    await directories[callerIndex].ResolveAsync(
                        actorId,
                        TestContext.Current.CancellationToken));
            }

            network.SetAvailable(members[blockedIndex].Reference.Node, available: true);
        }
    }

    [Fact]
    public async Task Released_activations_do_not_resurrect_after_replica_set_contraction()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("70000000-0000-0000-0000-000000000000"));
        var members = Enumerable.Range(1, 4)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            members[..3]));
        var network = new InProcessClusterNetwork();
        var directories = members.Select(member =>
        {
            var gateway = new RemoteActorGateway();
            var directory = new ReplicatedActorActivationDirectory(
                membership,
                network,
                network,
                gateway,
                new LocalActorNodeIdentity(member.Reference.Node),
                new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) });
            network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());
            return directory;
        }).ToArray();
        var activations = new List<ActorDirectoryRecord>();
        for (var index = 0; index < 64; index++)
        {
            var acquired = await directories[0].AcquireAsync(
                ActorId.From($"player:released-before-contraction:{index}"),
                members[index % 3].Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken);
            activations.Add(acquired.Record);
        }

        membership.Publish(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            members));
        foreach (var activation in activations)
        {
            Assert.True(await directories[3].ReleaseAsync(
                activation.ActorId,
                activation.ActivationId!.Value,
                activation.Version,
                TestContext.Current.CancellationToken));
        }

        for (var survivor = 0; survivor < members.Length; survivor++)
        {
            membership.Publish(new ClusterMembershipSnapshot(
                cluster,
                new MembershipViewId(3 + survivor),
                [members[survivor]]));
            foreach (var activation in activations)
            {
                Assert.Null(await directories[survivor].ResolveAsync(
                    activation.ActorId,
                    TestContext.Current.CancellationToken));
            }
        }
    }

    [Fact]
    public async Task Concurrent_reacquire_after_release_has_one_winner()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("80000000-0000-0000-0000-000000000000"));
        var members = Enumerable.Range(1, 3)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            members));
        var network = new InProcessClusterNetwork();
        var directories = members.Select(member =>
        {
            var gateway = new RemoteActorGateway();
            var directory = new ReplicatedActorActivationDirectory(
                membership,
                network,
                network,
                gateway,
                new LocalActorNodeIdentity(member.Reference.Node),
                new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) });
            network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());
            return directory;
        }).ToArray();
        var actorId = ActorId.From("player:concurrent-reacquire");
        var original = await directories[0].AcquireAsync(
            actorId,
            members[0].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);
        Assert.True(await directories[1].ReleaseAsync(
            actorId,
            original.Record.ActivationId!.Value,
            original.Record.Version,
            TestContext.Current.CancellationToken));

        var attempts = directories.Select((directory, index) => directory.AcquireAsync(
                actorId,
                members[index].Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken)
            .AsTask()).ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, static result => result.Acquired);
        Assert.All(results, result =>
        {
            Assert.Equal(results[0].Record.OwnerReference, result.Record.OwnerReference);
            Assert.Equal(results[0].Record.ActivationId, result.Record.ActivationId);
            Assert.Equal(results[0].Record.Version, result.Record.Version);
        });
        Assert.True(results[0].Record.Version > original.Record.Version);
    }

    [Fact]
    public async Task Removed_owner_is_superseded_with_a_higher_activation_version()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("30000000-0000-0000-0000-000000000000"));
        var members = Enumerable.Range(1, 3)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            members));
        var network = new InProcessClusterNetwork();
        var directories = members.Select(member =>
        {
            var gateway = new RemoteActorGateway();
            var directory = new ReplicatedActorActivationDirectory(
                membership,
                network,
                network,
                gateway,
                new LocalActorNodeIdentity(member.Reference.Node),
                new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) });
            network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());
            return directory;
        }).ToArray();
        var actorId = ActorId.From("player:recover");
        var first = await directories[0].AcquireAsync(
            actorId,
            members[2].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        membership.Publish(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            members[..2]));

        var replacement = await directories[0].AcquireAsync(
            actorId,
            members[0].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        Assert.True(replacement.Acquired);
        Assert.Equal(members[0].Reference, replacement.Record.OwnerReference);
        Assert.NotEqual(first.Record.ActivationId, replacement.Record.ActivationId);
        Assert.True(replacement.Record.Version > first.Record.Version);
    }

    private static ClusterMember CreateMember(ClusterIncarnationId cluster, int index)
    {
        return new ClusterMember(
            new NodeReference(
                cluster,
                new NodeId($"data-{index}"),
                new NodeIncarnationId(Guid.Parse($"{index:D8}-0000-0000-0000-000000000000"))),
            ClusterMemberState.Ready,
            new NodeEndpoint($"tcp://127.0.0.1:{22000 + index}"),
            isVoter: true);
    }

    private static DiagnosticCluster CreateDiagnosticCluster(
        Guid clusterValue,
        int memberCount,
        long membershipView)
    {
        var cluster = new ClusterIncarnationId(clusterValue);
        var members = Enumerable.Range(1, memberCount)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(membershipView),
            members));
        var network = new InProcessClusterNetwork();
        var logger = new RecordingLogger<ReplicatedActorActivationDirectory>();
        var time = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        var gateway = new RemoteActorGateway();
        var directories = members.Select(member =>
        {
            var memberGateway = member.Reference == members[0].Reference
                ? gateway
                : new RemoteActorGateway();
            var directory = new ReplicatedActorActivationDirectory(
                membership,
                network,
                network,
                memberGateway,
                new LocalActorNodeIdentity(member.Reference.Node),
                new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) },
                logger: logger,
                timeProvider: time);
            network.Register(member.Reference.Node, directory, memberGateway.CreateReplyHandler());
            return directory;
        }).ToArray();
        return new DiagnosticCluster(members, network, logger, time, directories, gateway);
    }

    private static void AssertPopulationMeasurement(
        IReadOnlyDictionary<string, PopulationMeasurement> population,
        string name,
        long expected)
    {
        var measurement = population[name];
        Assert.Equal(expected, measurement.Value);
        Assert.Empty(measurement.Tags);
    }

    private sealed class ReplicatedPopulationMetricCollector : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly Dictionary<string, PopulationMeasurement> measurements = new(StringComparer.Ordinal);

        public ReplicatedPopulationMetricCollector()
        {
            listener.InstrumentPublished = (instrument, currentListener) =>
            {
                if (instrument.Meter.Name == LakonaActorDiagnostics.MeterName)
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
                measurements[instrument.Name] = new PopulationMeasurement(measurement, tags.ToArray()));
            listener.Start();
        }

        public IReadOnlyDictionary<string, PopulationMeasurement> Observe()
        {
            listener.RecordObservableInstruments();
            return measurements;
        }

        public void Dispose()
        {
            listener.Dispose();
        }
    }

    private sealed record PopulationMeasurement(
        long Value,
        IReadOnlyList<KeyValuePair<string, object?>> Tags);

    private sealed record DiagnosticCluster(
        ClusterMember[] Members,
        InProcessClusterNetwork Network,
        RecordingLogger<ReplicatedActorActivationDirectory> Logger,
        ManualTimeProvider Time,
        ReplicatedActorActivationDirectory[] Directories,
        RemoteActorGateway Gateway);

    private sealed class MutableMembership(ClusterMembershipSnapshot current) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; private set; } = current;

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId observedView,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);

        public void Publish(ClusterMembershipSnapshot snapshot) => Current = snapshot;
    }

    private sealed class InProcessClusterNetwork : IExactClusterNodeSender, IClusterNodeSender
    {
        private readonly Dictionary<NodeId, Endpoint> endpoints = new();
        private readonly HashSet<NodeId> unavailable = [];
        private readonly HashSet<string> unavailableKinds = new(StringComparer.Ordinal);
        private readonly HashSet<string> rejectedKinds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> failAfterSuccessfulSends = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> successfulSends = new(StringComparer.Ordinal);
        private readonly Queue<ClusterSendStatus> exactStatuses = [];
        private readonly Queue<ClusterSendStatus> replyStatuses = [];

        public int ExactSendCount { get; private set; }

        public int InjectedReplyStatusCount { get; private set; }

        public Action? OnExactSend { get; set; }

        private NodeId? injectedSource;

        public void InjectExactStatusesFrom(NodeId source) => injectedSource = source;

        public void QueueExactStatuses(params ClusterSendStatus[] statuses)
        {
            foreach (var status in statuses) exactStatuses.Enqueue(status);
        }

        public void QueueReplyStatuses(params ClusterSendStatus[] statuses)
        {
            foreach (var status in statuses) replyStatuses.Enqueue(status);
        }

        public void Register(
            NodeId node,
            IClusterMessageHandler activationHandler,
            IClusterMessageHandler replyHandler) =>
            endpoints.Add(node, new Endpoint(activationHandler, replyHandler));

        public void SetAvailable(NodeId node, bool available)
        {
            if (available)
            {
                unavailable.Remove(node);
            }
            else
            {
                unavailable.Add(node);
            }
        }

        public void SetKindAvailable(string kind, bool available)
        {
            if (available)
            {
                unavailableKinds.Remove(kind);
            }
            else
            {
                unavailableKinds.Add(kind);
            }
        }

        public void FailKindAfterSuccessfulSends(string kind, int count)
        {
            failAfterSuccessfulSends[kind] = count;
            successfulSends[kind] = 0;
        }

        public void RejectKind(string kind) => rejectedKinds.Add(kind);

        public ValueTask<ClusterSendStatus> SendAsync(
            NodeReference target,
            MembershipViewId view,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            if (injectedSource is null || message.SourceNode == injectedSource)
            {
                ExactSendCount++;
                OnExactSend?.Invoke();
                if (exactStatuses.TryDequeue(out var injected))
                {
                    if (injected != ClusterSendStatus.Accepted)
                    {
                        return new ValueTask<ClusterSendStatus>(injected);
                    }
                }
            }
            if (unavailable.Contains(target.Node) || unavailableKinds.Contains(message.Kind))
            {
                return new ValueTask<ClusterSendStatus>(ClusterSendStatus.NodeUnavailable);
            }

            if (rejectedKinds.Contains(message.Kind))
            {
                return RemoteActorGateway.SendReplyAsync(
                    this,
                    target.Node,
                    message.SourceNode,
                    message.CorrelationId!,
                    JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        Succeeded = false,
                        Error = "Injected protocol rejection."
                    }),
                    cancellationToken);
            }

            var successful = successfulSends.GetValueOrDefault(message.Kind);
            if (failAfterSuccessfulSends.TryGetValue(message.Kind, out var allowed) && successful >= allowed)
            {
                return new ValueTask<ClusterSendStatus>(ClusterSendStatus.NodeUnavailable);
            }

            successfulSends[message.Kind] = successful + 1;
            return endpoints[target.Node].ActivationHandler.HandleAsync(message, cancellationToken);
        }

        public ValueTask<ClusterSendStatus> SendAsync(
            NodeId nodeId,
            long? expectedNodeEpoch,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default) =>
            unavailable.Contains(nodeId)
                ? new ValueTask<ClusterSendStatus>(ClusterSendStatus.NodeUnavailable)
                : SendReplyWithInjectedStatusAsync(nodeId, message, cancellationToken);

        private ValueTask<ClusterSendStatus> SendReplyWithInjectedStatusAsync(
            NodeId nodeId,
            ClusterMessage message,
            CancellationToken cancellationToken)
        {
            if (replyStatuses.TryDequeue(out var injected))
            {
                InjectedReplyStatusCount++;
                return new ValueTask<ClusterSendStatus>(injected);
            }

            return endpoints[nodeId].ReplyHandler.HandleAsync(message, cancellationToken);
        }

        private sealed record Endpoint(
            IClusterMessageHandler ActivationHandler,
            IClusterMessageHandler ReplyHandler);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(static pair => pair.Key, static pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }

    private sealed class ClosedAdmissionGate : IDistributedWorkAdmissionGate
    {
        public bool IsOpen => false;

        public bool TryEnter(out DistributedWorkAdmission admission)
        {
            admission = default;
            return false;
        }

        public void Exit(DistributedWorkAdmission admission) =>
            throw new InvalidOperationException("No work was admitted.");
    }
}
