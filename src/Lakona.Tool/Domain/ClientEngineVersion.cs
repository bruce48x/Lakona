namespace Lakona.Tool.Domain;

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
    private static readonly ClientEngineVersion[] UnityVersions =
    [
        ClientEngineVersion.Unity2022,
        ClientEngineVersion.Unity60,
        ClientEngineVersion.Unity63
    ];

    private static readonly ClientEngineVersion[] TuanjieVersions =
    [
        ClientEngineVersion.Tuanjie167
    ];

    private static readonly ClientEngineVersion[] GodotVersions =
    [
        ClientEngineVersion.Godot46
    ];

    private static readonly ClientEngineVersion[] NoVersions = [];

    public static IReadOnlyList<ClientEngineVersion> GetSupportedVersions(ClientEngine engine) => engine switch
    {
        ClientEngine.Unity => UnityVersions,
        ClientEngine.Tuanjie => TuanjieVersions,
        ClientEngine.Godot => GodotVersions,
        ClientEngine.Console => NoVersions,
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
    };

    public static ClientEngineVersion? GetDefaultVersion(ClientEngine engine) => engine switch
    {
        ClientEngine.Unity => ClientEngineVersion.Unity2022,
        ClientEngine.Tuanjie => ClientEngineVersion.Tuanjie167,
        ClientEngine.Godot => ClientEngineVersion.Godot46,
        ClientEngine.Console => null,
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
    };

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
}
