using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

internal sealed class ClusterCapabilityIndex(IClusterMembership membership)
{
    public IReadOnlyList<ActorHostMatch> FindReadyActorHosts(string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var snapshot = membership.Current;
        var matches = snapshot.Members
            .Where(static member => member.State == ClusterMemberState.Ready)
            .Select(member => (Member: member, Host: member.ActorHosts.SingleOrDefault(host => string.Equals(host.Actor, actor, StringComparison.Ordinal))))
            .Where(static pair => pair.Host is not null)
            .OrderBy(static pair => pair.Member.Reference.Node.Value, StringComparer.Ordinal)
            .Select(static pair => new ActorHostMatch(pair.Member.Reference.Node, pair.Host!))
            .ToArray();
        EnsureUnique(matches.Select(static match => match.Node));
        return matches;
    }

    public IReadOnlyList<StartupActorMatch> FindReadyStartupActors(string actor, string policyHash, string buildTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildTag);
        var snapshot = membership.Current;
        var matches = snapshot.Members
            .Where(static member => member.State == ClusterMemberState.Ready)
            .Select(member => (Member: member, Startup: member.StartupActors.SingleOrDefault(item =>
                string.Equals(item.Actor, actor, StringComparison.Ordinal)
                && string.Equals(item.PolicyHash, policyHash, StringComparison.Ordinal)
                && string.Equals(item.BuildTag, buildTag, StringComparison.Ordinal))))
            .Where(static pair => pair.Startup is not null)
            .OrderBy(static pair => pair.Member.Reference.Node.Value, StringComparer.Ordinal)
            .Select(static pair => new StartupActorMatch(pair.Member.Reference.Node, pair.Startup!))
            .ToArray();
        EnsureUnique(matches.Select(static match => match.Node));
        return matches;
    }

    private static void EnsureUnique(IEnumerable<NodeId> nodes)
    {
        var seen = new HashSet<NodeId>();
        foreach (var node in nodes)
        {
            if (!seen.Add(node)) throw new InvalidOperationException($"Committed membership contains ambiguous Ready node '{node.Value}'.");
        }
    }

    internal sealed record ActorHostMatch(NodeId Node, NodeActorHostDescriptor Host);
    internal sealed record StartupActorMatch(NodeId Node, StartupActorDescriptor Startup);
}
