using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class HotfixGeneratorArchitectureRepositoryTests
{
    [Fact]
    public void Hotfix_generator_implementation_is_split_by_product()
    {
        var generatorRoot = Path.Combine(
            GitChangeSetReader.FindRepositoryRoot(),
            "src",
            "Lakona.Game.Server.Hotfix.Generators");

        var entry = Read(generatorRoot, "HotfixGenerator.cs");
        Assert.Contains("void Initialize(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void GenerateActorContracts(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void GenerateRpcServices(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void GenerateTimerEntries(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void GenerateComponentRegistration(", entry, StringComparison.Ordinal);

        AssertOwnsProduct(
            generatorRoot,
            "HotfixActorGenerator.cs",
            "private static void GenerateActorContracts(",
            "private static void GenerateRpcServices(",
            "private static void GenerateTimerEntries(",
            "private static void GenerateComponentRegistration(");
        AssertOwnsProduct(
            generatorRoot,
            "HotfixRpcServiceGenerator.cs",
            "private static void GenerateRpcServices(",
            "private static void GenerateActorContracts(",
            "private static void GenerateTimerEntries(",
            "private static void GenerateComponentRegistration(");
        AssertOwnsProduct(
            generatorRoot,
            "HotfixTimerGenerator.cs",
            "private static void GenerateTimerEntries(",
            "private static void GenerateActorContracts(",
            "private static void GenerateRpcServices(",
            "private static void GenerateComponentRegistration(");
        AssertOwnsProduct(
            generatorRoot,
            "HotfixComponentGenerator.cs",
            "private static void GenerateComponentRegistration(",
            "private static void GenerateActorContracts(",
            "private static void GenerateRpcServices(",
            "private static void GenerateTimerEntries(");
    }

    private static void AssertOwnsProduct(
        string generatorRoot,
        string fileName,
        string ownedEntryPoint,
        params string[] foreignEntryPoints)
    {
        var source = Read(generatorRoot, fileName);
        Assert.Contains(ownedEntryPoint, source, StringComparison.Ordinal);
        foreach (var foreignEntryPoint in foreignEntryPoints)
        {
            Assert.DoesNotContain(foreignEntryPoint, source, StringComparison.Ordinal);
        }
    }

    private static string Read(string generatorRoot, string fileName)
    {
        var path = Path.Combine(generatorRoot, fileName);
        Assert.True(File.Exists(path), $"Required Hotfix generator product file is missing: {fileName}");
        return File.ReadAllText(path);
    }
}
