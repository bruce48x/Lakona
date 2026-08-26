using System.Collections.Concurrent;

namespace Lakona.Game.Testing;

/// <summary>Controls connectivity between nodes in an in-process test cluster.</summary>
public sealed class LakonaTestNetwork
{
    private readonly ConcurrentDictionary<Link, byte> blocked = new();

    /// <summary>Gets a stable snapshot of the currently blocked directed links.</summary>
    public IReadOnlyList<LakonaTestNetworkLink> BlockedLinks => blocked.Keys
        .OrderBy(static link => link.Source, StringComparer.Ordinal)
        .ThenBy(static link => link.Target, StringComparer.Ordinal)
        .Select(static link => new LakonaTestNetworkLink(link.Source, link.Target))
        .ToArray();

    /// <summary>Blocks traffic in both directions between two nodes.</summary>
    public void Partition(string firstNodeId, string secondNodeId)
    {
        ValidatePair(firstNodeId, secondNodeId);
        blocked.TryAdd(new Link(firstNodeId, secondNodeId), 0);
        blocked.TryAdd(new Link(secondNodeId, firstNodeId), 0);
    }

    /// <summary>Blocks traffic from one node to another without blocking the reverse direction.</summary>
    public void BlockOneWay(string sourceNodeId, string targetNodeId)
    {
        ValidatePair(sourceNodeId, targetNodeId);
        blocked.TryAdd(new Link(sourceNodeId, targetNodeId), 0);
    }

    /// <summary>Restores traffic in both directions between two nodes.</summary>
    public void Heal(string firstNodeId, string secondNodeId)
    {
        ValidatePair(firstNodeId, secondNodeId);
        blocked.TryRemove(new Link(firstNodeId, secondNodeId), out _);
        blocked.TryRemove(new Link(secondNodeId, firstNodeId), out _);
    }

    /// <summary>Restores one directed link without changing the reverse direction.</summary>
    public void HealOneWay(string sourceNodeId, string targetNodeId)
    {
        ValidatePair(sourceNodeId, targetNodeId);
        blocked.TryRemove(new Link(sourceNodeId, targetNodeId), out _);
    }

    /// <summary>Restores every blocked link.</summary>
    public void HealAll() => blocked.Clear();

    internal void ThrowIfBlocked(string sourceNodeId, string targetNodeId)
    {
        if (blocked.ContainsKey(new Link(sourceNodeId, targetNodeId)))
        {
            throw new IOException(
                $"Lakona TestCluster network is partitioned from '{sourceNodeId}' to '{targetNodeId}'.");
        }
    }

    private static void ValidatePair(string firstNodeId, string secondNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondNodeId);
        if (string.Equals(firstNodeId, secondNodeId, StringComparison.Ordinal))
        {
            throw new ArgumentException("A node cannot be partitioned from itself.");
        }
    }

    private readonly record struct Link(string Source, string Target);
}

/// <summary>Identifies one blocked direction in a TestCluster network.</summary>
public sealed record LakonaTestNetworkLink(string SourceNodeId, string TargetNodeId);
