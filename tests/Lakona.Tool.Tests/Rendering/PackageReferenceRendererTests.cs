using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Common;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class PackageReferenceRendererTests
{
    [Fact]
    public void RenderSdkPackageReferences_RendersAnalyzerMetadata()
    {
        var references = new[]
        {
            new PackageReferenceSpec(
                "Lakona.Rpc.Analyzers",
                "1.2.3",
                PackageReferenceStyle.Sdk,
                PrivateAssets: "all",
                IncludeAssets: "runtime; build; native; contentfiles; analyzers; buildtransitive"),
            new PackageReferenceSpec(
                "Lakona.Game.Server.Generators",
                "2.3.4",
                PackageReferenceStyle.Sdk,
                PrivateAssets: "all",
                OutputItemType: "Analyzer")
        };

        var xml = PackageReferenceRenderer.RenderSdkPackageReferences(references);

        Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Analyzers\" Version=\"1.2.3\">", xml, StringComparison.Ordinal);
        Assert.Contains("<PrivateAssets>all</PrivateAssets>", xml, StringComparison.Ordinal);
        Assert.Contains("<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>", xml, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Lakona.Game.Server.Generators\" Version=\"2.3.4\" PrivateAssets=\"all\" OutputItemType=\"Analyzer\" />", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNuGetForUnityPackages_RendersManualInstallFlag()
    {
        var references = new[]
        {
            new PackageReferenceSpec("Lakona.Rpc.Core", "1.2.3", PackageReferenceStyle.NuGetForUnity),
            new PackageReferenceSpec("Lakona.Rpc.Client", "2.3.4", PackageReferenceStyle.NuGetForUnity, ManuallyInstalled: true)
        };

        var xml = PackageReferenceRenderer.RenderNuGetForUnityPackages(references);

        Assert.Contains("<package id=\"Lakona.Rpc.Core\" version=\"1.2.3\" />", xml, StringComparison.Ordinal);
        Assert.Contains("<package id=\"Lakona.Rpc.Client\" version=\"2.3.4\" manuallyInstalled=\"true\" />", xml, StringComparison.Ordinal);
    }
}
