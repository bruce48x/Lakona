using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    private readonly IServiceProvider? services;
    private ClusterMembershipNode? node;
    private ClusterAuthorityCoordinator? coordinator;
    private readonly SemaphoreSlim descriptorGate = new(1, 1);
    private readonly TaskCompletionSource activated = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public ReplicatedClusterMembershipHostedService(
        LakonaGameRuntimeOptions runtimeOptions,
        DistributedWorkAdmissionGate admissionGate,
        IEnumerable<IClusterRecoveryParticipant> recoveryParticipants,
        ClusterMembershipNodeOptions? membershipOptions = null)
        : this(
            runtimeOptions,
            admissionGate,
            recoveryParticipants,
            new UnavailableMembershipTransport(),
            membershipOptions,
            null)
    {
    }

    public ReplicatedClusterMembershipHostedService(
        LakonaGameRuntimeOptions runtimeOptions,
        DistributedWorkAdmissionGate admissionGate,
        IEnumerable<IClusterRecoveryParticipant> recoveryParticipants,
        IClusterMembershipTransport transport,
        ClusterMembershipNodeOptions? membershipOptions = null,
        IServiceProvider? services = null)
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
        contacts = runtimeOptions.Cluster.Seeds
            .Select(static address => new NodeEndpoint(address))
            .ToArray();

        if (runtimeOptions.Cluster.BootstrapNewCluster)
        {
            if (contacts.Count != 0)
            {
                throw new InvalidOperationException(
                    "A fresh cluster bootstrap cannot also specify discovery contacts.");
            }

            InitializeNode(ClusterMembershipNode.BootstrapNewCluster(
                new NodeId(runtimeOptions.Node.Id),
                new NodeEndpoint(runtimeOptions.Cluster.Endpoint),
                this.membershipOptions));
        }
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
        if (node is null && contacts.Count > 0)
        {
            InitializeNode(await JoinExistingClusterWithRetryAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        if (node is null)
        {
            return;
        }

        if (IsLocalState(ClusterMemberState.Joining))
        {
            // The learner must let the host continue so its inbound membership RPC can start.
            return;
        }

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

    private async Task<ClusterMembershipNode> JoinExistingClusterWithRetryAsync(
        CancellationToken cancellationToken)
    {
        var startedAt = TimeProvider.System.GetTimestamp();
        var retry = membershipOptions.MinimumRetryDelay;
        var failures = new List<Exception>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await ClusterMembershipNode.JoinExistingClusterAsync(
                    new NodeId(runtimeOptions.Node.Id),
                    new NodeEndpoint(runtimeOptions.Cluster.Endpoint),
                    contacts,
                    transport,
                    membershipOptions,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (AggregateException exception)
            {
                failures.Add(exception);
                var elapsed = TimeProvider.System.GetElapsedTime(startedAt);
                if (elapsed >= membershipOptions.JoinRetryWindow)
                {
                    throw new AggregateException(
                        $"No discovery contact admitted the node within the configured " +
                        $"join retry window ({membershipOptions.JoinRetryWindow}). " +
                        "The node did not bootstrap a new cluster.",
                        failures);
                }

                var remaining = membershipOptions.JoinRetryWindow - elapsed;
                await Task.Delay(retry <= remaining ? retry : remaining, cancellationToken)
                    .ConfigureAwait(false);
                retry = retry >= membershipOptions.MaximumRetryDelay
                    ? membershipOptions.MaximumRetryDelay
                    : TimeSpan.FromTicks(Math.Min(
                        membershipOptions.MaximumRetryDelay.Ticks,
                        retry.Ticks * 2));
            }
        }
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
        return RequireNode().HandleTransportRequestAsync(request, transport, cancellationToken);
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
        finally
        {
            descriptorGate.Release();
        }
    }

    private ClusterMembershipNode RequireNode()
    {
        return node ?? throw new InvalidOperationException(
            "Replicated membership is disabled because BootstrapNewCluster is false and joining is not configured.");
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
                    await Task.Delay(retry, stoppingToken).ConfigureAwait(false);
                    retry = retry >= membershipOptions.MaximumRetryDelay
                        ? membershipOptions.MaximumRetryDelay
                        : TimeSpan.FromTicks(Math.Min(
                            membershipOptions.MaximumRetryDelay.Ticks,
                            retry.Ticks * 2));
                }
            }
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
                () => CreateLocalReadyDescriptor(membershipNode)),
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

        public RecoveryCompletion(
            ClusterMembershipNode node,
            IClusterMembershipTransport transport,
            IReadOnlyList<NodeEndpoint> contacts,
            Func<ClusterMember> descriptorFactory)
        {
            this.node = node;
            this.transport = transport;
            this.contacts = contacts;
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

            await node.RequestReadyAsync(descriptor, contacts, transport, cancellationToken)
                .ConfigureAwait(false);
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
