namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record ActorHostCandidate
{
    public ActorHostCandidate(
        string nodeId,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        NodeId = nodeId;
        Metadata = metadata;
    }

    public string NodeId { get; }

    public IReadOnlyDictionary<string, string>? Metadata { get; }
}
