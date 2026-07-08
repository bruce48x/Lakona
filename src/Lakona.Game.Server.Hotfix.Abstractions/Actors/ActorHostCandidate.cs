namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record ActorHostCandidate
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal));

    public ActorHostCandidate(
        string nodeId,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        NodeId = nodeId;
        Metadata = metadata is null
            ? EmptyMetadata
            : new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(metadata, StringComparer.Ordinal));
    }

    public string NodeId { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}
