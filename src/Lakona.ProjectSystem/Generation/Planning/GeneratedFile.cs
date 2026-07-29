namespace Lakona.ProjectSystem.Generation.Planning;

internal sealed record GeneratedFile(
    string RelativePath,
    string Content,
    FileWriteMode WriteMode,
    GeneratedFileKind Kind);
