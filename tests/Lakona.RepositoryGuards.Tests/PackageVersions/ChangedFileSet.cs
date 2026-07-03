namespace Lakona.RepositoryGuards.Tests.PackageVersions;

internal sealed record GitChangeSet(string BaseRef, string HeadRef, IReadOnlyList<string> ChangedPaths);
