using Microsoft.CodeAnalysis;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixStartupAnalyzerTests
{
    private const string Imports = """
        using System.Threading.Tasks;
        using Lakona.Game.Server.Hotfix.Abstractions;
        using Microsoft.Extensions.DependencyInjection;
        """;

    [Theory]
    [InlineData("internal static class Startup")]
    [InlineData("public sealed class Startup")]
    [InlineData("public static class Startup<T>")]
    [InlineData("file static class Startup")]
    public async Task Rejects_invalid_root(string declaration)
    {
        await AssertDiagnostic(Imports + "\n[HotfixStartup] " + declaration + " {}", "LKNHOTFIX050");
    }

    [Theory]
    [InlineData("HotfixConfigureServices", "IServiceCollection")]
    [InlineData("HotfixConfigureActors", "ActorHostBuilder")]
    public async Task Requires_root_attribute(string attribute, string parameter)
    {
        await AssertDiagnostic(Imports + $$"""

            public static class Startup { [{{attribute}}] public static void Configure({{parameter}} value) {} }
            """, "LKNHOTFIX052");
    }

    [Theory]
    [InlineData("public static int Configure(IServiceCollection services) => 1;")]
    [InlineData("private static void Configure(IServiceCollection services) {}")]
    [InlineData("public static void Configure<T>(IServiceCollection services) {}")]
    [InlineData("public static async void Configure(IServiceCollection services) { await Task.Yield(); }")]
    [InlineData("public static Task Configure(IServiceCollection services) => Task.CompletedTask;")]
    [InlineData("public static void Configure(ref IServiceCollection services) {}")]
    [InlineData("public static void Configure(in IServiceCollection services) {}")]
    [InlineData("public static void Configure() {}")]
    [InlineData("public static void Configure(ServiceCollection services) {}")]
    [InlineData("public static void Configure(IServiceCollection services, int extra) {}")]
    [InlineData("[HotfixConfigureActors] public static void Configure(IServiceCollection services) {}")]
    public async Task Rejects_invalid_service_method(string method)
    {
        await AssertDiagnostic(Imports + "\n[HotfixStartup] public static class Startup { [HotfixConfigureServices] " + method + " }", "LKNHOTFIX053");
    }

    [Fact]
    public async Task Rejects_instance_actor_method()
    {
        await AssertDiagnostic(Imports + "\n[HotfixStartup] public class Startup { [HotfixConfigureActors] public void Configure(ActorHostBuilder actors) {} }", "LKNHOTFIX053");
    }

    [Theory]
    [InlineData("internal class Container")]
    [InlineData("public class Container<T>")]
    public async Task Rejects_inaccessible_or_generic_container(string container)
    {
        await AssertDiagnostic(Imports + "\n" + container + " { [HotfixStartup] public static class Startup {} }", "LKNHOTFIX050");
    }

    [Fact]
    public async Task Reports_all_conflicting_roots_in_stable_order()
    {
        var diagnostics = await Analyze(Imports + "\n[HotfixStartup] public static class Z {} [HotfixStartup] public static class A {}");
        var duplicates = diagnostics.Where(item => item.Id == "LKNHOTFIX051").ToArray();
        Assert.Equal(2, duplicates.Length);
        Assert.All(duplicates, item => Assert.Contains("A, Z", item.GetMessage()));
    }

    [Theory]
    [InlineData("HotfixConfigureServices", "IServiceCollection")]
    [InlineData("HotfixConfigureActors", "ActorHostBuilder")]
    public async Task Reports_duplicate_methods_across_partial_declarations(string attribute, string parameter)
    {
        var diagnostics = await Analyze(Imports + $$"""

            [HotfixStartup] public static partial class Startup { [{{attribute}}] public static void First({{parameter}} value) {} }
            public static partial class Startup { [{{attribute}}] public static void Second({{parameter}} value) {} }
            """);
        Assert.Equal(2, diagnostics.Count(item => item.Id == "LKNHOTFIX054"));
    }

    [Fact]
    public async Task Accepts_optional_entries_aliases_nested_root_and_helpers()
    {
        var diagnostics = await Analyze(Imports + """

            using Root = Lakona.Game.Server.Hotfix.Abstractions.HotfixStartupAttribute;
            public class Container
            {
                [Root] public static partial class Startup
                {
                    [HotfixConfigureServices] public static void Services(IServiceCollection services) => Helper(services);
                    private static void Helper(IServiceCollection services) {}
                }
                public static partial class Startup
                {
                    [HotfixConfigureActors] public static void Actors(ActorHostBuilder actors) {}
                }
            }
            """);
        Assert.Empty(diagnostics);
        Assert.Empty(await Analyze(Imports + "\n[HotfixStartup] public static class Startup {}"));
        Assert.Empty(await Analyze(Imports + "\npublic static class Helper {}"));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> Analyze(string source) =>
        AnalyzerTestHost.RunAsync(source, MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location));

    private static async Task AssertDiagnostic(string source, string id)
    {
        var diagnostics = await Analyze(source);
        Assert.DoesNotContain(diagnostics, item => item.Id == "AD0001");
        Assert.Contains(diagnostics, item => item.Id == id && item.Severity == DiagnosticSeverity.Error && item.Location.IsInSource);
    }
}
