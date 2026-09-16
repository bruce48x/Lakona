using Lakona.Game.Server.Hotfix.Generators;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixDependencyCycleTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var root in new[] { false, true })
        {
            yield return ["constructors", root, true];
            yield return ["factory", root, true];
            yield return ["enumerable", root, true];
            yield return ["interface", root, true];
            yield return ["override", root, false];
            yield return ["duplicates", root, false];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Resolves_real_registration_graph_and_recovers_after_rejection(string scenario, bool withRoot, bool fails)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-dependency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var root = new ServiceCollection().BuildServiceProvider();
        var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(directory, "Hotfix.dll"), rootServices: withRoot ? root : null);
        Task? inProgress = null;
        try
        {
            Emit(directory, "valid");
            var initial = await manager.ReloadAsync(TestContext.Current.CancellationToken);
            Assert.True(initial.Succeeded, initial.ErrorMessage);
            Emit(directory, scenario);
            var validationTask = Task.Run(async () => await manager.ValidateAsync(TestContext.Current.CancellationToken));
            inProgress = validationTask;
            var validation = await validationTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(!fails, validation.Succeeded);
            Assert.Equal(initial.Current.DispatchTableVersion, manager.Current.DispatchTableVersion);
            var reloadTask = Task.Run(async () => await manager.ReloadAsync(TestContext.Current.CancellationToken));
            inProgress = reloadTask;
            var reload = await reloadTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(!fails, reload.Succeeded);
            if (fails)
            {
                Assert.Contains("A", reload.ErrorMessage);
                Assert.Contains("->", reload.ErrorMessage);
                Assert.Equal(initial.Current.DispatchTableVersion, manager.Current.DispatchTableVersion);
            }
            Emit(directory, "valid");
            var recoveryTask = Task.Run(async () => await manager.ReloadAsync(TestContext.Current.CancellationToken));
            inProgress = recoveryTask;
            var recovery = await recoveryTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(recovery.Succeeded, recovery.ErrorMessage);
        }
        finally
        {
            // Do not turn the bounded regression failure into an unbounded shutdown wait.
            if (inProgress is null || inProgress.IsCompleted) await manager.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Emit(string directory, string scenario)
    {
        var declarations = scenario switch
        {
            "constructors" or "override" => "[HotfixComponent] public sealed class A { public A(B b) {} } [HotfixComponent] public sealed class B { public B(A a) {} }",
            "enumerable" => "[HotfixComponent] public sealed class A { public A(IEnumerable<A> all) {} }",
            "interface" => "public interface IA {} [HotfixComponent] public sealed class A : IA { public A(IA a) {} }",
            _ => "[HotfixComponent] public sealed class A {}"
        };
        var registration = scenario switch
        {
            "factory" => "services.AddSingleton<A>(provider => provider.GetRequiredService<A>());",
            "interface" => "services.AddSingleton<IA>(provider => provider.GetRequiredService<A>());",
            "override" => "services.AddSingleton<A>(provider => new A(null!));",
            "duplicates" => "services.AddSingleton<A>(provider => provider.GetRequiredService<A>()); services.AddSingleton<A>();",
            _ => ""
        };
        var parameters = scenario == "duplicates" ? "IEnumerable<A> a" : scenario == "override" ? "A a, B b" : "A a";
        var source = $$"""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using Lakona.Game.Server.Http;
            using Microsoft.Extensions.DependencyInjection;
            {{declarations}}
            [HotfixStartup] public static class Startup
            {
                [HotfixConfigureServices] public static void Configure(IServiceCollection services) { {{registration}} }
            }
            [LakonaHttpService("probe")] public sealed class ProbeService
            {
                public ProbeService({{parameters}}) {}
                [LakonaHttpEndpoint("GET", "/probe")]
                public ValueTask<LakonaHttpResponse> Get(LakonaHttpCall call) => default;
            }
            """;
        var compilation = CSharpCompilation.Create("DependencyHotfix", [CSharpSyntaxTree.ParseText(source)],
            HotfixTestMetadataReferences.CreateDefaultReferences(typeof(HotfixManager), typeof(IServiceCollection), typeof(ServiceCollectionServiceExtensions)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        CSharpGeneratorDriver.Create(new HotfixGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var emit = generated.Emit(Path.Combine(directory, "Hotfix.dll"));
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
    }
}
