using System.Text.RegularExpressions;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class PublicComponentSkillExamplesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resource_examples_compile_with_real_app_hotfix_generators_and_analyzers(bool hasState)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lakona.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var document = File.ReadAllText(Path.Combine(directory.FullName,
            "skills", "lakona-implement-component", "references", "resource-patterns.md"));
        var examples = Regex.Matches(document, @"(?m)^```csharp\r?\n(?<code>[\s\S]*?)^```")
            .Select(match => match.Groups["code"].Value).ToArray();
        Assert.Equal(3, examples.Length);
        var appSource = hasState ? examples[1] : string.Empty;
        var hotfixSource = hasState ? examples[2] : examples[0];

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource, hotfixSource, "Example.App", "Example.Hotfix",
            new Dictionary<string, string> { ["build_property.LakonaProjectRole"] = "ServerApp" },
            new Dictionary<string, string> { ["build_property.LakonaProjectRole"] = "Hotfix" });
        Assert.Empty(result.App.ErrorDiagnostics);
        Assert.Empty(result.Hotfix.ErrorDiagnostics);

        Assert.Empty(await AnalyzerTestHost.RunProjectRoleAsync(appSource, "ServerApp"));
        var appReference = AnalyzerTestHost.CreateReference("Example.App", appSource);
        Assert.Empty(await AnalyzerTestHost.RunProjectRoleAsync(hotfixSource, "Hotfix", appReference));
    }
}
