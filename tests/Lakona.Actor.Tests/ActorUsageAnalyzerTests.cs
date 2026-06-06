using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Lakona.Actor.SourceGenerator.Analyzers;

namespace Lakona.Actor.Tests;

public sealed class ActorUsageAnalyzerTests
{
    [Fact]
    public async Task Analyzer_reports_self_call_inside_actor()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Lakona.Actor;

            public sealed class BadActor : IActor<object>
            {
                public async ValueTask OnMessage(ActorContext<object> ctx, object message)
                {
                    await ctx.Self.Call<string>(message, new ActorCallOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
                }
            }
            """;

        Diagnostic[] diagnostics = await GetAnalyzerDiagnostics(source);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ActorUsageAnalyzer.SelfCallDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task Analyzer_reports_blocking_waits_inside_actor()
    {
        const string source = """
            using System.Threading.Tasks;
            using System.Threading;
            using Lakona.Actor;

            public sealed class BadActor : IActor<object>
            {
                public ValueTask OnMessage(ActorContext<object> ctx, object message)
                {
                    Task.CompletedTask.Wait();
                    _ = Task.FromResult(1).Result;
                    Task.WaitAll(Task.CompletedTask);
                    Task.WaitAny(Task.CompletedTask);
                    Task.CompletedTask.GetAwaiter().GetResult();
                    Thread.Sleep(1);
                    return ValueTask.CompletedTask;
                }
            }
            """;

        Diagnostic[] diagnostics = await GetAnalyzerDiagnostics(source);

        Assert.Equal(
            [
                ActorUsageAnalyzer.BlockingWaitDiagnosticId,
                ActorUsageAnalyzer.BlockingWaitDiagnosticId,
                ActorUsageAnalyzer.BlockingWaitDiagnosticId,
                ActorUsageAnalyzer.BlockingWaitDiagnosticId,
                ActorUsageAnalyzer.BlockingWaitDiagnosticId,
                ActorUsageAnalyzer.BlockingWaitDiagnosticId
            ],
            diagnostics.Select(static diagnostic => diagnostic.Id));
    }

    [Fact]
    public async Task Analyzer_does_not_report_blocking_waits_outside_actor()
    {
        const string source = """
            using System.Threading.Tasks;
            using System.Threading;

            public sealed class RegularType
            {
                public void Run()
                {
                    Task.CompletedTask.Wait();
                    _ = Task.FromResult(1).Result;
                    Task.WaitAll(Task.CompletedTask);
                    Task.WaitAny(Task.CompletedTask);
                    Task.CompletedTask.GetAwaiter().GetResult();
                    Thread.Sleep(1);
                }
            }
            """;

        Diagnostic[] diagnostics = await GetAnalyzerDiagnostics(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyzer_does_not_report_safe_offload_inside_actor()
    {
        const string source = """
            using System.Threading.Tasks;
            using Lakona.Actor;

            public sealed class WorkerActor : IActor<object>
            {
                public ValueTask OnMessage(ActorContext<object> ctx, object message)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1);
                        await ctx.Self.Send("done");
                    });

                    return ValueTask.CompletedTask;
                }
            }
            """;

        Diagnostic[] diagnostics = await GetAnalyzerDiagnostics(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyzer_reports_discarded_actor_calls()
    {
        const string source = """
            using System;
            using Lakona.Actor;

            public sealed class RegularType
            {
                public void Run(ActorRef<object> actor)
                {
                    actor.Call<string>("ping", new ActorCallOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
                    _ = actor.Call<string>("ping", new ActorCallOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
                }
            }
            """;

        Diagnostic[] diagnostics = await GetAnalyzerDiagnostics(source);

        Assert.Equal(
            [ActorUsageAnalyzer.DiscardedCallDiagnosticId, ActorUsageAnalyzer.DiscardedCallDiagnosticId],
            diagnostics.Select(static diagnostic => diagnostic.Id));
    }

    [Fact]
    public async Task Analyzer_does_not_report_observed_actor_calls()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Lakona.Actor;

            public sealed class RegularType
            {
                public async ValueTask<string> Run(ActorRef<object> actor)
                {
                    ValueTask<string> pending = actor.Call<string>("ping", new ActorCallOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
                    return await pending;
                }
            }
            """;

        Diagnostic[] diagnostics = await GetAnalyzerDiagnostics(source);

        Assert.Empty(diagnostics);
    }

    private static async Task<Diagnostic[]> GetAnalyzerDiagnostics(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "AnalyzerTests",
            [syntaxTree],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new ActorUsageAnalyzer());

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(analyzers)
            .GetAnalyzerDiagnosticsAsync();

        return diagnostics
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        string trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            string.Empty;

        foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }

        yield return MetadataReference.CreateFromFile(typeof(ActorSystem).Assembly.Location);
    }
}
