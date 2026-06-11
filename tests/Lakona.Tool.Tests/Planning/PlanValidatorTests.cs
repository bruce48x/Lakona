using Lakona.Tool.Planning;
using Xunit;

namespace Lakona.Tool.Tests.Planning;

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

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "LTPLAN001");
    }

    public static TheoryData<string, string> InvalidPaths => new()
    {
        { "../escape.txt", "LTPLAN002" },
        { string.Concat("Server", "/Server", "/Server.csproj"), "LTPLAN003" },
        { "Client/Assets/Scripts/Rpc/Generated/Foo.cs", "LTPLAN004" }
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
        { string.Concat("Rpc", "Starter"), "LTPLAN005" },
        { string.Concat("ULink", "RPC"), "LTPLAN005" },
        { string.Concat("ULink", "Game"), "LTPLAN005" },
        { "\"Cluster\": { \"Enabled\": true }", "LTPLAN006" },
        { "\"Hotfix\": { \"Enabled\": true }", "LTPLAN006" },
        { "\"ReliablePush\": { \"Enabled\": true }", "LTPLAN006" }
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
