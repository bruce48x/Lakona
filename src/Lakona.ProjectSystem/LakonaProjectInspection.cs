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
    public string? ServerPath { get; init; }

    public string? ClientPath { get; init; }

    public bool IsRecognized => Status is LakonaProjectStatus.Ready or LakonaProjectStatus.Incomplete;
}
