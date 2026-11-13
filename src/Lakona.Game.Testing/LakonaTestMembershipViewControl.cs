using Lakona.Game.Cluster;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Testing;

/// <summary>
/// Deliberately holds one TestCluster node on its current Membership view so
/// tests can exercise deterministic view-propagation races.
/// </summary>
public sealed class LakonaTestMembershipViewControl
{
    private readonly object gate = new();
    private readonly Dictionary<string, NodeViewGate> nodes = new(StringComparer.Ordinal);

    /// <summary>Holds <paramref name="nodeId"/> on the Membership view it currently sees.</summary>
    public void Pause(string nodeId) => GetNode(nodeId).Pause();

    /// <summary>Releases a paused node so it can observe the latest committed view.</summary>
    public void Resume(string nodeId) => GetNode(nodeId).Resume();

    /// <summary>Waits until the node has a newer committed view hidden behind its pause.</summary>
    public Task WaitUntilBehindAsync(
        string nodeId,
        CancellationToken cancellationToken = default) =>
        GetNode(nodeId).WaitUntilBehindAsync(cancellationToken);

    /// <summary>
    /// Returns the cumulative number of waits that reached a newer Membership view
    /// hidden by the current or most recent pause. Canceled waits remain counted,
    /// and Resume preserves the count; the next Pause resets it to zero.
    /// This is an arrival count, not the number of currently blocked waiters.
    /// </summary>
    public int GetBlockedWaiterCount(string nodeId) => GetNode(nodeId).WaiterArrivalCount;

    /// <summary>
    /// While the node is paused, waits until at least <paramref name="minimum"/>
    /// waits have reached a newer Membership view hidden by this pause, including
    /// waits that subsequently canceled. Each Pause starts the count at zero.
    /// </summary>
    public Task WaitForBlockedWaiterCountAsync(
        string nodeId,
        int minimum,
        CancellationToken cancellationToken = default) =>
        GetNode(nodeId).WaitForWaiterArrivalCountAsync(minimum, cancellationToken);

    internal void ConfigureNode(IServiceCollection services, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var descriptor = services.LastOrDefault(static candidate =>
            candidate.ServiceType == typeof(IClusterMembership));
        if (descriptor is null || descriptor.Lifetime != ServiceLifetime.Singleton)
        {
            throw new InvalidOperationException(
                "Lakona TestCluster requires one singleton IClusterMembership registration.");
        }

        services.Remove(descriptor);
        var nodeGate = AddNode(nodeId);
        services.AddSingleton<IClusterMembership>(provider =>
            new PausableClusterMembership(
                CreateMembership(descriptor, provider),
                nodeGate));
    }

    private NodeViewGate AddNode(string nodeId)
    {
        lock (gate)
        {
            var result = new NodeViewGate(nodeId);
            nodes[nodeId] = result;
            return result;
        }
    }

