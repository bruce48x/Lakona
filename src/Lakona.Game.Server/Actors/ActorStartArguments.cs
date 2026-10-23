using MemoryPack;
using System.Reflection;

namespace Lakona.Game.Server.Actors;

/// <summary>A detached, serialized input for one Actor activation attempt.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ActorStartArguments
{
    /// <summary>Maximum serialized initialization payload: one MiB.</summary>
    public const int MaximumPayloadBytes = 1024 * 1024;
    [MemoryPackOrder(0)] public string Contract { get; init; } = string.Empty;
    [MemoryPackOrder(1)] public byte[] Payload { get; init; } = [];

    public static ActorStartArguments From<T>(T arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ValidateType(typeof(T));
        if (arguments.GetType() != typeof(T))
            throw new ArgumentException("Actor start arguments must use their exact declared type.", nameof(arguments));
        var payload = MemoryPackSerializer.Serialize(arguments);
        if (payload.Length > MaximumPayloadBytes)
            throw new ArgumentException("Actor start arguments exceed one MiB.", nameof(arguments));
        return new() { Contract = Identity(typeof(T)), Payload = payload };
    }

    internal object? Decode(Type type)
    {
        ValidateType(type);
        if (Payload is null || Payload.Length > MaximumPayloadBytes)
            throw new InvalidOperationException("Actor start arguments exceed one MiB or are missing.");
        if (Contract != Identity(type))
            throw new InvalidOperationException($"Actor start arguments expect '{Identity(type)}', received '{Contract}'.");
        return MemoryPackSerializer.Deserialize(type, Payload)
            ?? throw new InvalidOperationException("Actor start arguments cannot be null.");
    }

    internal static void ValidateType(Type type)
    {
        var hotfix = type.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(attribute =>
            attribute.Key == "LakonaProjectRole" && string.Equals(attribute.Value?.Trim(), "Hotfix", StringComparison.OrdinalIgnoreCase));
        if (hotfix || type.Assembly.IsCollectible || type.IsGenericType || !type.IsClass || type.IsAbstract
            || type.GetCustomAttributes(typeof(MemoryPackableAttribute), false).Length == 0)
            throw new InvalidOperationException("Actor start arguments must be a concrete, non-generic MemoryPack DTO in a stable assembly.");
    }

    private static string Identity(Type type) => $"{type.Assembly.GetName().Name}:{type.FullName}";
}
