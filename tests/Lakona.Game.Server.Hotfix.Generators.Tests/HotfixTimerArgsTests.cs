using Lakona.Testing.TimerArgs;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixTimerArgsTests
{
    [Theory]
    [MemberData(nameof(TimerArgsValidationCases.Cases), MemberType = typeof(TimerArgsValidationCases))]
    public void Reports_timer_args_errors_at_the_parameter(string argsType, string? expectedCode)
    {
        var result = GeneratorTestHost.Run(TimerArgsValidationCases.Source(argsType));
        Assert.DoesNotContain(result.CompilationDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (expectedCode is null)
        {
            Assert.Empty(result.GeneratorDiagnostics);
            return;
        }
        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal(expectedCode, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Contains("Tick", diagnostic.GetMessage());
    }
}
