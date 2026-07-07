namespace Lakona.Game.Server.Hotfix;

public static class HotfixActorApiMetadata
{
    public const string ActorMessageKind = "_hotfix_actor";
    public const string VersionKey = "lakona-game.actor-api.version";
    public const string ActorTypeKey = "lakona-game.actor-api.actor-type";
    public const string MethodKey = "lakona-game.actor-api.method";
    public const string RequestTypeKey = "lakona-game.actor-api.request-type";
    public const string ResultTypeKey = "lakona-game.actor-api.result-type";
    public const string MethodKeyKey = "lakona-game.actor-api.method-key";
    public const string MethodIdKey = "lakona-game.actor-api.method-id";
    public const string VoidResultType = "void";

    public static string CreateTypeIdentity(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
        return $"{type.FullName ?? type.Name}, {assemblyName}";
    }

    public static string CreateMethodKey(
        string actorType,
        string methodName,
        string requestType,
        string resultType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestType);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultType);

        return $"actor:{actorType}|method:{methodName}|request:{requestType}|result:{resultType}";
    }

    public static ulong CreateMethodId(string methodKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodKey);

        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        unchecked
        {
            var hash = offsetBasis;
            foreach (var value in System.Text.Encoding.UTF8.GetBytes(methodKey))
            {
                hash ^= value;
                hash *= prime;
            }

            return hash;
        }
    }
}
