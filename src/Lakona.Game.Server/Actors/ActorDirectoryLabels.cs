namespace Lakona.Game.Server.Actors;

public static class ActorDirectoryLabels
{
    public const string RoleKey = "lakona.role";

    public const string RoleValue = "actor-directory";

    public static IReadOnlyDictionary<string, string> Values { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RoleKey] = RoleValue
        };
}
