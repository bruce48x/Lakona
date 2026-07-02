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
            CreateDefaultReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new HotfixActorBoundaryAnalyzer(),
            new HotfixFeatureLifecycleAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync().ConfigureAwait(false);
    }

    private static MetadataReference[] CreateDefaultReferences()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Concat(
            [
                MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(CancellationToken).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Server.Actors.Actor).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Server.Hotfix.Abstractions.HotfixBehaviorOfAttribute).Assembly.Location)
            ])
            .Distinct(MetadataReferencePathComparer.Instance)
            .ToArray();
    }

    private sealed class MetadataReferencePathComparer : IEqualityComparer<MetadataReference>
    {
        public static readonly MetadataReferencePathComparer Instance = new();

        public bool Equals(MetadataReference? x, MetadataReference? y)
        {
            return string.Equals(x?.Display, y?.Display, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(MetadataReference obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Display ?? string.Empty);
        }
    }
}