    private NodeViewGate GetNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        lock (gate)
        {
            return nodes.TryGetValue(nodeId, out var result)
                ? result
                : throw new KeyNotFoundException(
                    $"Lakona TestCluster does not contain Membership control for node '{nodeId}'.");
        }
    }

    private static IClusterMembership CreateMembership(
        ServiceDescriptor descriptor,
        IServiceProvider provider)
    {
        if (descriptor.ImplementationInstance is IClusterMembership instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (IClusterMembership)descriptor.ImplementationFactory(provider);
        }

        if (descriptor.ImplementationType is not null)
        {
            return (IClusterMembership)ActivatorUtilities.CreateInstance(
                provider,
                descriptor.ImplementationType);
        }

        throw new InvalidOperationException(
            "The TestCluster IClusterMembership registration cannot be decorated.");
    }

    private sealed class PausableClusterMembership(
        IClusterMembership inner,
        NodeViewGate gate) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current => gate.GetCurrent(inner);

        public async ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default)
        {
            while (true)
            {
                var pause = gate.GetPause();
                if (pause is null)
                {
                    return await inner.WaitForChangeAsync(after, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (pause.Visible.View.CompareTo(after) > 0)
                {
                    return pause.Visible;
                }

                var observed = await inner.WaitForChangeAsync(
                        pause.Visible.View,
                        cancellationToken)
                    .ConfigureAwait(false);
                var resume = gate.BlockOn(observed);
                if (resume is null)
                {
                    continue;
                }

                await resume.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class NodeViewGate(string nodeId)
    {
        private readonly object gate = new();
        private bool paused;
        private ClusterMembershipSnapshot? visible;
        private int waiterArrivals;
        private TaskCompletionSource behind = NewCompletion();
        private TaskCompletionSource resumed = NewCompletion();
        private TaskCompletionSource waiterArrived = NewCompletion();

        internal int WaiterArrivalCount
        {
            get
            {
                lock (gate)
                {
                    return waiterArrivals;
                }
            }
        }

        internal void Pause()
        {
            lock (gate)
            {
                if (paused)
                {
                    throw new InvalidOperationException(
                        $"Membership view observation is already paused for node '{nodeId}'.");
                }

                if (visible is null)
                {
                    throw new InvalidOperationException(
                        $"Membership view control for node '{nodeId}' has not initialized.");
                }

                paused = true;
                waiterArrivals = 0;
                behind = NewCompletion();
                resumed = NewCompletion();
                waiterArrived = NewCompletion();
            }
        }

        internal void Resume()
        {
            TaskCompletionSource completion;
            lock (gate)
            {
                if (!paused)
                {
                    throw new InvalidOperationException(
                        $"Membership view observation is not paused for node '{nodeId}'.");
                }

                paused = false;
                completion = resumed;
            }

            completion.TrySetResult();
        }

        internal ClusterMembershipSnapshot GetCurrent(IClusterMembership inner)
        {
            lock (gate)
            {
                if (paused)
                {
                    return visible!;
                }
            }

            var current = inner.Current;
            lock (gate)
            {
                if (!paused)
                {
                    visible = current;
                    return current;
                }

                return visible!;
            }
        }

        internal PauseSnapshot? GetPause()
        {
            lock (gate)
            {
                return paused ? new PauseSnapshot(visible!) : null;
            }
        }

        internal Task? BlockOn(ClusterMembershipSnapshot observed)
        {
            TaskCompletionSource behindCompletion;
            TaskCompletionSource arrivalCompletion;
            Task resume;
            lock (gate)
            {
                if (!paused)
                {
                    visible = observed;
                    return null;
                }

                if (observed.View.CompareTo(visible!.View) <= 0)
                {
                    return resumed.Task;
                }

                waiterArrivals++;
                behindCompletion = behind;
                arrivalCompletion = waiterArrived;
                waiterArrived = NewCompletion();
                resume = resumed.Task;
            }

            behindCompletion.TrySetResult();
            arrivalCompletion.TrySetResult();
            return resume;
        }

        internal Task WaitUntilBehindAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                if (!paused)
                {
                    throw new InvalidOperationException(
                        $"Membership view observation is not paused for node '{nodeId}'.");
                }

                return behind.Task.WaitAsync(cancellationToken);
            }
        }

        internal async Task WaitForWaiterArrivalCountAsync(
            int minimum,
            CancellationToken cancellationToken)
        {
            if (minimum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum));
            }

            while (true)
            {
                Task changed;
                lock (gate)
                {
                    if (!paused)
                    {
                        throw new InvalidOperationException(
                            $"Membership view observation is not paused for node '{nodeId}'.");
                    }

                    if (waiterArrivals >= minimum)
                    {
                        return;
                    }

                    changed = waiterArrived.Task;
                }

                await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static TaskCompletionSource NewCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal sealed record PauseSnapshot(ClusterMembershipSnapshot Visible);
    }
}
