namespace Lakona.ProjectSystem;

public static class LakonaProjectCreationRules
{
    private static readonly IReadOnlyList<LakonaClientEngineVersion> UnityVersions =
        Array.AsReadOnly<LakonaClientEngineVersion>(
        [
        LakonaClientEngineVersion.Unity2022,
        LakonaClientEngineVersion.Unity60,
        LakonaClientEngineVersion.Unity63
        ]);

    private static readonly IReadOnlyList<LakonaClientEngineVersion> TuanjieVersions =
        Array.AsReadOnly<LakonaClientEngineVersion>(
        [
        LakonaClientEngineVersion.Tuanjie167
        ]);

    private static readonly IReadOnlyList<LakonaClientEngineVersion> GodotVersions =
        Array.AsReadOnly<LakonaClientEngineVersion>(
        [
        LakonaClientEngineVersion.Godot46
        ]);

    private static readonly IReadOnlyList<LakonaClientEngineVersion> NoVersions =
        Array.AsReadOnly<LakonaClientEngineVersion>([]);

    public static IReadOnlyList<LakonaClientEngineVersion> GetSupportedClientEngineVersions(
        LakonaClientEngine engine) => engine switch
    {
        LakonaClientEngine.Unity => UnityVersions,
        LakonaClientEngine.Tuanjie => TuanjieVersions,
        LakonaClientEngine.Godot => GodotVersions,
        LakonaClientEngine.Console => NoVersions,
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
    };

    public static LakonaClientEngineVersion? GetDefaultClientEngineVersion(
        LakonaClientEngine engine) => engine switch
    {
        LakonaClientEngine.Unity => LakonaClientEngineVersion.Unity2022,
        LakonaClientEngine.Tuanjie => LakonaClientEngineVersion.Tuanjie167,
        LakonaClientEngine.Godot => LakonaClientEngineVersion.Godot46,
        LakonaClientEngine.Console => null,
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
    };
}
