using System.Text.Json;

namespace Lakona.Game.Server.HotfixAdmin;

public sealed record HotfixStatusResponse(
    string Mode,
    string? CurrentPointerVersion,
    string? PreviousPointerVersion,
    string? LoadedVersion,
    long DispatchTableVersion,
    int MethodCount,
    string? LastReloadStatus,
    string? LastFailureMessage,
    string BuildTag);

public sealed record HotfixActivateRequest(
    string Version,
    string? ExpectedCurrentVersion,
    string? OperationId);

public sealed record HotfixPackageManifest(
    string Version,
    DateTimeOffset BuiltAtUtc,
    string Assembly,
    string TargetFramework,
    string BuildTag,
    string ToolVersion);

public static class HotfixAdminJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
