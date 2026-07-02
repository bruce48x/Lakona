using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixFeatureDocumentationShapeTests
{
    private static readonly string[] ActiveDocumentationAndTemplateFiles =
    [
        "docs/hotfix/architecture.md",
        "docs/hotfix/actor-behavior.md",
        "docs/configuration.md",
        "docs/actor.md",
        "docs/tool/default-experience.md",
        "src/Lakona.Game.Server.Hotfix.Abstractions/README.md",
        "src/Lakona.Tool/Rendering/Server/HotfixRenderer.cs",
    ];

    [Fact]
    public void Active_documentation_and_templates_show_only_supported_hotfix_feature_lifecycle_shape()
    {
        var combinedText = string.Join(
            Environment.NewLine,
            ActiveDocumentationAndTemplateFiles
                .Select(ReadIfPresent)
                .Where(text => text is not null));

        Assert.Contains("public static void Configure(HotfixFeatureContext context)", combinedText, StringComparison.Ordinal);
        Assert.Contains("public static async ValueTask StartAsync(HotfixFeatureStartCall call)", combinedText, StringComparison.Ordinal);
        Assert.Contains("public static async ValueTask StopAsync(HotfixFeatureStopCall call)", combinedText, StringComparison.Ordinal);
        Assert.Contains("[HotfixFeature(", combinedText, StringComparison.Ordinal);

        Assert.DoesNotContain("IHotfixFeatureConfigure", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("IHotfixFeatureStart", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("IHotfixFeatureStop", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("public override void Configure(HotfixFeatureContext", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("public override ValueTask StartAsync(HotfixFeatureStartCall", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("public override ValueTask StopAsync(HotfixFeatureStopCall", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Hotfix_architecture_documents_stable_feature_lifecycle_rules()
    {
        var architecture = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs/hotfix/architecture.md"));

        Assert.Contains("activation and removal hooks", architecture, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not every-reload hooks", architecture, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preserves `HotfixFeatureState`", architecture, StringComparison.Ordinal);
        Assert.Contains("must not contain hotfix-owned DTOs, services, delegates, or instances", architecture, StringComparison.Ordinal);
        Assert.Contains("no other public `Configure` overloads", architecture, StringComparison.Ordinal);
    }

    private static string? ReadIfPresent(string relativePath)
    {
        var path = Path.Combine(FindRepositoryRoot(), relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lakona.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from AppContext.BaseDirectory.");
    }
}
