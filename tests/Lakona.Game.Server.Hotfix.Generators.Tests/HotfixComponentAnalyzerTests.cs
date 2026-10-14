using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixComponentAnalyzerTests
{
    private const string Imports = "using System.Threading; using System.Threading.Tasks; using Lakona.Game.Server.Hotfix.Abstractions; ";

    [Theory]
    [InlineData("public void Configure(IServiceCollection services, IConfiguration config) {}")]
    [InlineData("private static void Configure(IServiceCollection services, IConfiguration config) {}")]
    [InlineData("public static void Configure<T>(IServiceCollection services, IConfiguration config) {}")]
    [InlineData("public static Task Configure(IServiceCollection services, IConfiguration config) => Task.CompletedTask;")]
    [InlineData("public static async void Configure(IServiceCollection services, IConfiguration config) { await Task.Yield(); }")]
    [InlineData("public static void Configure(IServiceCollection services) {}")]
    [InlineData("public static void Configure(IConfiguration config, IServiceCollection services) {}")]
    [InlineData("public static void Configure(ref IServiceCollection services, IConfiguration config) {}")]
    [InlineData("public static void Configure(IServiceCollection services, IConfiguration config = null) {}")]
    public async Task Component_rejects_invalid_service_configuration(string method)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports
            + "using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Configuration; "
            + "[HotfixComponent] public sealed class Component { [ComponentConfigureServices] " + method + " }", "Hotfix");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061");
    }

    [Theory]
    [InlineData("ServerApp", "[ComponentState]", true)]
    [InlineData("ServerApp", "", true)]
    [InlineData("Hotfix", "[HotfixComponent]", false)]
    public async Task Service_configuration_requires_hotfix_component(string role, string marker, bool fails)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports
            + "using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Configuration; "
            + marker + " public sealed class State { [ComponentConfigureServices] public static void Register(IServiceCollection services, IConfiguration config) {} }", role);
        Assert.Equal(fails, diagnostics.Any(diagnostic => diagnostic.Id == "LAKONA20061"));
    }

    [Fact]
    public async Task Component_rejects_duplicate_service_configuration()
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            [HotfixComponent] public sealed class Component
            {
                [ComponentConfigureServices] public static void First(IServiceCollection services, IConfiguration config) {}
                [ComponentConfigureServices] public static void Second(IServiceCollection services, IConfiguration config) {}
            }
            """, "Hotfix");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061");
    }

    [Theory]
    [InlineData("public Task Start(CancellationToken token) => Task.CompletedTask;")]
    [InlineData("public ValueTask Start() => default;")]
    [InlineData("public static ValueTask Start(CancellationToken token) => default;")]
    [InlineData("public ValueTask Start(CancellationToken token = default) => default;")]
    [InlineData("public ValueTask Start(ref CancellationToken token) => default;")]
    [InlineData("public ValueTask Start<T>(CancellationToken token) => default;")]
    [InlineData("private ValueTask Start(CancellationToken token) => default;")]
    [InlineData("[ComponentStop] public ValueTask Start(CancellationToken token) => default;")]
    public async Task Rejects_invalid_lifecycle_signature(string method)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + "[HotfixComponent] public sealed class C { [ComponentStart] " + method + " }");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061");
    }

    [Theory]
    [InlineData("")]
    [InlineData("[ComponentStart] public ValueTask Begin(CancellationToken token) => default;")]
    [InlineData("[ComponentStop] public ValueTask End(CancellationToken token) => default;")]
    public async Task Allows_optional_hooks_and_no_state(string hook)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + "[HotfixComponent] public sealed class C { " + hook + " }");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id.StartsWith("LAKONA2006", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Hotfix", "public sealed class State { }", true)]
    [InlineData("ServerApp", "public class State { }", true)]
    [InlineData("ServerApp", "public sealed class State<T> { }", true)]
    [InlineData("ServerApp", "public sealed class State { public State(int value) {} }", true)]
    [InlineData("ServerApp", "public sealed class State { }", false)]
    public async Task State_requires_stable_project_and_simple_construction(string role, string declaration, bool fails)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + "[ComponentState] " + declaration, role);
        Assert.Equal(fails, diagnostics.Any(diagnostic => diagnostic.Id == "LAKONA20062"));
    }

    [Theory]
    [InlineData("public System.Action Callback;")]
    [InlineData("public Task Pending;")]
    [InlineData("public System.IServiceProvider Services;")]
    public async Task State_rejects_obvious_execution_references(string member)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + "[ComponentState] public sealed class State { " + member + " }", "ServerApp");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20062");
    }

    [Fact]
    public async Task State_allows_mechanical_async_disposal()
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + "[ComponentState] public sealed class State : System.IAsyncDisposable { public ValueTask DisposeAsync() => default; }", "ServerApp");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "LAKONA20062");
    }

    [Fact]
    public async Task State_rejects_multiple_owners_foreign_access_and_manual_creation()
    {
        var reference = AnalyzerTestHost.CreateReference("StableApp", Imports + "[ComponentState] public sealed class State { public int Value; }");
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + """
            [HotfixComponent] public sealed class A(State state) { public int Read() => state.Value; }
            [HotfixComponent] public sealed class B(State state) { }
            public sealed class Stranger
            {
                public int Read(State state) => state.Value;
                public void Create() { _ = new State(); }
            }
            """, reference);
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "LAKONA20061"));
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "LAKONA20063"));
    }
}
