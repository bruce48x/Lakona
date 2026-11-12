namespace Lakona.ProjectSystem.Generation.Planning;

internal sealed record PackageReferenceSpec(
    string Id,
    string Version,
    bool ManuallyInstalled = false,
    string? PrivateAssets = null,
    string? IncludeAssets = null,
    string? OutputItemType = null);

internal sealed record DependencyPlan(IReadOnlyList<PackageReferenceSpec> PackageReferences);
