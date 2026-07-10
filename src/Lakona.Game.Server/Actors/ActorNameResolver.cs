namespace Lakona.Game.Server.Actors;

internal static class ActorNameResolver
{
    public static string Resolve(Type actorType)
    {
        ArgumentNullException.ThrowIfNull(actorType);

        var attribute = (ActorNameAttribute?)Attribute.GetCustomAttribute(
            actorType,
            typeof(ActorNameAttribute),
            inherit: false);
        if (attribute is not null)
        {
            return attribute.Name;
        }

        var name = actorType.Name.EndsWith("Actor", StringComparison.Ordinal)
            ? actorType.Name[..^"Actor".Length]
            : actorType.Name;
        return string.IsNullOrWhiteSpace(name)
            ? actorType.Name.ToLowerInvariant()
            : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
