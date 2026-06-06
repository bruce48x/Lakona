namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record HotfixReloadResult
{
    public HotfixReloadResult(
        HotfixReloadStatus Status,
        HotfixSnapshot Current,
        string? RequestedVersion,
        string? RequestedPath,
        IReadOnlyList<string>? Diagnostics,
        string? ErrorMessage = null,
        string? ExceptionType = null)
    {
        this.Status = Status;
        this.Current = Current;
        this.RequestedVersion = RequestedVersion;
        this.RequestedPath = RequestedPath;
        this.Diagnostics = Array.AsReadOnly(Diagnostics?.ToArray() ?? []);
        this.ErrorMessage = ErrorMessage;
        this.ExceptionType = ExceptionType;
    }

    public HotfixReloadStatus Status { get; }

    public HotfixSnapshot Current { get; }

    public string? RequestedVersion { get; }

    public string? RequestedPath { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public string? ErrorMessage { get; }

    public string? ExceptionType { get; }

    public bool Succeeded => Status == HotfixReloadStatus.Succeeded;
}
