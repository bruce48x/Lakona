namespace Lakona.ProjectSystem;

public sealed record LakonaProjectInspection(
    string RootPath,
    string Name,
    LakonaProjectStatus Status,
    LakonaProjectClient Client,
    string? ClientVersion,
    string? LakonaVersion,
    IReadOnlyList<LakonaProjectDiagnostic> Diagnostics)
{
    public bool IsRecognized => Status is LakonaProjectStatus.Ready or LakonaProjectStatus.Incomplete;
}
