namespace Lakona.Tool.Hotfix;

internal sealed record HotfixPackageManifest(
    string Version,
    DateTimeOffset BuiltAtUtc,
    string Assembly,
    string TargetFramework,
    string BuildTag,
    string ToolVersion);
