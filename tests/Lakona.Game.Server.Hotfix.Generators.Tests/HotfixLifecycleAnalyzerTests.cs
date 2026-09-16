using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixLifecycleAnalyzerTests
{
    private const string Imports = """
        using System.Threading.Tasks;
        using Lakona.Game.Server.Hotfix;
        using Lakona.Game.Server.Hotfix.Abstractions;
        """;

    [Fact]
    public async Task Requires_real_interface_implementation()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync(Imports + """

            [HotfixLifecycle] public sealed class Hooks
            {
                public ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call) => default;
            }
            """);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LKNHOTFIX059");
    }

    [Theory]
    [InlineData("object")]
    [InlineData("HotfixServiceCall<object>")]
    public async Task Rejects_non_lifecycle_contract_parameters(string parameter)
    {
        var diagnostics = await AnalyzerTestHost.RunAsync(Imports + $$"""

            public interface IHooks { ValueTask Run({{parameter}} call); }
            [HotfixLifecycle] public sealed class Hooks : IHooks
            { public ValueTask Run({{parameter}} call) => default; }
            """);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LKNHOTFIX059");
    }

    [Fact]
    public async Task Accepts_inherited_and_multiple_interfaces_with_explicit_implementations()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync(Imports + """

            public interface IBase { ValueTask Run(HotfixLifecycleCall<object> call); }
            public interface IDerived : IBase { }
            public interface IOther { ValueTask Run(HotfixLifecycleCall<object> call); }
            [HotfixLifecycle] public sealed class Hooks : IDerived, IOther
            {
                ValueTask IBase.Run(HotfixLifecycleCall<object> call) => default;
                ValueTask IOther.Run(HotfixLifecycleCall<object> call) => default;
            }
            """);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Accepts_multiple_methods_without_any_method_attributes()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync(Imports + """

            public interface IHooks
            {
                ValueTask First(HotfixLifecycleCall<object> call);
                ValueTask Second(HotfixLifecycleCall<object> call);
            }
            [HotfixLifecycle] public sealed class Hooks : IHooks
            {
                public ValueTask First(HotfixLifecycleCall<object> call) => default;
                public ValueTask Second(HotfixLifecycleCall<object> call) => default;
            }
            """);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }
}
