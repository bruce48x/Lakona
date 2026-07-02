using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixFeatureLifecycleAnalyzerTests
{
    [Fact]
    public async Task Allows_valid_static_hotfix_feature_lifecycle_shape()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixFeature("arena")]
            public sealed class ArenaFeature : HotfixGameFeature
            {
                public static void Configure(HotfixFeatureContext context)
                {
                }

                public static ValueTask StartAsync(HotfixFeatureStartCall call)
                {
                    return default;
                }

                public static ValueTask StopAsync(HotfixFeatureStopCall call)
                {
                    return default;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_hotfix_feature_that_does_not_inherit_hotfix_game_feature()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixFeature("arena")]
            public sealed class ArenaFeature
            {
                public static void Configure(HotfixFeatureContext context)
                {
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX022");
        Assert.Contains("must inherit Lakona.Game.Server.Hotfix.Abstractions.HotfixGameFeature", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_missing_configure()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixFeature("arena")]
            public sealed class ArenaFeature : HotfixGameFeature
            {
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX023");
        Assert.Contains("must declare exactly one public static void Configure(HotfixFeatureContext context)", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_abstract_hotfix_feature()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixFeature("arena")]
            public abstract class ArenaFeature : HotfixGameFeature
            {
                public static void Configure(HotfixFeatureContext context)
                {
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX026");
        Assert.Contains("must be a concrete class", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_open_generic_hotfix_feature()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixFeature("arena")]
            public sealed class ArenaFeature<T> : HotfixGameFeature
            {
                public static void Configure(HotfixFeatureContext context)
                {
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX026");
        Assert.Contains("must be a concrete class", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_public_configure_overload()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixFeature("arena")]
            public sealed class ArenaFeature : HotfixGameFeature
            {
                public static void Configure(HotfixFeatureContext context)
                {
                }

                public static void Configure(string name)
                {
                }
            }
            """;

        var diagnostics = await AnalyzerTestHost.RunAsync(source);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX023");
        Assert.Contains("no other public Configure overloads", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal(
            GetLineNumber(source, "public static void Configure(string name)"),
            diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public async Task Reports_by_ref_configure_parameter()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixFeature("arena")]
            public sealed class ArenaFeature : HotfixGameFeature
            {
                public static void Configure(ref HotfixFeatureContext context)
                {
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX023");
        Assert.Contains("Configure(HotfixFeatureContext context)", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_invalid_start_and_stop_hooks()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixFeature("arena")]
            public sealed class ArenaFeature : HotfixGameFeature
            {
                public static void Configure(HotfixFeatureContext context)
                {
                }

                public ValueTask StartAsync(HotfixFeatureStartCall call)
                {
                    return default;
                }

                public static Task StopAsync(HotfixFeatureStopCall call)
                {
                    return Task.CompletedTask;
                }
            }
            """);

        Assert.Equal(2, diagnostics.Count(static item => item.Id == "ULGHOTFIX024"));
        Assert.Contains(diagnostics, item => item.Id == "ULGHOTFIX024" && item.GetMessage().Contains("StartAsync", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item => item.Id == "ULGHOTFIX024" && item.GetMessage().Contains("StopAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reports_by_ref_start_hook_parameter()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixFeature("arena")]
            public sealed class ArenaFeature : HotfixGameFeature
            {
                public static void Configure(HotfixFeatureContext context)
                {
                }

                public static ValueTask StartAsync(ref HotfixFeatureStartCall call)
                {
                    return default;
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX024");
        Assert.Contains("StartAsync", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_public_on_reload_hook()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixFeature("arena")]
            public sealed class ArenaFeature : HotfixGameFeature
            {
                public static void Configure(HotfixFeatureContext context)
                {
                }

                public static ValueTask OnReload(HotfixFeatureStartCall call)
                {
                    return default;
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX025");
        Assert.Contains("declares public OnReload", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static int GetLineNumber(string source, string text)
    {
        var index = source.IndexOf(text, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Could not find '{text}' in source.");
        return source[..index].Count(static item => item == '\n');
    }
}
