using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Common;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Rendering;

public sealed class PackageReferenceRendererTests
{
    [Fact]
    public void RenderSdkPackageReferences_RendersAnalyzerMetadata()
    {
        var references = new[]
        {
            new PackageReferenceSpec(
                "Sample.Analyzers",
                "1.2.3",
                PackageReferenceStyle.Sdk,
                PrivateAssets: "all",
                IncludeAssets: "runtime; build; native; contentfiles; analyzers; buildtransitive"),
            new PackageReferenceSpec(
                "Sample.Generators",
                "2.3.4",
                PackageReferenceStyle.Sdk,
                PrivateAssets: "all",
                OutputItemType: "Analyzer")
        };

        var xml = PackageReferenceRenderer.RenderSdkPackageReferences(references);

        Assert.Contains("<PackageReference Include=\"Sample.Analyzers\" Version=\"1.2.3\">", xml, StringComparison.Ordinal);
        Assert.Contains("<PrivateAssets>all</PrivateAssets>", xml, StringComparison.Ordinal);
        Assert.Contains("<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>", xml, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Sample.Generators\" Version=\"2.3.4\" PrivateAssets=\"all\" OutputItemType=\"Analyzer\" />", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNuGetForUnityPackages_RendersTargetFrameworkAndManualInstallFlag()
    {
        var references = new[]
        {
            new PackageReferenceSpec("Lakona.Rpc.Core", "1.2.3", PackageReferenceStyle.NuGetForUnity),
            new PackageReferenceSpec("Lakona.Rpc.Client", "2.3.4", PackageReferenceStyle.NuGetForUnity, ManuallyInstalled: true)
        };

        var xml = PackageReferenceRenderer.RenderNuGetForUnityPackages(references);

        Assert.Contains("<package id=\"Lakona.Rpc.Core\" version=\"1.2.3\" targetFramework=\"netstandard2.1\" />", xml, StringComparison.Ordinal);
        Assert.Contains("<package id=\"Lakona.Rpc.Client\" version=\"2.3.4\" targetFramework=\"netstandard2.1\" manuallyInstalled=\"true\" />", xml, StringComparison.Ordinal);
    }
}
