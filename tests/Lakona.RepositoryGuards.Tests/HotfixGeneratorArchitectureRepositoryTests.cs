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
        Assert.Contains(
            "public sealed class HotfixGenerator : IIncrementalGenerator",
            entry,
            StringComparison.Ordinal);
        Assert.DoesNotContain("partial class HotfixGenerator", entry, StringComparison.Ordinal);
        Assert.Contains("void Initialize(", entry, StringComparison.Ordinal);
        Assert.Contains("HotfixRpcServiceGenerator.Register(context, options);", entry, StringComparison.Ordinal);
        Assert.Contains("HotfixHttpGenerator.Register(context, options);", entry, StringComparison.Ordinal);
        Assert.Contains("HotfixActorGenerator.Register(context);", entry, StringComparison.Ordinal);
        Assert.Contains("HotfixTimerGenerator.Register(context);", entry, StringComparison.Ordinal);
        Assert.Contains("HotfixComponentGenerator.Register(context);", entry, StringComparison.Ordinal);

        foreach (var sourcePath in Directory.EnumerateFiles(generatorRoot, "*.cs"))
        {
            var source = File.ReadAllText(sourcePath);
            Assert.DoesNotContain("partial class HotfixGenerator", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class HotfixActorGenerator", source, StringComparison.Ordinal);
        }

        AssertOwnsProduct(
            generatorRoot,
            "HotfixActorGenerator.cs",
            "internal static class HotfixActorGenerator",
            "internal static void Register(",
            "private static void GenerateActorContracts(",
            "private static void GenerateRpcServices(",
            "private static void GenerateTimerEntries(",
            "private static void GenerateComponentRegistration(");
        AssertOwnsProduct(
            generatorRoot,
            "HotfixRpcServiceGenerator.cs",
            "internal static class HotfixRpcServiceGenerator",
            "internal static void Register(",
            "private static void GenerateRpcServices(",
            "private static void GenerateActorContracts(",
            "private static void GenerateTimerEntries(",
            "private static void GenerateComponentRegistration(");
        AssertOwnsProduct(
            generatorRoot,
            "HotfixTimerGenerator.cs",
            "internal static class HotfixTimerGenerator",
            "internal static void Register(",
            "private static void GenerateTimerEntries(",
            "private static void GenerateActorContracts(",
            "private static void GenerateRpcServices(",
            "private static void GenerateComponentRegistration(");
        AssertOwnsProduct(
            generatorRoot,
            "HotfixComponentGenerator.cs",
            "internal static class HotfixComponentGenerator",
            "internal static void Register(",
            "private static void GenerateComponentRegistration(",
            "private static void GenerateActorContracts(",
            "private static void GenerateRpcServices(",
            "private static void GenerateTimerEntries(");
        AssertOwnsProduct(
            generatorRoot,
            "HotfixHttpGenerator.cs",
            "internal static class HotfixHttpGenerator",
            "internal static void Register(",
            "private static void ValidateHttpServices(",
            "private static void GenerateActorContracts(",
            "private static void GenerateRpcServices(",
            "private static void GenerateTimerEntries(",
            "private static void GenerateComponentRegistration(");
    }

    private static void AssertOwnsProduct(
        string generatorRoot,
        string fileName,
        string moduleDeclaration,
        string registrationEntryPoint,
        string ownedEntryPoint,
        params string[] foreignEntryPoints)
    {
        var source = Read(generatorRoot, fileName);
        Assert.Contains(moduleDeclaration, source, StringComparison.Ordinal);
        Assert.Contains(registrationEntryPoint, source, StringComparison.Ordinal);
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
