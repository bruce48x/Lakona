namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record HotfixSnapshot
{
    public HotfixSnapshot(
        string? Version,
        string? SourceKind,
        string? SourcePath,
        DateTimeOffset? LoadedAtUtc,
        long DispatchTableVersion,
        IReadOnlyList<HotfixMethodKey>? Methods,
        HotfixReloadStatus? LastReloadStatus,
        string? LastFailureMessage,
        string? LastFailureExceptionType)
    {
        this.Version = Version;
        this.SourceKind = SourceKind;
        this.SourcePath = SourcePath;
        this.LoadedAtUtc = LoadedAtUtc;
        this.DispatchTableVersion = DispatchTableVersion;
        this.Methods = Array.AsReadOnly(Methods?.ToArray() ?? []);
        this.LastReloadStatus = LastReloadStatus;
        this.LastFailureMessage = LastFailureMessage;
        this.LastFailureExceptionType = LastFailureExceptionType;
    }

    public string? Version { get; }

    public string? SourceKind { get; }

    public string? SourcePath { get; }

    public DateTimeOffset? LoadedAtUtc { get; }

    public long DispatchTableVersion { get; }

    public IReadOnlyList<HotfixMethodKey> Methods { get; }

    public HotfixReloadStatus? LastReloadStatus { get; }

    public string? LastFailureMessage { get; }

    public string? LastFailureExceptionType { get; }
}
