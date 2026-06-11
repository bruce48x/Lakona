namespace Lakona.Tool.Planning;

internal sealed class GenerationPlanBuilder(string rootPath)
{
    private readonly List<GeneratedFile> files = [];
    private readonly List<GeneratedDirectory> directories = [];
    private readonly List<PlanDiagnostic> diagnostics = [];
    private readonly List<GeneratedArchive> archives = [];

    public void AddFile(string relativePath, string content, FileWriteMode writeMode, GeneratedFileKind kind)
    {
        files.Add(new GeneratedFile(relativePath, content, writeMode, kind));
    }

    public void AddDirectory(string relativePath)
    {
        directories.Add(new GeneratedDirectory(relativePath));
    }

    public void AddArchive(string resourceName, string relativeDestinationPath)
    {
        archives.Add(new GeneratedArchive(resourceName, relativeDestinationPath));
    }

    public void AddDiagnostic(PlanDiagnostic diagnostic)
    {
        diagnostics.Add(diagnostic);
    }

    public GenerationPlan Build()
    {
        return new GenerationPlan(rootPath, files, directories, diagnostics, archives);
    }
}
