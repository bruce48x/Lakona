namespace Lakona.Game.Server.HotfixAdmin;

public sealed record HotfixAdminDiagnostic(
    string Code,
    string Stage,
    string? CandidateVersion,
    string Message,
    string Remediation,
    string CorrelationId,
    string? LoadedVersion,
    long DispatchTableVersion,
    IReadOnlyList<string> Diagnostics);

public sealed class HotfixAdminException : InvalidOperationException
{
    internal HotfixAdminException(HotfixAdminDiagnostic diagnostic) : base(diagnostic.Message)
    {
        Diagnostic = diagnostic;
    }

    public HotfixAdminDiagnostic Diagnostic { get; }
}
