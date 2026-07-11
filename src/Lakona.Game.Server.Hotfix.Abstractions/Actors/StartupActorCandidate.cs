namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record StartupActorCandidate
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal));

    public StartupActorCandidate(
        string nodeId,
        long nodeEpoch,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentOutOfRangeException.ThrowIfNegative(nodeEpoch);

        NodeId = nodeId;
        NodeEpoch = nodeEpoch;
        Metadata = metadata is null
            ? EmptyMetadata
            : new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(metadata, StringComparer.Ordinal));
    }

    public string NodeId { get; }

    public long NodeEpoch { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}
