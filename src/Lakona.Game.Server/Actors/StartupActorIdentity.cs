using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

internal static class StartupActorIdentity
{
    public static ActorId CreateReplicaId(string actorName, NodeId node)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);
        return ActorId.From($"{actorName}/@startup/{node.Value}");
    }

    public static string CreatePolicyHash(Type actorType, Type keyType)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentNullException.ThrowIfNull(keyType);
        return $"startup:v1:{actorType.FullName ?? actorType.Name}:{keyType.FullName ?? keyType.Name}";
    }

    public static string NormalizeHotfixVersion(string? sourceVersion) =>
        string.IsNullOrWhiteSpace(sourceVersion) ? "hotfix" : sourceVersion;
}
