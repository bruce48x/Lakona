using Lakona.Tool.RpcStarter;

internal static class PackageReferenceText
{
    public static string RenderSdkPackageReferences(StarterDependencyPlan plan)
    {
        return string.Join(Environment.NewLine, plan.PackageReferences.Select(RenderSdkPackageReference));
    }

    public static string RenderNuGetForUnityPackages(StarterDependencyPlan plan)
    {
        return string.Join(Environment.NewLine, plan.PackageReferences.Select(RenderNuGetForUnityPackage));
    }

    private static string RenderSdkPackageReference(StarterPackageReference reference)
    {
        if (reference.PrivateAssets is null && reference.IncludeAssets is null)
        {
            return $"    <PackageReference Include=\"{reference.Id}\" Version=\"{reference.Version}\" />";
        }

        var lines = new List<string>
        {
            $"    <PackageReference Include=\"{reference.Id}\" Version=\"{reference.Version}\">"
        };

        if (reference.PrivateAssets is not null)
        {
            lines.Add($"      <PrivateAssets>{reference.PrivateAssets}</PrivateAssets>");
        }

        if (reference.IncludeAssets is not null)
        {
            lines.Add($"      <IncludeAssets>{reference.IncludeAssets}</IncludeAssets>");
        }

        lines.Add("    </PackageReference>");
        return string.Join(Environment.NewLine, lines);
    }

    private static string RenderNuGetForUnityPackage(StarterPackageReference reference)
    {
        var manuallyInstalled = reference.ManuallyInstalled ? " manuallyInstalled=\"true\"" : string.Empty;
        return $"  <package id=\"{reference.Id}\" version=\"{reference.Version}\"{manuallyInstalled} />";
    }
}
