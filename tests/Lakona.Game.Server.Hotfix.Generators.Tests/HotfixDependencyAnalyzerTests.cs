using Microsoft.CodeAnalysis;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixDependencyAnalyzerTests
{
    private const string Cycle = """
        using System.Collections.Generic;
        using Lakona.Game.Server.Hotfix.Abstractions;
        using Microsoft.Extensions.DependencyInjection;
        [HotfixComponent] public sealed class A { public A(B b) {} }
        [HotfixComponent] public sealed class B { public B(A a) {} }
        """;

    [Fact]
    public async Task Default_component_cycle_is_an_error_with_dependency_chain()
    {
        var diagnostic = Assert.Single(await Analyze(Cycle));
        Assert.Equal("LKNHOTFIX057", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("A -> B -> A", diagnostic.GetMessage());
        Assert.True(diagnostic.Location.IsInSource);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("IEnumerable<A>")]
    public async Task Detects_direct_and_enumerable_self_dependency(string dependency)
    {
        var source = Cycle[..Cycle.IndexOf("[HotfixComponent]", StringComparison.Ordinal)] +
            $"[HotfixComponent] public sealed class A {{ public A({dependency} value) {{}} }}";
        var diagnostic = Assert.Single(await Analyze(source));
        Assert.Equal("LKNHOTFIX057", diagnostic.Id);
        Assert.Contains("A -> A", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("""
        [HotfixStartup] public static class Startup
        {
            [HotfixConfigureServices] public static void Configure(IServiceCollection services)
                => services.AddSingleton<A>(_ => new A(null!));
        }
        """)]
    [InlineData("""
        public sealed class CustomRegistration : Lakona.Game.Server.Hotfix.IHotfixGeneratedServiceRegistration
        {
            public void Register(IServiceCollection services) => services.AddSingleton<A>(_ => new A(null!));
        }
        """)]
    public async Task Custom_registration_downgrades_default_graph_cycle_to_warning(string registration)
    {
        var diagnostic = Assert.Single(await Analyze(Cycle + registration));
        Assert.Equal("LKNHOTFIX058", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("A -> B -> A", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Selected_constructor_avoids_false_cycle()
    {
        Assert.Empty(await Analyze(Cycle.Replace("public A(B b) {}",
            "public A(B b) {} [ActivatorUtilitiesConstructor] public A() {}")));
    }

    [Fact]
    public async Task Does_not_guess_interface_or_keyed_registration()
    {
        Assert.Empty(await Analyze(Cycle.Replace("public A(B b)", "public A([FromKeyedServices(\"other\")] B b)")));
        Assert.Empty(await Analyze(Cycle.Replace("public A(B b)", "public A(System.IDisposable b)")));
    }

    [Fact]
    public async Task Shared_acyclic_dependency_is_valid()
    {
        Assert.Empty(await Analyze("""
            using Lakona.Game.Server.Hotfix.Abstractions;
            [HotfixComponent] public sealed class A { public A(B b, C c) {} }
            [HotfixComponent] public sealed class B { public B(C c) {} }
            [HotfixComponent] public sealed class C { }
            """));
    }

    [Fact]
    public async Task Disconnected_cycles_are_both_reported()
    {
        var diagnostics = await Analyze(Cycle + "[HotfixComponent] public sealed class C { public C(C c) {} }");
        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("C -> C", StringComparison.Ordinal));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> Analyze(string source) =>
        AnalyzerTestHost.RunAsync(source,
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions).Assembly.Location));
}
