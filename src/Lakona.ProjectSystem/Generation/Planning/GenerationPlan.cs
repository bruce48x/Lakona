namespace Lakona.ProjectSystem.Generation.Planning;

internal sealed record GenerationPlan(
    string RootPath,
    IReadOnlyList<GeneratedFile> Files,
    IReadOnlyList<GeneratedDirectory> Directories,
    IReadOnlyList<PlanDiagnostic> Diagnostics,
    IReadOnlyList<GeneratedArchive>? Archives = null);
