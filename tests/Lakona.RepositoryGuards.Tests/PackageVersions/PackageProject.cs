namespace Lakona.RepositoryGuards.Tests.PackageVersions;

internal sealed record PackageProject(
    string ProjectPath,
    string PackageId,
    string Version,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> VersionSourceReferences,
    IReadOnlyList<string> PackedInputPaths);
