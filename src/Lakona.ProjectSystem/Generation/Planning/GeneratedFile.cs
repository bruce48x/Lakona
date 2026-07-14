namespace Lakona.Tool.Planning;

internal sealed record GeneratedFile(
    string RelativePath,
    string Content,
    FileWriteMode WriteMode,
    GeneratedFileKind Kind);
