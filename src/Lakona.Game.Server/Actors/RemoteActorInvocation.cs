using Lakona.Game.Cluster;
using System.Collections.ObjectModel;

namespace Lakona.Game.Server.Actors;

public sealed class RemoteActorInvocation
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    public RemoteActorInvocation(
        NodeId node,
        ActorId actorId,
        string actorName,
        string methodName,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset deadline,
        string correlationId,
        IReadOnlyDictionary<string, string>? metadata = null,
        long? expectedNodeEpoch = null,
        NodeReference? ownerReference = null,
        ActorActivationId? activationId = null,
        long activationVersion = 0)
    {
        Node = node;
        ActorId = actorId;
        ActorName = actorName;
        MethodName = methodName;
        Payload = payload.ToArray();
        Deadline = deadline;
        CorrelationId = correlationId;
        ExpectedNodeEpoch = expectedNodeEpoch;
        OwnerReference = ownerReference;
        ActivationId = activationId;
        ActivationVersion = activationVersion;
        Metadata = metadata is null
            ? EmptyMetadata
            : new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(metadata, StringComparer.Ordinal));
    }

    public NodeId Node { get; }

    public ActorId ActorId { get; }

    public string ActorName { get; }

    public string MethodName { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public DateTimeOffset Deadline { get; }

    public string CorrelationId { get; }

    public long? ExpectedNodeEpoch { get; }

    public NodeReference? OwnerReference { get; }

    public ActorActivationId? ActivationId { get; }

    public long ActivationVersion { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}
