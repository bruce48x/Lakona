using System.Text.Json;
using FrameworkBenchmark.Contracts;
using FrameworkBenchmark.Coordinator;
using Xunit;

namespace FrameworkBenchmark.Tests;

public sealed class ContractFileTests
{
    [Fact]
    public void CheckedInSchemasAreValidJsonDocuments()
    {
        var schemaDirectory = Path.Combine(TestPaths.BenchmarkRoot, "schemas");

        foreach (var path in Directory.GetFiles(schemaDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            Assert.True(document.RootElement.TryGetProperty("$schema", out _), path);
        }
    }

    [Theory]
    [InlineData("smoke.json", 6)]
    [InlineData("v1.json", 48)]
    public void CheckedInSuitesLoadAndExpand(string fileName, int expectedCases)
    {
        var suite = BenchmarkJson.Read<BenchmarkSuite>(Path.Combine(TestPaths.BenchmarkRoot, "suites", fileName));

        var cases = SuiteExpander.Expand(suite);

        Assert.Equal(expectedCases, cases.Count);
    }
}
