using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

internal static class AnalyzerTestHost
{
    public static async Task<ImmutableArray<Diagnostic>> RunAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(CancellationToken).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Server.Actors.Actor).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new HotfixActorBoundaryAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync().ConfigureAwait(false);
    }
}
