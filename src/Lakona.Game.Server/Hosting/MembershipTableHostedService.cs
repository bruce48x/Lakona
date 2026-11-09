using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc;
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
    private readonly TimeProvider timeProvider;
    private readonly Dictionary<NodeReference, int> failedProbes = [];
    private uint probeRound;
    private readonly object shutdownGate = new();
    private Task? beginStoppingTask;
    private readonly MembershipTableSafetyWindow safetyWindow;
    private readonly CancellationTokenSource safetyCancellation = new();
    private readonly CancellationTokenSource peerCancellation = new();
    private readonly CancellationToken safetyToken;
    private readonly CancellationToken peerToken;
    private DateTimeOffset nextDefunctCleanup;
    private DateTimeOffset nextTableRefresh;
    private DateTimeOffset nextIAmAlive;

    public MembershipTableHostedService(
        LakonaGameRuntimeOptions runtime,
        MembershipTableManager manager,
        IClusterMembership membership,
        IMembershipProbeTransport probes,
        DistributedWorkAdmissionGate admissionGate,
        IEnumerable<IClusterRecoveryParticipant> recoveryParticipants,
        IServiceProvider services,
        ILogger<MembershipTableHostedService> logger,
        IHostApplicationLifetime? lifetime = null,
        TimeProvider? timeProvider = null)
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
        this.timeProvider = timeProvider ?? TimeProvider.System;
        safetyToken = safetyCancellation.Token;
        peerToken = peerCancellation.Token;
        safetyWindow = new MembershipTableSafetyWindow(this.timeProvider,
            TimeSpan.FromSeconds(runtime.Cluster.Membership.IAmAliveSeconds), admissionGate, OnSafetyWindowExpired);
        admissionGate.SafetyWindow = safetyWindow;
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
        var descriptor = CreateDescriptor(includeCapabilities: false);
        await RetryAsync(
            token => ObserveTableOperationAsync(
                "activate",
                () => manager.ActivateAsync(descriptor.Labels, descriptor.ActorHosts, descriptor.StartupActors, token)),
            "activate membership",
            cancellationToken).ConfigureAwait(false);
        safetyWindow.Start();
        try
        {
            var now = timeProvider.GetUtcNow();
            nextDefunctCleanup = now.AddSeconds(runtime.Cluster.Membership.DefunctEntryCleanupIntervalSeconds);
            nextTableRefresh = now.AddSeconds(runtime.Cluster.Membership.TableRefreshSeconds);
            nextIAmAlive = now.AddSeconds(runtime.Cluster.Membership.IAmAliveSeconds);
            await base.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            admissionGate.Fence();
            safetyWindow.Dispose();
            throw;
        }
        ClusterDiagnostics.RecordMembershipLifecycle("active");
        logger.LogInformation(
            "Cluster node active. NodeId={NodeId} Incarnation={Incarnation}",
            local.Node.Value,
            local.Incarnation.Value);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        admissionGate.Fence();
        safetyWindow.Dispose();
        using var shutdownBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, safetyToken);
        await BeginStoppingAsync(shutdownBudget.Token).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        // BackgroundService's stop deadline can expire before ExecuteTask finishes.
        // Do not report a clean stop or publish Dead while a worker is still running.
        if (ExecuteTask is { } execution) await execution.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ObserveTableOperationAsync("dead", () => manager.MarkDeadAsync(shutdownBudget.Token)).ConfigureAwait(false);
            ClusterDiagnostics.RecordMembershipLifecycle("dead");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not publish graceful cluster shutdown.");
        }
    }

    internal async ValueTask RefreshDescriptorAsync(CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, safetyToken);
        var descriptor = CreateDescriptor(includeCapabilities: admissionGate.IsOpen);
        await manager.ActivateAsync(descriptor.Labels, descriptor.ActorHosts, descriptor.StartupActors, budget.Token).ConfigureAwait(false);
        if (!safetyWindow.Check()) throw new ClusterMembershipFencedException("Membership safety window expired during descriptor refresh.");
        budget.Token.ThrowIfCancellationRequested();
    }

    internal async ValueTask MarkUnavailableAsync()
    {
        await admissionGate.CloseAndDrainAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
        await BeginStoppingAsync(CancellationToken.None).ConfigureAwait(false);
    }

    internal Task BeginStoppingAsync(CancellationToken cancellationToken)
    {
        lock (shutdownGate)
        {
            return beginStoppingTask ??= BeginStoppingCoreAsync(cancellationToken);
        }
    }

    private async Task BeginStoppingCoreAsync(CancellationToken cancellationToken)
    {
        CancelPeerOperations();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, safetyToken);
        try
        {
            await ObserveTableOperationAsync("stopping", () => manager.MarkStoppingAsync(budget.Token)).ConfigureAwait(false);
            services.GetService<LocalActorNodeIdentity>()?.ConfirmStopping(manager.Local);
            ClusterDiagnostics.RecordMembershipLifecycle("stopping");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not publish the cluster node's Stopping state.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The renewable deadline cancels this token independently of any awaited operation.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, safetyToken);
        using var peerBudget = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, peerToken);
        var tableTask = RunWorkerAsync(MaintainTableAsync, budget.Token);
        var peerTask = RunWorkerAsync(CommunicateWithPeersAsync, peerBudget.Token);
        try
        {
            var completed = await Task.WhenAny(tableTask, peerTask).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
            // Stopping withdraws this node before Actor cleanup. Table maintenance
            // must remain available during that phase, even though probes have ended.
            if (completed == peerTask && peerToken.IsCancellationRequested && !budget.IsCancellationRequested)
                await tableTask.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (!budget.IsCancellationRequested)
                {
                    admissionGate.Fence();
                    lifetime?.StopApplication();
                }
            }
            finally
            {
                try { budget.Cancel(); }
                finally { await Task.WhenAll(tableTask, peerTask).ConfigureAwait(false); }
            }
        }
    }

    private async Task RunWorkerAsync(Func<CancellationToken, Task> worker, CancellationToken token)
    {
        try { await worker(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ClusterMembershipFencedException exception)
        {
            logger.LogCritical(exception, "This node incarnation is Dead in the membership table and will stop.");
            ClusterDiagnostics.RecordMembershipLifecycle("fenced");
            admissionGate.Fence();
            try { lifetime?.StopApplication(); }
            finally { CancelMembershipOperations(); }
        }
    }

    private async Task MaintainTableAsync(CancellationToken stoppingToken)
    {
        var options = runtime.Cluster.Membership;
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var delay = Earliest(nextTableRefresh, nextIAmAlive, nextDefunctCleanup) - now;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
                now = timeProvider.GetUtcNow();
            }

            var refreshDue = now >= nextTableRefresh;
            var iAmAliveDue = now >= nextIAmAlive;
            var cleanupDue = now >= nextDefunctCleanup;
            if (refreshDue) nextTableRefresh = now.AddSeconds(options.TableRefreshSeconds);
            if (iAmAliveDue) nextIAmAlive = now.AddSeconds(options.IAmAliveSeconds);
            if (cleanupDue) nextDefunctCleanup = now.AddSeconds(options.DefunctEntryCleanupIntervalSeconds);

            try
            {
                if (refreshDue)
                {
                    await ObserveTableOperationAsync("refresh", () => manager.RefreshAsync(stoppingToken)).ConfigureAwait(false);
                    stoppingToken.ThrowIfCancellationRequested();
                    if (!safetyWindow.Renew()) return;
                }

                if (iAmAliveDue)
                {
                    await ObserveTableOperationAsync("heartbeat", () => manager.UpdateIAmAliveAsync(stoppingToken)).ConfigureAwait(false);
                    stoppingToken.ThrowIfCancellationRequested();
                    if (!safetyWindow.Renew()) return;
                }

                if (cleanupDue && ShouldRunDefunctCleanup(membership.Current, manager.Local))
                {
                    var removed = await ObserveTableOperationAsync(
                        "cleanup",
                        () => manager.CleanupDefunctAsync(
                            TimeSpan.FromSeconds(options.DefunctEntryRetentionSeconds),
                            options.DefunctEntryCleanupBatchSize,
                            stoppingToken)).ConfigureAwait(false);
                    stoppingToken.ThrowIfCancellationRequested();
                    if (!safetyWindow.Renew()) return;
                    if (removed > 0) logger.LogInformation("Removed {Count} expired defunct membership rows.", removed);
                }
            }
            catch (ClusterMembershipFencedException) { throw; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (!safetyWindow.Check()) return;
                logger.LogWarning(exception, "Membership refresh failed; continuing with the last committed snapshot.");
            }
        }
    }

    private async Task CommunicateWithPeersAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await GossipMembershipAsync(token).ConfigureAwait(false);
        var options = runtime.Cluster.Membership;
        while (!token.IsCancellationRequested)
        {
            try { await ProbeTargetsAsync(options, token).ConfigureAwait(false); }
            catch (ClusterMembershipFencedException) { throw; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Membership peer probe failed; retrying on the next cycle.");
            }

            // Schedule from completion: slow rounds neither overlap nor accumulate.
            await Task.Delay(TimeSpan.FromSeconds(options.ProbeIntervalSeconds), timeProvider, token).ConfigureAwait(false);
        }
    }

    private void OnSafetyWindowExpired()
    {
        logger.LogCritical("Membership table contact exceeded the safety window; distributed-work admission is fenced.");
        ClusterDiagnostics.RecordMembershipLifecycle("table_unavailable");
        try { lifetime?.StopApplication(); }
        finally { CancelMembershipOperations(); }
    }

    private void CancelMembershipOperations()
    {
        try { safetyCancellation.Cancel(); }
        catch (ObjectDisposedException) { } // Concurrent host disposal already ended this lifetime.
        catch (AggregateException exception) { logger.LogWarning(exception, "Membership cancellation callback failed."); }
    }

    private void CancelPeerOperations()
    {
        try { peerCancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (AggregateException exception) { logger.LogWarning(exception, "Membership peer cancellation callback failed."); }
    }

    public override void Dispose()
    {
        admissionGate.Fence();
        safetyWindow.Dispose();
        CancelMembershipOperations();
        CancelPeerOperations();
        base.Dispose();
        safetyCancellation.Dispose();
        peerCancellation.Dispose();
    }

    internal ValueTask RunProbeCycleAsync(
        LakonaGameMembershipOptions options,
        CancellationToken cancellationToken = default) =>
        ProbeTargetsAsync(options, cancellationToken);

    internal ValueTask ValidateStartupConnectivityAsync(CancellationToken cancellationToken = default) =>
        ValidateConnectivityAsync(manager.Local, runtime.Cluster.Membership, cancellationToken);

    internal static bool ShouldRunDefunctCleanup(
        ClusterMembershipSnapshot snapshot,
        NodeReference local)
    {
        var owner = snapshot.Members
            .Where(static member => member.State == ClusterMemberState.Active)
            .Select(static member => member.Reference)
            .OrderBy(static reference => reference.Node.Value, StringComparer.Ordinal)
            .ThenBy(static reference => reference.Incarnation.Value)
            .FirstOrDefault();
        return owner == local;
    }

    private async ValueTask ProbeTargetsAsync(LakonaGameMembershipOptions options, CancellationToken cancellationToken)
    {
        var round = probeRound++;
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
            cancellationToken.ThrowIfCancellationRequested();
            var result = await probes.ProbeAsync(local, target, target.ClusterEndpoint, false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result == MembershipProbeStatus.Failed)
                result = await TryIndirectProbeAsync(snapshot, local, target, options.IndirectProbes, round, cancellationToken)
                    .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result == MembershipProbeStatus.Succeeded)
            {
                failedProbes.Remove(target.Reference);
                continue;
            }
            // Unknown neither clears evidence nor adds/refreshes a suspicion vote.
            if (result != MembershipProbeStatus.Failed) continue;

            var failures = failedProbes.GetValueOrDefault(target.Reference) + 1;
            failedProbes[target.Reference] = failures;
            if (failures < options.FailedProbesBeforeSuspect) continue;
            var declaredDead = await manager.TrySuspectAsync(
                target.Reference,
                options.VotesForDeath,
                TimeSpan.FromSeconds(options.SuspectVoteLifetimeSeconds),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await probes.GossipAsync(manager.Local, member.ClusterEndpoint, snapshot.View, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                // A transport's own RPC deadline is a failed best-effort gossip,
                // not cancellation of the membership worker's lifetime.
                logger.LogDebug(exception, "Membership gossip to {NodeId} failed.", member.Reference.Node.Value);
            }
        }
    }

    private async ValueTask<MembershipProbeStatus> TryIndirectProbeAsync(
        ClusterMembershipSnapshot snapshot,
        NodeReference local,
        ClusterMember target,
        int helperCount,
        uint round,
        CancellationToken cancellationToken)
    {
        var helpers = snapshot.Members
            .Where(member => member.State == ClusterMemberState.Active
                && member.Reference != local
                && member.Reference != target.Reference)
            .ToArray();
        // A two-node cluster must still progress on direct evidence. Unreachable
        // helpers are different: they exist, but provide no corroboration.
        if (helpers.Length == 0) return MembershipProbeStatus.Failed;
        var result = MembershipProbeStatus.Unknown;
        var start = (int)(round % (uint)helpers.Length);
        for (var index = 0; index < Math.Min(helperCount, helpers.Length); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var helper = helpers[(start + index) % helpers.Length];
            var reply = await probes.ProbeAsync(local, target, helper.ClusterEndpoint, true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (reply == MembershipProbeStatus.Succeeded) return reply;
            if (reply == MembershipProbeStatus.Failed) result = reply;
        }

        return result;
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
            var outgoing = await probes.ProbeAsync(local, projected, active.ClusterEndpoint, false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var incoming = await probes.ProbeAsync(local, localMember, active.ClusterEndpoint, true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (outgoing != MembershipProbeStatus.Succeeded || incoming != MembershipProbeStatus.Succeeded)
            {
                var declaredDead = outgoing == MembershipProbeStatus.Failed && await manager.TryMarkDefunctAsync(
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

    private ClusterMember CreateDescriptor(bool includeCapabilities = true)
    {
        var current = membership.Current.Members.Single(member => member.Reference == manager.Local);
        var actorHosts = new List<NodeActorHostDescriptor>();
        var catalog = services.GetService<ActorHostDescriptorCatalog>();
        var hotfixHosts = services.GetService<IHotfixManager>()?.Current.ActorHosts
            .ToDictionary(static descriptor => descriptor.Actor, StringComparer.OrdinalIgnoreCase);
        var roleCatalog = services.GetService<NodeRoleCatalog>();
        var localActors = roleCatalog?.LocalActorNames
            ?? hotfixHosts?.Keys.OrderBy(static actor => actor, StringComparer.Ordinal).ToArray()
            ?? [];
        foreach (var actor in includeCapabilities ? localActors : [])
        {
            if (catalog is not null && catalog.TryGet(actor, out var descriptor))
            {
                actorHosts.Add(new NodeActorHostDescriptor(descriptor.Actor, descriptor.PolicyHash, descriptor.HotfixVersion, descriptor.Metadata));
            }
            else if (hotfixHosts is not null && hotfixHosts.TryGetValue(actor, out var hotfix))
            {
                actorHosts.Add(new NodeActorHostDescriptor(hotfix.Actor, hotfix.PolicyHash, hotfix.HotfixVersion, hotfix.Metadata));
            }
            else
            {
                throw new InvalidOperationException($"Local Actor host '{actor}' has no descriptor.");
            }
        }

        return new ClusterMember(
            current.Reference,
            ClusterMemberState.Joining,
            current.ClusterEndpoint,
            current.Labels,
            actorHosts,
            includeCapabilities
                ? services.GetService<StartupActorDescriptorCatalog>()?.Snapshot()
                : []);
    }

    private async ValueTask<T> RetryAsync<T>(Func<CancellationToken, ValueTask<T>> operation, string name, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(200);
        while (true)
        {
            try { return await operation(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (ClusterMembershipFencedException) { throw; }
            catch (MembershipSchemaException) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Cluster operation {Operation} failed; retrying.", name);
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
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
        DateTimeOffset third)
    {
        var firstPair = first <= second ? first : second;
        return firstPair <= third ? firstPair : third;
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
