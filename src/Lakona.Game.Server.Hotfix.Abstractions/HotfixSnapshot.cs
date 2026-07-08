namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record HotfixSnapshot
{
    public HotfixSnapshot(
        string? Version,
        string? SourcePath,
        DateTimeOffset? LoadedAtUtc,
        long DispatchTableVersion,
        IReadOnlyList<HotfixMethodKey>? Methods,
        HotfixReloadStatus? LastReloadStatus,
        string? LastFailureMessage,
        string? LastFailureExceptionType,
        IReadOnlyList<HotfixFeatureDeclaration>? Features = null,
        IReadOnlyList<HotfixActorHostDescriptor>? ActorHosts = null)
    {
        this.Version = Version;
        this.SourcePath = SourcePath;
        this.LoadedAtUtc = LoadedAtUtc;
        this.DispatchTableVersion = DispatchTableVersion;
        this.Methods = Array.AsReadOnly(Methods?.ToArray() ?? []);
        this.LastReloadStatus = LastReloadStatus;
        this.LastFailureMessage = LastFailureMessage;
        this.LastFailureExceptionType = LastFailureExceptionType;
        this.Features = Array.AsReadOnly(Features?.ToArray() ?? []);
        this.ActorHosts = Array.AsReadOnly(ActorHosts?.ToArray() ?? []);
    }

    public string? Version { get; }

    public string? SourcePath { get; }

    public DateTimeOffset? LoadedAtUtc { get; }

    public long DispatchTableVersion { get; }

    public IReadOnlyList<HotfixMethodKey> Methods { get; }

    public IReadOnlyList<HotfixFeatureDeclaration> Features { get; }

    public IReadOnlyList<HotfixActorHostDescriptor> ActorHosts { get; }

    public HotfixReloadStatus? LastReloadStatus { get; }

    public string? LastFailureMessage { get; }

    public string? LastFailureExceptionType { get; }
}

public sealed record HotfixActorHostDescriptor(
    string Actor,
    string PolicyHash,
    string BuildTag,
    IReadOnlyDictionary<string, string>? Metadata = null);
