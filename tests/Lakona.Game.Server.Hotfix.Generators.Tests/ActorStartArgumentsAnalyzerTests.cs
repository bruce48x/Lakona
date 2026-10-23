using Microsoft.CodeAnalysis;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class ActorStartArgumentsAnalyzerTests
{
    private static string Hook(string type) => $$"""
        using System.Threading.Tasks;
        using Lakona.Game.Server.Hotfix.Abstractions;
        public class Hooks {
            [ActorStart] public ValueTask Start(object actor, ActorStartCall<{{type}}> call) => default;
        }
        """;

    [Theory]
    [InlineData("public class Args {}", "Args", "missing [MemoryPackable]")]
    [InlineData("[MemoryPack.MemoryPackable] public abstract partial class Args {}", "Args", "concrete class")]
    [InlineData("[MemoryPack.MemoryPackable] public partial struct Args {}", "Args", "concrete class")]
    [InlineData("[MemoryPack.MemoryPackable] public partial class Args<T> {}", "Args<int>", "must not be generic")]
    [InlineData("public class Outer<T> { [MemoryPack.MemoryPackable] public partial class Args {} }", "Outer<int>.Args", "must not be generic")]
    public async Task Rejects_invalid_root_with_specific_reason(string declaration, string type, string reason)
    {
        var reference = AnalyzerTestHost.CreateReference("StableApp", declaration);
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Hook(type), reference);
        var error = Assert.Single(diagnostics.Where(d => d.Id == "LAKONA20064"));
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains(type, error.GetMessage());
        Assert.Contains("StableApp", error.GetMessage());
        Assert.Contains(reason, error.GetMessage());
    }

    [Fact]
    public async Task Rejects_root_declared_in_current_hotfix()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Hook("Args") +
            "[MemoryPack.MemoryPackable] public partial class Args {}");
        Assert.Contains(diagnostics, d => d.Id == "LAKONA20064" && d.GetMessage().Contains("Hotfix assembly"));
    }

    [Fact]
    public async Task Rejects_referenced_hotfix_metadata()
    {
        var reference = AnalyzerTestHost.CreateReference("OtherHotfix", """
            [assembly: System.Reflection.AssemblyMetadata("LakonaProjectRole", "Hotfix")]
            [MemoryPack.MemoryPackable] public partial class Args {}
            """);
        var diagnostics = await AnalyzerTestHost.RunAsync(Hook("Args"), reference);
        Assert.Contains(diagnostics, d => d.Id == "LAKONA20064" && d.GetMessage().Contains("Hotfix assembly"));
    }

    [Fact]
    public async Task Accepts_stable_root_and_leaves_nested_serialization_to_MemoryPack()
    {
        var reference = AnalyzerTestHost.CreateReference("StableApp", """
            [MemoryPack.MemoryPackable] public partial class Args {
                public System.Collections.Generic.List<Item> Items { get; set; }
            }
            public class Item {}
            """);
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Hook("Args"), reference);
        Assert.DoesNotContain(diagnostics, d => d.Id == "LAKONA20064");
        var oldHook = Hook("Args").Replace("ActorStartCall<Args>", "ActorStartCall");
        Assert.DoesNotContain(await AnalyzerTestHost.RunHotfixProjectAsync(oldHook), d => d.Id == "LAKONA20064");
    }
}
