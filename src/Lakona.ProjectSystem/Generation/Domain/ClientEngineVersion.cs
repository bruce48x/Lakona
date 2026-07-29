using Lakona.ProjectSystem;

namespace Lakona.ProjectSystem.Generation.Domain;

internal enum ClientEngineVersion
{
    Unity2022,
    Unity60,
    Unity63,
    Tuanjie167,
    Godot46
}

internal static class ClientEngineVersionPolicy
{
    public static IReadOnlyList<ClientEngineVersion> GetSupportedVersions(ClientEngine engine) =>
        LakonaProjectCreationRules.GetSupportedClientEngineVersions(Map(engine))
            .Select(Map)
            .ToArray();

    public static ClientEngineVersion? GetDefaultVersion(ClientEngine engine) =>
        LakonaProjectCreationRules.GetDefaultClientEngineVersion(Map(engine)) is { } version
            ? Map(version)
            : null;

    public static ClientEngineVersion? Resolve(
        ClientEngine engine,
        ClientEngineVersion? requestedVersion)
    {
        if (requestedVersion is null)
        {
            return GetDefaultVersion(engine);
        }

        if (!GetSupportedVersions(engine).Contains(requestedVersion.Value))
        {
            throw new ArgumentException(
                $"Client engine version '{requestedVersion}' is not supported by '{engine}'.",
                nameof(requestedVersion));
        }

        return requestedVersion;
    }

    private static LakonaClientEngine Map(ClientEngine value) => value switch
    {
        ClientEngine.Unity => LakonaClientEngine.Unity,
        ClientEngine.Tuanjie => LakonaClientEngine.Tuanjie,
        ClientEngine.Godot => LakonaClientEngine.Godot,
        ClientEngine.Console => LakonaClientEngine.Console,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static ClientEngineVersion Map(LakonaClientEngineVersion value) => value switch
    {
        LakonaClientEngineVersion.Unity2022 => ClientEngineVersion.Unity2022,
        LakonaClientEngineVersion.Unity60 => ClientEngineVersion.Unity60,
        LakonaClientEngineVersion.Unity63 => ClientEngineVersion.Unity63,
        LakonaClientEngineVersion.Tuanjie167 => ClientEngineVersion.Tuanjie167,
        LakonaClientEngineVersion.Godot46 => ClientEngineVersion.Godot46,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}
