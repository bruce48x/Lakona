using System.Diagnostics;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Server.Hosting;

internal sealed class ReplicatedClusterMembershipHostedService :
    BackgroundService,
    IClusterAuthorityListener,
    IClusterMembershipFrameHandler
{
    private readonly DistributedWorkAdmissionGate admissionGate;
    private readonly LakonaGameRuntimeOptions runtimeOptions;
    private readonly IClusterMembershipTransport transport;
    private readonly IReadOnlyList<IClusterRecoveryParticipant> recoveryParticipants;
    private readonly ClusterMembershipNodeOptions membershipOptions;
    private readonly IReadOnlyList<NodeEndpoint> contacts;
    private readonly ClusterFormationCoordinator formation;
    private readonly IServiceProvider? services;
    private readonly ILogger<ReplicatedClusterMembershipHostedService> logger;
    private ClusterMembershipNode? node;
    private ClusterAuthorityCoordinator? coordinator;
    private int transientFailureLogged;
    private int locallyUnavailable;
    private readonly SemaphoreSlim authorityGate = new(1, 1);
    private readonly SemaphoreSlim descriptorGate = new(1, 1);
    private readonly TaskCompletionSource activated = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public ReplicatedClusterMembershipHostedService(
        LakonaGameRuntimeOptions runtimeOptions,
        DistributedWorkAdmissionGate admissionGate,
        IEnumerable<IClusterRecoveryParticipant> recoveryParticipants,
        IClusterMembershipTransport transport,
        ClusterMembershipState membership,
        ClusterMembershipNodeOptions? membershipOptions = null,
        IServiceProvider? services = null,
        ILogger<ReplicatedClusterMembershipHostedService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeOptions);
        this.runtimeOptions = runtimeOptions;
        this.admissionGate = admissionGate
            ?? throw new ArgumentNullException(nameof(admissionGate));
        ArgumentNullException.ThrowIfNull(recoveryParticipants);
        this.recoveryParticipants = recoveryParticipants.ToArray();
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        ArgumentNullException.ThrowIfNull(membership);
        this.membershipOptions = membershipOptions ?? new ClusterMembershipNodeOptions();
        this.services = services;
        this.logger = logger ?? NullLogger<ReplicatedClusterMembershipHostedService>.Instance;
        var peers = runtimeOptions.Cluster.Peers
            .Select(static peer => new ClusterFormationPeer(
                new NodeId(peer.Id),
                new NodeEndpoint(peer.Endpoint)))
            .ToArray();
        contacts = peers.Select(static peer => peer.Endpoint).ToArray();
        formation = new ClusterFormationCoordinator(
            new NodeId(runtimeOptions.Node.Id),
            new NodeEndpoint(runtimeOptions.Cluster.Endpoint),
            peers,
            transport,
            this.membershipOptions,
            membership: membership);
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Starting replicated membership. NodeId={NodeId} Endpoint={Endpoint} ContactCount={ContactCount}",
            runtimeOptions.Node.Id,
            runtimeOptions.Cluster.Endpoint,
            contacts.Count);
        var membershipNode = await formation.FormOrJoinAsync(cancellationToken).ConfigureAwait(false);
        InitializeNode(membershipNode);
        LogMembershipState("Formation completed");

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        logger.LogDebug("Membership background supervisor started. NodeId={NodeId}", runtimeOptions.Node.Id);

        var execution = ExecuteTask ?? throw new InvalidOperationException(
            "The membership background supervisor did not start.");
        var completed = await Task.WhenAny(activated.Task, execution).WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (ReferenceEquals(completed, execution))
        {
            await execution.ConfigureAwait(false);
            throw new InvalidOperationException(
                "The membership supervisor exited before distributed work became active.");
        }

        await activated.Task.ConfigureAwait(false);
        LogMembershipState("Distributed work activated");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (node is null)
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (coordinator is not null)
        {
            await OnAuthorityLostAsync(cancellationToken).ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return node is null ? Task.CompletedTask : RunNodeAsync(stoppingToken);
    }

    public ValueTask<ClusterMembershipTransportFrame> HandleAsync(
        ClusterMembershipTransportFrame request,
        CancellationToken cancellationToken = default)
    {
        return formation.HandleAsync(request, cancellationToken);
    }

    public async ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken)
    {
        await authorityGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref locallyUnavailable) != 0)
            {
                await EnsureDistributedAdmissionClosedAsync(cancellationToken)
                    .ConfigureAwait(false);
                logger.LogWarning(
                    "Ignored quorum authority because the node is locally unavailable. NodeId={NodeId}",
                    runtimeOptions.Node.Id);
                return;
            }

            LogMembershipState("Quorum authority available");
            await RequireCoordinator().OnAuthorityAvailableAsync(cancellationToken)
                .ConfigureAwait(false);
            LogMembershipState("Quorum authority processed");
            if (admissionGate.IsOpen)
            {
                activated.TrySetResult();
                logger.LogDebug(
                    "Signaled hosted-service activation. NodeId={NodeId}",
                    runtimeOptions.Node.Id);
            }
        }
        finally
        {
            authorityGate.Release();
        }
    }

    public async ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken)
    {
        await authorityGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LogMembershipState("Quorum authority lost");
            await RequireCoordinator().OnAuthorityLostAsync(cancellationToken).ConfigureAwait(false);
            LogMembershipState("Quorum authority loss processed");
        }
        finally
        {
            authorityGate.Release();
        }
    }

    public void OnTransientFailure(Exception exception)
    {
        RequireCoordinator().OnTransientFailure(exception);
        var currentNode = node;
        var snapshot = currentNode?.Membership.Current;
        var localState = GetLocalState(currentNode, snapshot);
        if (Interlocked.Exchange(ref transientFailureLogged, 1) != 0)
        {
            logger.LogTrace(
                "Repeated transient membership failure. NodeId={NodeId} Cluster={Cluster} View={View} LocalState={LocalState} Failure={Failure}",
                runtimeOptions.Node.Id,
                snapshot?.Cluster.Value,
                snapshot?.View.Value,
                localState,
                $"{exception.GetType().FullName}: {exception.Message}");
            return;
        }

        logger.LogDebug(
            exception,
            "Transient membership failure. NodeId={NodeId} Cluster={Cluster} View={View} LocalState={LocalState} IsLeader={IsLeader} AdmissionOpen={AdmissionOpen}",
            runtimeOptions.Node.Id,
            snapshot?.Cluster.Value,
            snapshot?.View.Value,
            localState,
            currentNode?.IsLeader,
            admissionGate.IsOpen);
    }

    internal async ValueTask RefreshDescriptorAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfLocallyUnavailable();
        var currentNode = RequireNode();
        await descriptorGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Descriptor construction validates local configuration. Keep it outside the
            // retry loop so a permanent configuration error remains process-visible.
            var descriptor = CreateLocalReadyDescriptor(currentNode);
            await RetryTransientMembershipOperationAsync(
                "descriptor refresh",
                async attemptCancellation =>
                {
                    ThrowIfLocallyUnavailable();
                    ThrowIfRemovedFromMembership(currentNode);
                    if (currentNode.IsLeader)
                    {
                        await currentNode.CommitMemberReadyDescriptorAsync(
                            descriptor,
                            transport,
                            attemptCancellation).ConfigureAwait(false);
                        return;
                    }

                    await currentNode.RequestReadyAsync(
                        descriptor,
                        GetControlContacts(currentNode),
                        transport,
                        attemptCancellation).ConfigureAwait(false);
                },
                cancellationToken,
                membershipOptions.DescriptorRefreshRetryWindow).ConfigureAwait(false);
        }
        finally
        {
            descriptorGate.Release();
        }
    }

    internal async ValueTask MarkUnavailableAsync()
    {
        var firstTransition = Interlocked.Exchange(ref locallyUnavailable, 1) == 0;
        await descriptorGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await authorityGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await EnsureDistributedAdmissionClosedAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                authorityGate.Release();
            }
        }
        finally
        {
            descriptorGate.Release();
        }

        if (firstTransition)
        {
            logger.LogError(
                "Marked cluster node locally unavailable until process restart. NodeId={NodeId}",
                runtimeOptions.Node.Id);
        }
    }

    private ValueTask EnsureDistributedAdmissionClosedAsync(
        CancellationToken cancellationToken)
    {
        return RequireCoordinator().OnAuthorityLostAsync(cancellationToken);
    }

    private void ThrowIfLocallyUnavailable()
    {
        if (Volatile.Read(ref locallyUnavailable) != 0)
        {
            throw new ClusterAuthorityFencingException(
                "The cluster node is locally unavailable until process restart.");
        }
    }

    private ClusterMembershipNode RequireNode()
    {
        return node ?? throw new InvalidOperationException(
            "Cluster membership is unavailable while formation is incomplete.");
    }

    private ClusterAuthorityCoordinator RequireCoordinator()
    {
        return coordinator ?? throw new InvalidOperationException(
            "Replicated membership authority is not configured.");
    }

    private async Task RunNodeAsync(CancellationToken stoppingToken)
    {
        var currentNode = RequireNode();
        if (IsLocalState(ClusterMemberState.Joining))
        {
            // Give subsequently registered RPC hosted services an opportunity to begin listening.
            await Task.Delay(membershipOptions.MinimumRetryDelay, stoppingToken)
                .ConfigureAwait(false);
            await RetryTransientMembershipOperationAsync(
                "learner promotion",
                async attemptCancellation =>
                {
                    if (!IsLocalState(ClusterMemberState.Joining))
                    {
                        return;
                    }

                    var controlContacts = GetControlContacts(currentNode);
                    logger.LogTrace(
                        "Requesting learner promotion. NodeId={NodeId} ContactCount={ContactCount}",
                        runtimeOptions.Node.Id,
                        controlContacts.Count);
                    await currentNode.RequestPromotionAsync(
                            controlContacts,
                            transport,
                            attemptCancellation)
                        .ConfigureAwait(false);
                    LogMembershipState("Learner promotion request completed");
                },
                stoppingToken).ConfigureAwait(false);
        }

        await currentNode.RunAsync(this, transport, stoppingToken).ConfigureAwait(false);
    }

    private async ValueTask RetryTransientMembershipOperationAsync(
        string operation,
        Func<CancellationToken, ValueTask> attempt,
        CancellationToken cancellationToken,
        TimeSpan? retryWindow = null)
    {
        var retryCap = membershipOptions.MinimumRetryDelay;
        var startedAt = Stopwatch.GetTimestamp();
        Exception? lastFailure = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lastFailure is not null
                && retryWindow is { } activeWindow
                && Stopwatch.GetElapsedTime(startedAt) >= activeWindow)
            {
                throw CreateRetryTimeout(operation, activeWindow, lastFailure);
            }

            try
            {
                await attempt(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TerminalMembershipException)
            {
                throw;
            }
            catch (ClusterAuthorityFencingException)
            {
                throw;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastFailure = exception;
                logger.LogTrace(
                    "Transient membership operation failed and will retry. NodeId={NodeId} Operation={Operation} RetryCapMs={RetryCapMs}",
                    runtimeOptions.Node.Id,
                    operation,
                    retryCap.TotalMilliseconds);
                OnTransientFailure(exception);

                if (retryWindow is { } window
                    && Stopwatch.GetElapsedTime(startedAt) >= window)
                {
                    throw CreateRetryTimeout(operation, window, exception);
                }
            }

            var delay = ApplyFullJitter(retryCap);
            if (retryWindow is { } retryBudget)
            {
                var remaining = retryBudget - Stopwatch.GetElapsedTime(startedAt);
                if (remaining <= TimeSpan.Zero)
                {
                    throw CreateRetryTimeout(operation, retryBudget, lastFailure!);
                }

                delay = delay < remaining ? delay : remaining;
            }

            await Task.Delay(delay, cancellationToken)
                .ConfigureAwait(false);
            retryCap = DoubleCapped(retryCap, membershipOptions.MaximumRetryDelay);
        }
    }

    private static TimeoutException CreateRetryTimeout(
        string operation,
        TimeSpan retryWindow,
        Exception lastFailure)
    {
        return new TimeoutException(
            $"Membership {operation} did not converge within {retryWindow}.",
            lastFailure);
    }

    private static TimeSpan ApplyFullJitter(TimeSpan cap)
    {
        var sampledTicks = (long)(cap.Ticks * Random.Shared.NextDouble());
        var minimumTicks = Math.Min(cap.Ticks, TimeSpan.TicksPerMillisecond);
        return TimeSpan.FromTicks(Math.Max(minimumTicks, sampledTicks));
    }

    private static TimeSpan DoubleCapped(TimeSpan current, TimeSpan maximum)
    {
        if (current >= maximum || current.Ticks > maximum.Ticks / 2)
        {
            return maximum;
        }

        var doubled = TimeSpan.FromTicks(current.Ticks * 2);
        return doubled < maximum ? doubled : maximum;
    }

    private bool IsLocalState(ClusterMemberState state)
    {
        var currentNode = RequireNode();
        return currentNode.Membership.Current.TryGetMember(currentNode.Local, out var member)
            && member is not null
            && member.State == state;
    }

    private static void ThrowIfRemovedFromMembership(ClusterMembershipNode membershipNode)
    {
        if (!membershipNode.Membership.Current.TryGetMember(membershipNode.Local, out _))
        {
            throw new ClusterAuthorityFencingException(
                "The exact local node incarnation has been removed from membership.");
        }
    }

    private void InitializeNode(ClusterMembershipNode membershipNode)
    {
        node = membershipNode;
        coordinator = new ClusterAuthorityCoordinator(
            membershipNode.Local,
            membershipNode.Membership,
            admissionGate,
            new ClusterRecoveryBarrier(recoveryParticipants),
            new RecoveryCompletion(
                membershipNode,
                transport,
                () => GetControlContacts(membershipNode),
                () => CreateLocalReadyDescriptor(membershipNode)),
            TimeSpan.FromSeconds(30));
    }

    private void LogMembershipState(string transition)
    {
        var currentNode = node;
        var snapshot = currentNode?.Membership.Current;
        logger.LogDebug(
            "{Transition}. NodeId={NodeId} Cluster={Cluster} View={View} LocalState={LocalState} MemberCount={MemberCount} IsLeader={IsLeader} AdmissionOpen={AdmissionOpen}",
            transition,
            runtimeOptions.Node.Id,
            snapshot?.Cluster.Value,
            snapshot?.View.Value,
            GetLocalState(currentNode, snapshot),
            snapshot?.Members.Count,
            currentNode?.IsLeader,
            admissionGate.IsOpen);
    }

    private static ClusterMemberState? GetLocalState(
        ClusterMembershipNode? currentNode,
        ClusterMembershipSnapshot? snapshot)
    {
        return currentNode is not null
            && snapshot is not null
            && snapshot.TryGetMember(currentNode.Local, out var member)
            && member is not null
                ? member.State
                : null;
    }

    private ClusterMember CreateLocalReadyDescriptor(ClusterMembershipNode membershipNode)
    {
        if (!membershipNode.Membership.Current.TryGetMember(
                membershipNode.Local,
                out var current)
            || current is null)
        {
            throw new ClusterAuthorityFencingException(
                "The exact local membership descriptor is unavailable.");
        }

        var actorHosts = new List<NodeActorHostDescriptor>();
        var catalog = services?.GetService<ActorHostDescriptorCatalog>();
        var hotfixHosts = services?.GetService<IHotfixManager>()?.Current.ActorHosts
            .ToDictionary(static descriptor => descriptor.Actor, StringComparer.OrdinalIgnoreCase);
        foreach (var actor in runtimeOptions.ActorHosts)
        {
            if (catalog is not null && catalog.TryGet(actor, out var descriptor))
            {
                actorHosts.Add(new NodeActorHostDescriptor(
                    descriptor.Actor,
                    descriptor.PolicyHash,
                    descriptor.BuildTag,
                    descriptor.Metadata));
            }
            else if (hotfixHosts is not null && hotfixHosts.TryGetValue(actor, out var hotfix))
            {
                actorHosts.Add(new NodeActorHostDescriptor(
                    hotfix.Actor,
                    hotfix.PolicyHash,
                    hotfix.BuildTag,
                    hotfix.Metadata));
            }
            else
            {
                throw new InvalidOperationException(
                    $"Lakona:ActorHosts contains unknown actor host '{actor}'.");
            }
        }

        return new ClusterMember(
            current.Reference,
            ClusterMemberState.Recovering,
            current.ClusterEndpoint,
            isVoter: true,
            current.Labels,
            actorHosts,
            services?.GetService<StartupActorDescriptorCatalog>()?.Snapshot());
    }

    private IReadOnlyList<NodeEndpoint> GetControlContacts(ClusterMembershipNode membershipNode)
    {
        return membershipNode.Membership.Current.Members
            .Where(member => member.Reference != membershipNode.Local)
            .Select(static member => member.ClusterEndpoint)
            .Concat(contacts)
            .DistinctBy(static endpoint => endpoint.Address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed class RecoveryCompletion : IClusterRecoveryCompletion
    {
        private readonly ClusterMembershipNode node;
        private readonly IClusterMembershipTransport transport;
        private readonly Func<IReadOnlyList<NodeEndpoint>> contactFactory;
        private readonly Func<ClusterMember> descriptorFactory;

        public RecoveryCompletion(
            ClusterMembershipNode node,
            IClusterMembershipTransport transport,
            Func<IReadOnlyList<NodeEndpoint>> contactFactory,
            Func<ClusterMember> descriptorFactory)
        {
            this.node = node;
            this.transport = transport;
            this.contactFactory = contactFactory;
            this.descriptorFactory = descriptorFactory;
        }

        public async ValueTask CommitReadyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = descriptorFactory();

            if (node.IsLeader)
            {
                await node.CommitMemberReadyDescriptorAsync(
                        descriptor,
                        transport,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await node.RequestReadyAsync(
                    descriptor,
                    contactFactory(),
                    transport,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
