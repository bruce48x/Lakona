using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Actors;

internal static class ActorNameResolver
{
    public static string Resolve(Type actorType)
    {
        return ActorNameConventions.Resolve(actorType);
    }
}
