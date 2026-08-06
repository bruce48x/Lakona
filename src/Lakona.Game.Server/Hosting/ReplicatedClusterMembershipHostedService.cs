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
    IClusterMembership,
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
    private readonly ILogger logger;
    private ClusterMembershipNode? node;
    private ClusterAuthorityCoordinator? coordinator;
    private readonly SemaphoreSlim descriptorGate = new(1, 1);
    private readonly TaskCompletionSource activated = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public ReplicatedClusterMembershipHostedService(
        LakonaGameRuntimeOptions runtimeOptions,
        DistributedWorkAdmissionGate admissionGate,
        IEnumerable<IClusterRecoveryParticipant> recoveryParticipants,
        ClusterMembershipNodeOptions? membershipOptions = null,
        ILoggerFactory? loggerFactory = null)
        : this(
            runtimeOptions,
            admissionGate,
            recoveryParticipants,
            new UnavailableMembershipTransport(),
            membershipOptions,
            null,
            loggerFactory)
    {
    }

    public ReplicatedClusterMembershipHostedService(
        LakonaGameRuntimeOptions runtimeOptions,
        DistributedWorkAdmissionGate admissionGate,
        IEnumerable<IClusterRecoveryParticipant> recoveryParticipants,
        IClusterMembershipTransport transport,
        ClusterMembershipNodeOptions? membershipOptions = null,
        IServiceProvider? services = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeOptions);
        this.runtimeOptions = runtimeOptions;
        this.admissionGate = admissionGate
            ?? throw new ArgumentNullException(nameof(admissionGate));
        ArgumentNullException.ThrowIfNull(recoveryParticipants);
        this.recoveryParticipants = recoveryParticipants.ToArray();
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.membershipOptions = membershipOptions ?? new ClusterMembershipNodeOptions();
        this.services = services;
        logger = loggerFactory?.CreateLogger<ReplicatedClusterMembershipHostedService>()
            ?? NullLogger<ReplicatedClusterMembershipHostedService>.Instance;
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
            loggerFactory: loggerFactory);
    }

    ClusterMembershipSnapshot IClusterMembership.Current => RequireNode().Membership.Current;

    ValueTask<ClusterMembershipSnapshot> IClusterMembership.WaitForChangeAsync(
        MembershipViewId after,
        CancellationToken cancellationToken)
    {
        return RequireNode().Membership.WaitForChangeAsync(after, cancellationToken);
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var membershipNode = await formation.FormOrJoinAsync(cancellationToken).ConfigureAwait(false);
        InitializeNode(membershipNode);
        logger.LogInformation(
            "Cluster membership established for node {Node} in cluster {Cluster} view {View} with {MemberCount} member(s).",
            membershipNode.Local.Node.Value,
            membershipNode.Membership.Current.Cluster,
            membershipNode.Membership.Current.View,
            membershipNode.Membership.Current.Members.Count);

        await base.StartAsync(cancellationToken).ConfigureAwait(false);

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
            await coordinator.OnAuthorityLostAsync(cancellationToken).ConfigureAwait(false);
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
        await RequireCoordinator().OnAuthorityAvailableAsync(cancellationToken)
            .ConfigureAwait(false);
        if (admissionGate.IsOpen)
        {
            activated.TrySetResult();
        }
    }

    public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken)
    {
        return RequireCoordinator().OnAuthorityLostAsync(cancellationToken);
    }

    public void OnTransientFailure(Exception exception)
    {
        RequireCoordinator().OnTransientFailure(exception);
    }

    internal async ValueTask RefreshDescriptorAsync(
        CancellationToken cancellationToken = default)
    {
        var currentNode = RequireNode();
        await descriptorGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var descriptor = CreateLocalReadyDescriptor(currentNode);
            if (currentNode.IsLeader)
            {
                await currentNode.CommitMemberReadyDescriptorAsync(
                    descriptor,
                    transport,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            await currentNode.RequestReadyAsync(
                descriptor,
                GetControlContacts(currentNode),
                transport,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Membership ready descriptor refresh failed for node {Node}.",
                currentNode.Local.Node.Value);
            throw;
        }
        finally
        {
            descriptorGate.Release();
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
            var retry = membershipOptions.MinimumRetryDelay;
            while (IsLocalState(ClusterMemberState.Joining))
            {
                try
                {
                    await currentNode.RequestPromotionAsync(contacts, transport, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    OnTransientFailure(exception);
                    logger.LogWarning(
                        exception,
                        "Membership promotion for node {Node} failed; retrying in {RetryDelay}.",
                        currentNode.Local.Node.Value,
                        retry);
                    await Task.Delay(retry, stoppingToken).ConfigureAwait(false);
                    retry = retry >= membershipOptions.MaximumRetryDelay
                        ? membershipOptions.MaximumRetryDelay
                        : TimeSpan.FromTicks(Math.Min(
                            membershipOptions.MaximumRetryDelay.Ticks,
                            retry.Ticks * 2));
                }
            }

            logger.LogInformation(
                "Membership promotion completed for node {Node}.",
                currentNode.Local.Node.Value);
        }

        await currentNode.RunAsync(this, transport, stoppingToken).ConfigureAwait(false);
    }

    private bool IsLocalState(ClusterMemberState state)
    {
        var currentNode = RequireNode();
        return currentNode.Membership.Current.TryGetMember(currentNode.Local, out var member)
            && member is not null
            && member.State == state;
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
                contacts,
                () => CreateLocalReadyDescriptor(membershipNode),
                logger),
            TimeSpan.FromSeconds(30));
    }

    private ClusterMember CreateLocalReadyDescriptor(ClusterMembershipNode membershipNode)
    {
        if (!membershipNode.Membership.Current.TryGetMember(
                membershipNode.Local,
                out var current)
            || current is null)
        {
            throw new InvalidOperationException("The exact local membership descriptor is unavailable.");
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
        private readonly IReadOnlyList<NodeEndpoint> contacts;
        private readonly Func<ClusterMember> descriptorFactory;
        private readonly ILogger logger;

        public RecoveryCompletion(
            ClusterMembershipNode node,
            IClusterMembershipTransport transport,
            IReadOnlyList<NodeEndpoint> contacts,
            Func<ClusterMember> descriptorFactory,
            ILogger logger)
        {
            this.node = node;
            this.transport = transport;
            this.contacts = contacts;
            this.descriptorFactory = descriptorFactory;
            this.logger = logger;
        }

        public async ValueTask CommitReadyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = descriptorFactory();

            try
            {
                if (node.IsLeader)
                {
                    await node.CommitMemberReadyDescriptorAsync(
                            descriptor,
                            transport,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                await node.RequestReadyAsync(descriptor, contacts, transport, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Membership recovery ready commit failed for node {Node}.",
                    node.Local.Node.Value);
                throw;
            }
        }
    }

    private sealed class UnavailableMembershipTransport : IClusterMembershipTransport
    {
        public ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "A membership transport is required for a multi-node cluster.");
        }
    }
}
