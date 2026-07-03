namespace Lakona.RepositoryGuards.Tests.PackageVersions;

internal sealed record PackageVersionGuardResult(IReadOnlyList<PackageVersionFailure> Failures);

internal sealed record PackageVersionFailure(string PackageId, string CurrentVersion, string Reason);
