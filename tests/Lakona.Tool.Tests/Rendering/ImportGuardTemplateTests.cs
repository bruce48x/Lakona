using Lakona.Tool.Rendering.Client;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class ImportGuardTemplateTests
{
    [Fact]
    public void RenderNuGetPackageImportGuard_ContainsForbiddenTfmDisableRules()
    {
        var source = UnityClientCodeTemplates.RenderNuGetPackageImportGuard();

        Assert.Contains("net10.0", source, StringComparison.Ordinal);
        Assert.Contains("net8.0", source, StringComparison.Ordinal);
        Assert.Contains("SetCompatibleWithAnyPlatform(false)", source, StringComparison.Ordinal);
        Assert.Contains("SetCompatibleWithEditor(false)", source, StringComparison.Ordinal);
        Assert.Contains("SetCompatibleWithPlatform", source, StringComparison.Ordinal);
        Assert.Contains("BuildTarget", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNuGetPackageImportGuard_ContainsAllowedTfmEnableRules()
    {
        var source = UnityClientCodeTemplates.RenderNuGetPackageImportGuard();

        Assert.Contains("PreferredRuntimeTfm", source, StringComparison.Ordinal);
        Assert.Contains("netstandard2.1", source, StringComparison.Ordinal);
        Assert.Contains("SetCompatibleWithAnyPlatform(true)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNuGetPackageImportGuard_PrefersNetstandard21OverNetstandard20()
    {
        var source = UnityClientCodeTemplates.RenderNuGetPackageImportGuard();

        Assert.Contains("FallbackRuntimeTfm", source, StringComparison.Ordinal);
        Assert.Contains("netstandard2.0", source, StringComparison.Ordinal);
        Assert.Contains("HasHigherPriorityRuntimeSibling", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNuGetPackageImportGuard_RunsSynchronousScanOnLoad()
    {
        var source = UnityClientCodeTemplates.RenderNuGetPackageImportGuard();

        Assert.Contains("ApplyNuGetPluginPolicy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("delayCall += DisableExistingAnalyzerPlugins", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderNuGetPackageImportGuard_StillDisablesAnalyzers()
    {
        var source = UnityClientCodeTemplates.RenderNuGetPackageImportGuard();

        Assert.Contains("/analyzers/", source, StringComparison.Ordinal);
        Assert.Contains("IsAnalyzerOrGeneratorPlugin", source, StringComparison.Ordinal);
        Assert.Contains("KnownAnalyzerPackageIds", source, StringComparison.Ordinal);
    }
}
