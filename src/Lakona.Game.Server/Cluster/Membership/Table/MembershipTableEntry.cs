using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster.Membership;

public enum MembershipTableStatus
{
    Joining = 0,
    Active = 1,
    Stopping = 2,
    Dead = 3
}

public sealed class MembershipTableEntry
{
    private static readonly IReadOnlyDictionary<string, string> EmptyLabels =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    public MembershipTableEntry(
        NodeReference reference,
        MembershipTableStatus status,
        NodeEndpoint clusterEndpoint,
        long version,
        DateTimeOffset iAmAliveTime,
        IReadOnlyDictionary<string, string>? labels = null,
        IReadOnlyList<NodeActorHostDescriptor>? actorHosts = null,
        IReadOnlyList<StartupActorDescriptor>? startupActors = null,
        IReadOnlyList<MembershipSuspectVote>? suspectVotes = null,
        DateTimeOffset? startTime = null,
        long generation = 1)
    {
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        ClusterEndpoint = clusterEndpoint ?? throw new ArgumentNullException(nameof(clusterEndpoint));
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Membership entry version must be positive.");
        }
        if (generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), "Membership generation must be positive.");
        }

        Status = status;
        Version = version;
        IAmAliveTime = iAmAliveTime;
        StartTime = startTime ?? iAmAliveTime;
        Generation = generation;
        Labels = CopyLabels(labels);
        ActorHosts = ClusterActorDescriptorNormalization.CopyActorHosts(actorHosts, nameof(actorHosts));
        StartupActors = ClusterActorDescriptorNormalization.CopyStartupActors(startupActors, nameof(startupActors));
        SuspectVotes = CopySuspectVotes(suspectVotes);
    }

    public NodeReference Reference { get; }
    public MembershipTableStatus Status { get; }
    public NodeEndpoint ClusterEndpoint { get; }
    public long Version { get; }
    public DateTimeOffset IAmAliveTime { get; }
    public DateTimeOffset StartTime { get; }
    public long Generation { get; }
    public IReadOnlyDictionary<string, string> Labels { get; }
    public IReadOnlyList<NodeActorHostDescriptor> ActorHosts { get; }
    public IReadOnlyList<StartupActorDescriptor> StartupActors { get; }
    public IReadOnlyList<MembershipSuspectVote> SuspectVotes { get; }

    public MembershipTableEntry WithStatus(MembershipTableStatus status) =>
        Copy(status, checked(Version + 1), IAmAliveTime);

    public MembershipTableEntry WithIAmAliveTime(DateTimeOffset timestamp) =>
        Copy(Status, Version, timestamp);

    public MembershipTableEntry WithSuspectVotes(IReadOnlyList<MembershipSuspectVote> votes) =>
        new(
            Reference,
            Status,
            ClusterEndpoint,
            checked(Version + 1),
            IAmAliveTime,
            Labels,
            ActorHosts,
            StartupActors,
            votes,
            StartTime,
            Generation);

    public MembershipTableEntry WithDescriptor(
        MembershipTableStatus status,
        NodeEndpoint clusterEndpoint,
        IReadOnlyDictionary<string, string>? labels,
        IReadOnlyList<NodeActorHostDescriptor>? actorHosts,
        IReadOnlyList<StartupActorDescriptor>? startupActors)
    {
        return new MembershipTableEntry(
            Reference,
            status,
            clusterEndpoint,
            checked(Version + 1),
            IAmAliveTime,
            labels,
            actorHosts,
            startupActors,
            SuspectVotes,
            StartTime,
            Generation);
    }

    private MembershipTableEntry Copy(MembershipTableStatus status, long version, DateTimeOffset iAmAliveTime) =>
        new(Reference, status, ClusterEndpoint, version, iAmAliveTime, Labels, ActorHosts, StartupActors, SuspectVotes, StartTime, Generation);

    private IReadOnlyList<MembershipSuspectVote> CopySuspectVotes(
        IReadOnlyList<MembershipSuspectVote>? votes)
    {
        if (votes is null)
        {
            return Array.Empty<MembershipSuspectVote>();
        }

        var observers = new HashSet<NodeReference>();
        var copy = new MembershipSuspectVote[votes.Count];
        for (var index = 0; index < votes.Count; index++)
        {
            var vote = votes[index] ?? throw new ArgumentException("Suspicion vote cannot be null.", nameof(votes));
            if (vote.Observer.Cluster != Reference.Cluster || vote.Observer == Reference || !observers.Add(vote.Observer))
            {
                throw new ArgumentException("Suspicion votes require distinct peer observers from the same cluster.", nameof(votes));
            }

            copy[index] = vote;
        }

        Array.Sort(copy, static (left, right) => string.Compare(
            left.Observer.Node.Value,
            right.Observer.Node.Value,
            StringComparison.Ordinal));
        return new ReadOnlyCollection<MembershipSuspectVote>(copy);
    }

    private static IReadOnlyDictionary<string, string> CopyLabels(IReadOnlyDictionary<string, string>? labels)
    {
        if (labels is null)
        {
            return EmptyLabels;
        }

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in labels)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new ArgumentException("Cluster member label names cannot be empty.", nameof(labels));
            }

            copy[pair.Key] = pair.Value ?? throw new ArgumentException("Cluster member label values cannot be null.", nameof(labels));
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}

public sealed class MembershipSuspectVote
{
    public MembershipSuspectVote(NodeReference observer, DateTimeOffset timestamp)
    {
        Observer = observer ?? throw new ArgumentNullException(nameof(observer));
        Timestamp = timestamp;
    }

    public NodeReference Observer { get; }
    public DateTimeOffset Timestamp { get; }
}
