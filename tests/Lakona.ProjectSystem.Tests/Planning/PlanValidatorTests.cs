using Lakona.ProjectSystem.Generation.Planning;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Planning;

public sealed class PlanValidatorTests
{
    [Fact]
    public void Validate_RejectsDuplicatePaths()
    {
        var plan = new GenerationPlan(
            "Root",
            [
                new GeneratedFile("Shared/Shared.csproj", "a", FileWriteMode.Replace, GeneratedFileKind.Project),
                new GeneratedFile("Shared/Shared.csproj", "b", FileWriteMode.Replace, GeneratedFileKind.Project)
            ],
            [],
            []);

        var result = PlanValidator.Validate(plan);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "LAKONA50001");
    }

    public static TheoryData<string, string> InvalidPaths => new()
    {
        { "../escape.txt", "LAKONA50002" },
        { string.Concat("Server", "/Server", "/Server.csproj"), "LAKONA50003" },
        { "Client/Assets/Scripts/Rpc/Generated/Foo.cs", "LAKONA50004" }
    };

    [Theory]
    [MemberData(nameof(InvalidPaths))]
    public void Validate_RejectsInvalidPaths(string relativePath, string code)
    {
        var plan = new GenerationPlan(
            "Root",
            [new GeneratedFile(relativePath, "content", FileWriteMode.Replace, GeneratedFileKind.Text)],
            [],
            []);

        var result = PlanValidator.Validate(plan);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    public static TheoryData<string, string> ForbiddenGeneratedContent => new()
    {
        { string.Concat("Rpc", "Starter"), "LAKONA50005" },
        { "\"Cluster\": { \"Enabled\": true }", "LAKONA50006" },
        { "\"Hotfix\": { \"Enabled\": true }", "LAKONA50006" },
        { "\"ReliablePush\": { \"Enabled\": true }", "LAKONA50006" }
    };

    [Theory]
    [MemberData(nameof(ForbiddenGeneratedContent))]
    public void Validate_RejectsForbiddenGeneratedContent(string content, string code)
    {
        var plan = new GenerationPlan(
            "Root",
            [new GeneratedFile("Server/App/appsettings.json", content, FileWriteMode.Replace, GeneratedFileKind.Json)],
            [],
            []);

        var result = PlanValidator.Validate(plan);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }
}
