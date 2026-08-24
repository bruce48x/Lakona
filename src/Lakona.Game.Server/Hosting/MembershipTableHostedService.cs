using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hosting;

internal sealed class MembershipTableHostedService : BackgroundService
{
    private readonly LakonaGameRuntimeOptions runtime;
    private readonly MembershipTableManager manager;
    private readonly IClusterMembership membership;
    private readonly IMembershipProbeTransport probes;
    private readonly DistributedWorkAdmissionGate admissionGate;
    private readonly ClusterRecoveryBarrier recovery;
    private readonly IServiceProvider services;
    private readonly IHostApplicationLifetime? lifetime;
    private readonly ILogger<MembershipTableHostedService> logger;
    private readonly Dictionary<NodeReference, int> failedProbes = [];
    private DateTimeOffset lastTableContact;
    private DateTimeOffset nextDefunctCleanup;
    private DateTimeOffset nextTableRefresh;
    private DateTimeOffset nextIAmAlive;
    private DateTimeOffset nextProbe;

    public MembershipTableHostedService(
        LakonaGameRuntimeOptions runtime,
        MembershipTableManager manager,
        IClusterMembership membership,
        IMembershipProbeTransport probes,
        DistributedWorkAdmissionGate admissionGate,
        IEnumerable<IClusterRecoveryParticipant> recoveryParticipants,
        IServiceProvider services,
        ILogger<MembershipTableHostedService> logger,
        IHostApplicationLifetime? lifetime = null)
    {
        this.runtime = runtime;
        this.manager = manager;
        this.membership = membership;
        this.probes = probes;
        this.admissionGate = admissionGate;
        recovery = new ClusterRecoveryBarrier(recoveryParticipants);
        this.services = services;
        this.lifetime = lifetime;
        this.logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var local = await RetryAsync(
            token => ObserveTableOperationAsync("join", () => manager.JoinAsync(token)),
            "join membership",
            cancellationToken).ConfigureAwait(false);
        services.GetService<LocalActorNodeIdentity>()?.Observe(local);
        ClusterDiagnostics.RecordMembershipLifecycle("joining");
        await RetryAsync(
            token => ValidateConnectivityAsync(local, runtime.Cluster.Membership, token),
            "validate two-way connectivity",
            cancellationToken).ConfigureAwait(false);
        await recovery.RecoverAsync(new ClusterRecoveryContext(local, membership.Current), cancellationToken).ConfigureAwait(false);
        var descriptor = CreateDescriptor();
        await RetryAsync(
            token => ObserveTableOperationAsync(
                "activate",
                () => manager.ActivateAsync(descriptor.Labels, descriptor.ActorHosts, descriptor.StartupActors, token)),
            "activate membership",
            cancellationToken).ConfigureAwait(false);
        await GossipMembershipAsync(cancellationToken).ConfigureAwait(false);
        admissionGate.Open();
        ClusterDiagnostics.RecordMembershipLifecycle("active");
        var now = DateTimeOffset.UtcNow;
        lastTableContact = now;
        nextDefunctCleanup = now.AddSeconds(runtime.Cluster.Membership.DefunctEntryCleanupIntervalSeconds);
        nextTableRefresh = now.AddSeconds(runtime.Cluster.Membership.TableRefreshSeconds);
        nextIAmAlive = now.AddSeconds(runtime.Cluster.Membership.IAmAliveSeconds);
        nextProbe = now;
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Cluster node active. NodeId={NodeId} ClusterId={ClusterId} Incarnation={Incarnation}",
            local.Node.Value,
            runtime.Cluster.Id,
            local.Incarnation.Value);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await admissionGate.CloseAndDrainAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        try
        {
            await ObserveTableOperationAsync("stopping", () => manager.MarkStoppingAsync(cancellationToken)).ConfigureAwait(false);
            ClusterDiagnostics.RecordMembershipLifecycle("stopping");
            await ObserveTableOperationAsync("dead", () => manager.MarkDeadAsync(cancellationToken)).ConfigureAwait(false);
            ClusterDiagnostics.RecordMembershipLifecycle("dead");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not publish graceful cluster shutdown.");
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask RefreshDescriptorAsync(CancellationToken cancellationToken = default)
    {
        var descriptor = CreateDescriptor();
        await manager.ActivateAsync(descriptor.Labels, descriptor.ActorHosts, descriptor.StartupActors, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask MarkUnavailableAsync()
    {
        await admissionGate.CloseAndDrainAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
        await manager.MarkStoppingAsync(CancellationToken.None).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = runtime.Cluster.Membership;
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var delay = Earliest(nextTableRefresh, nextIAmAlive, nextProbe, nextDefunctCleanup) - now;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                now = DateTimeOffset.UtcNow;
            }

            var refreshDue = now >= nextTableRefresh;
            var iAmAliveDue = now >= nextIAmAlive;
            var probeDue = now >= nextProbe;
            var cleanupDue = now >= nextDefunctCleanup;
            if (refreshDue) nextTableRefresh = now.AddSeconds(options.TableRefreshSeconds);
            if (iAmAliveDue) nextIAmAlive = now.AddSeconds(options.IAmAliveSeconds);
            if (probeDue) nextProbe = now.AddSeconds(options.ProbeIntervalSeconds);
            if (cleanupDue) nextDefunctCleanup = now.AddSeconds(options.DefunctEntryCleanupIntervalSeconds);

            try
            {
                if (refreshDue)
                {
                    await ObserveTableOperationAsync("refresh", () => manager.RefreshAsync(stoppingToken)).ConfigureAwait(false);
                    lastTableContact = DateTimeOffset.UtcNow;
                }

                if (iAmAliveDue)
                {
                    await ObserveTableOperationAsync("heartbeat", () => manager.UpdateIAmAliveAsync(stoppingToken)).ConfigureAwait(false);
                    lastTableContact = DateTimeOffset.UtcNow;
                }

                if (probeDue) await ProbeTargetsAsync(options, stoppingToken).ConfigureAwait(false);

                if (cleanupDue)
                {
                    var removed = await ObserveTableOperationAsync(
                        "cleanup",
                        () => manager.CleanupDefunctAsync(
                            TimeSpan.FromSeconds(options.DefunctEntryRetentionSeconds),
                            options.DefunctEntryCleanupBatchSize,
                            stoppingToken)).ConfigureAwait(false);
                    lastTableContact = DateTimeOffset.UtcNow;
                    if (removed > 0) logger.LogInformation("Removed {Count} expired defunct membership rows.", removed);
                }
            }
            catch (ClusterMembershipFencedException exception)
            {
                logger.LogCritical(exception, "This node incarnation is Dead in the membership table and will stop.");
                ClusterDiagnostics.RecordMembershipLifecycle("fenced");
                lifetime?.StopApplication();
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (DateTimeOffset.UtcNow - lastTableContact >= TimeSpan.FromSeconds(options.IAmAliveSeconds))
                {
                    logger.LogCritical(
                        exception,
                        "Membership table has been unreachable for the safety window; this node will stop admitting work.");
                    ClusterDiagnostics.RecordMembershipLifecycle("table_unavailable");
                    await admissionGate.CloseAndDrainAsync(TimeSpan.FromSeconds(30), CancellationToken.None)
                        .ConfigureAwait(false);
                    lifetime?.StopApplication();
                    return;
                }

                logger.LogWarning(exception, "Membership refresh failed; continuing with the last committed snapshot.");
            }
        }
    }

    private async ValueTask ProbeTargetsAsync(LakonaGameMembershipOptions options, CancellationToken cancellationToken)
    {
        var snapshot = membership.Current;
        var local = manager.Local;
        var activeReferences = snapshot.Members
            .Where(static member => member.State == ClusterMemberState.Active)
            .Select(static member => member.Reference)
            .ToHashSet();
        foreach (var departed in failedProbes.Keys.Where(reference => !activeReferences.Contains(reference)).ToArray())
        {
            failedProbes.Remove(departed);
        }

        foreach (var target in MembershipProbeTargetSelector.Select(snapshot, local, options.MonitoredNodes))
        {
            if (await probes.ProbeAsync(local, target, target.ClusterEndpoint, false, cancellationToken).ConfigureAwait(false)
                || await TryIndirectProbeAsync(snapshot, local, target, options.IndirectProbes, cancellationToken).ConfigureAwait(false))
            {
                failedProbes.Remove(target.Reference);
                continue;
            }

            var failures = failedProbes.GetValueOrDefault(target.Reference) + 1;
            failedProbes[target.Reference] = failures;
            if (failures < options.FailedProbesBeforeSuspect) continue;
            var declaredDead = await manager.TrySuspectAsync(
                target.Reference,
                options.VotesForDeath,
                TimeSpan.FromSeconds(options.SuspectVoteLifetimeSeconds),
                cancellationToken).ConfigureAwait(false);
            if (declaredDead)
            {
                await GossipMembershipAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask GossipMembershipAsync(CancellationToken cancellationToken)
    {
        var snapshot = membership.Current;
        foreach (var member in snapshot.Members.Where(member =>
                     member.State == ClusterMemberState.Active && member.Reference != manager.Local))
        {
            try
            {
                await probes.GossipAsync(manager.Local, member.ClusterEndpoint, snapshot.View, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogDebug(exception, "Membership gossip to {NodeId} failed.", member.Reference.Node.Value);
            }
        }
    }

    private async ValueTask<bool> TryIndirectProbeAsync(
        ClusterMembershipSnapshot snapshot,
        NodeReference local,
        ClusterMember target,
        int helperCount,
        CancellationToken cancellationToken)
    {
        var helpers = snapshot.Members
            .Where(member => member.State == ClusterMemberState.Active
                && member.Reference != local
                && member.Reference != target.Reference)
            .Take(helperCount);
        foreach (var helper in helpers)
        {
            if (await probes.ProbeAsync(local, target, helper.ClusterEndpoint, true, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private async ValueTask ValidateConnectivityAsync(
        NodeReference local,
        LakonaGameMembershipOptions options,
        CancellationToken cancellationToken)
    {
        var tableSnapshot = await manager.ReadTableAsync(cancellationToken).ConfigureAwait(false);
        var localMember = membership.Current.Members.Single(member => member.Reference == local);
        foreach (var active in tableSnapshot.Entries.Where(entry => entry.Status == MembershipTableStatus.Active))
        {
            var projected = new ClusterMember(
                active.Reference,
                ClusterMemberState.Active,
                active.ClusterEndpoint,
                active.Labels,
                active.ActorHosts,
                active.StartupActors);
            var outgoing = await probes.ProbeAsync(local, projected, active.ClusterEndpoint, false, cancellationToken).ConfigureAwait(false);
            var incoming = await probes.ProbeAsync(local, localMember, active.ClusterEndpoint, true, cancellationToken).ConfigureAwait(false);
            if (!outgoing || !incoming)
            {
                var declaredDead = await manager.TryMarkDefunctAsync(
                    active.Reference,
                    TimeSpan.FromSeconds(options.AllowedIAmAliveMissSeconds),
                    cancellationToken).ConfigureAwait(false);
                if (declaredDead)
                {
                    logger.LogWarning(
                        "Declared stale and unreachable member Dead during startup. NodeId={NodeId} IAmAlive={IAmAlive}",
                        active.Reference.Node.Value,
                        active.IAmAliveTime);
                    continue;
                }

                throw new InvalidOperationException($"Joining node '{local.Node.Value}' does not have two-way cluster connectivity with '{active.Reference.Node.Value}'.");
            }
        }
    }

    private ClusterMember CreateDescriptor()
    {
        var current = membership.Current.Members.Single(member => member.Reference == manager.Local);
        var actorHosts = new List<NodeActorHostDescriptor>();
        var catalog = services.GetService<ActorHostDescriptorCatalog>();
        var hotfixHosts = services.GetService<IHotfixManager>()?.Current.ActorHosts
            .ToDictionary(static descriptor => descriptor.Actor, StringComparer.OrdinalIgnoreCase);
        foreach (var actor in runtime.ActorHosts)
        {
            if (catalog is not null && catalog.TryGet(actor, out var descriptor))
            {
                actorHosts.Add(new NodeActorHostDescriptor(descriptor.Actor, descriptor.PolicyHash, descriptor.BuildTag, descriptor.Metadata));
            }
            else if (hotfixHosts is not null && hotfixHosts.TryGetValue(actor, out var hotfix))
            {
                actorHosts.Add(new NodeActorHostDescriptor(hotfix.Actor, hotfix.PolicyHash, hotfix.BuildTag, hotfix.Metadata));
            }
            else
            {
                throw new InvalidOperationException($"Lakona:ActorHosts contains unknown actor host '{actor}'.");
            }
        }

        return new ClusterMember(
            current.Reference,
            ClusterMemberState.Joining,
            current.ClusterEndpoint,
            current.Labels,
            actorHosts,
            services.GetService<StartupActorDescriptorCatalog>()?.Snapshot());
    }

    private async ValueTask<T> RetryAsync<T>(Func<CancellationToken, ValueTask<T>> operation, string name, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(200);
        while (true)
        {
            try { return await operation(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (ClusterMembershipFencedException) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Cluster operation {Operation} failed; retrying.", name);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 5000));
            }
        }
    }

    private async ValueTask RetryAsync(Func<CancellationToken, ValueTask> operation, string name, CancellationToken cancellationToken)
    {
        await RetryAsync(async token => { await operation(token).ConfigureAwait(false); return true; }, name, cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset Earliest(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third,
        DateTimeOffset fourth)
    {
        var firstPair = first <= second ? first : second;
        var secondPair = third <= fourth ? third : fourth;
        return firstPair <= secondPair ? firstPair : secondPair;
    }

    private static async ValueTask<T> ObserveTableOperationAsync<T>(
        string operation,
        Func<ValueTask<T>> action)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var outcome = "failure";
        using var activity = ClusterDiagnostics.StartActivity("cluster.membership.table");
        activity?.SetTag("lakona.game.cluster.operation", operation);
        try
        {
            var result = await action().ConfigureAwait(false);
            outcome = "success";
            return result;
        }
        catch (OperationCanceledException)
        {
            outcome = "canceled";
            throw;
        }
        finally
        {
            activity?.SetTag("lakona.game.cluster.outcome", outcome);
            ClusterDiagnostics.RecordMembershipTableOperation(
                operation,
                outcome,
                System.Diagnostics.Stopwatch.GetElapsedTime(started));
        }
    }

    private static async ValueTask ObserveTableOperationAsync(
        string operation,
        Func<ValueTask> action)
    {
        await ObserveTableOperationAsync(
            operation,
            async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
    }
}
