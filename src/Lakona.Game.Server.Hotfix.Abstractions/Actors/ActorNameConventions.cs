namespace Lakona.Game.Server.Hotfix.Abstractions;

public static class ActorNameConventions
{
    public const string ActorNameAttributeMetadataName =
        "Lakona.Game.Server.Actors.ActorNameAttribute";

    public static string Resolve(Type actorType)
    {
        ArgumentNullException.ThrowIfNull(actorType);

        foreach (var attribute in actorType.GetCustomAttributesData())
        {
            if (string.Equals(
                    attribute.AttributeType.FullName,
                    ActorNameAttributeMetadataName,
                    StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Count == 1 &&
                attribute.ConstructorArguments[0].Value is string explicitName &&
                !string.IsNullOrWhiteSpace(explicitName))
            {
                return explicitName;
            }
        }

        return ResolveDefault(actorType.Name);
    }

    public static string ResolveDefault(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        var name = typeName.EndsWith("Actor", StringComparison.Ordinal) &&
                   typeName.Length > "Actor".Length
            ? typeName[..^"Actor".Length]
            : typeName;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
