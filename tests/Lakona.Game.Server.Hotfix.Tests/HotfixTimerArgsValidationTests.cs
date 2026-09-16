using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Testing.TimerArgs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixTimerArgsValidationTests
{
    [Theory]
    [MemberData(nameof(TimerArgsValidationCases.Cases), MemberType = typeof(TimerArgsValidationCases))]
    public async Task Candidate_validation_rejects_invalid_args_without_running_analyzers(string argsType, string? expectedCode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-timer-args-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Hotfix.dll");
            Emit(path, "int");
            await using var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(directory, "Hotfix.dll"));
            var original = await manager.ReloadAsync(TestContext.Current.CancellationToken);
            Assert.True(original.Succeeded, original.ErrorMessage);
            Emit(path, argsType);
            var validation = await manager.ValidateAsync(TestContext.Current.CancellationToken);
            Assert.Equal(expectedCode is null, validation.Succeeded);
            Assert.Equal(original.Current.DispatchTableVersion, manager.Current.DispatchTableVersion);
            var reload = await manager.ReloadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(expectedCode is null, reload.Succeeded);
            if (expectedCode is not null)
            {
                Assert.Contains("ValidationBehavior.Tick", validation.ErrorMessage);
                Assert.Equal(original.Current.DispatchTableVersion, manager.Current.DispatchTableVersion);
                Assert.Contains(expectedCode == "LKNHOTFIX055" ? "stable assembly" : "Timer args", reload.ErrorMessage);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Emit(string path, string argsType)
    {
        var compilation = CSharpCompilation.Create("TimerValidationHotfix",
            [CSharpSyntaxTree.ParseText(TimerArgsValidationCases.Source(argsType))],
            HotfixTestMetadataReferences.CreateDefaultReferences(typeof(ValidationActor)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
    }
}
