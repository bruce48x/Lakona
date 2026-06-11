using System.Security;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Rendering.Common;

internal static class PackageReferenceRenderer
{
    public static string RenderSdkPackageReferences(IEnumerable<PackageReferenceSpec> references)
    {
        return string.Join(Environment.NewLine, references.Select(RenderSdkPackageReference));
    }

    public static string RenderNuGetForUnityPackages(IEnumerable<PackageReferenceSpec> references)
    {
        return string.Join(Environment.NewLine, references.Select(RenderNuGetForUnityPackage));
    }

    private static string RenderSdkPackageReference(PackageReferenceSpec reference)
    {
        var attributes = new List<string>
        {
            $"Include=\"{Escape(reference.Id)}\"",
            $"Version=\"{Escape(reference.Version)}\""
        };

        if (reference.OutputItemType is not null)
        {
            if (reference.PrivateAssets is not null)
            {
                attributes.Add($"PrivateAssets=\"{Escape(reference.PrivateAssets)}\"");
            }

            attributes.Add($"OutputItemType=\"{Escape(reference.OutputItemType)}\"");
            return $"    <PackageReference {string.Join(" ", attributes)} />";
        }

        if (reference.PrivateAssets is null && reference.IncludeAssets is null)
        {
            return $"    <PackageReference {string.Join(" ", attributes)} />";
        }

        var lines = new List<string>
        {
            $"    <PackageReference {string.Join(" ", attributes)}>"
        };

        if (reference.PrivateAssets is not null)
        {
            lines.Add($"      <PrivateAssets>{Escape(reference.PrivateAssets)}</PrivateAssets>");
        }

        if (reference.IncludeAssets is not null)
        {
            lines.Add($"      <IncludeAssets>{Escape(reference.IncludeAssets)}</IncludeAssets>");
        }

        lines.Add("    </PackageReference>");
        return string.Join(Environment.NewLine, lines);
    }

    private static string RenderNuGetForUnityPackage(PackageReferenceSpec reference)
    {
        var manuallyInstalled = reference.ManuallyInstalled ? " manuallyInstalled=\"true\"" : string.Empty;
        return $"  <package id=\"{Escape(reference.Id)}\" version=\"{Escape(reference.Version)}\"{manuallyInstalled} />";
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}
