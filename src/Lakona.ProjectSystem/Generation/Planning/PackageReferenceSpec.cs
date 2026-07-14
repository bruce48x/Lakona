namespace Lakona.Tool.Planning;

internal enum PackageReferenceStyle
{
    Sdk,
    NuGetForUnity
}

internal sealed record PackageReferenceSpec(
    string Id,
    string Version,
    PackageReferenceStyle Style,
    bool ManuallyInstalled = false,
    string? PrivateAssets = null,
    string? IncludeAssets = null,
    string? OutputItemType = null);

internal sealed record DependencyPlan(IReadOnlyList<PackageReferenceSpec> PackageReferences);
