using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

internal static class AnalyzerTestHost
{
    public static async Task<ImmutableArray<Diagnostic>> RunAsync(
        string source,
        params MetadataReference[] additionalReferences)
    {
        return await RunAsync(source, optionsProvider: null, additionalReferences).ConfigureAwait(false);
    }

    public static async Task<ImmutableArray<Diagnostic>> RunHotfixProjectAsync(
        string source,
        params MetadataReference[] additionalReferences)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.LakonaHotfixProject"] = "true"
        };
        return await RunAsync(
            source,
            new TestAnalyzerConfigOptionsProvider(options),
            additionalReferences).ConfigureAwait(false);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAsync(
        string source,
        AnalyzerConfigOptionsProvider? optionsProvider,
        params MetadataReference[] additionalReferences)
    {
        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            [CSharpSyntaxTree.ParseText(source)],
            CreateDefaultReferences().Concat(additionalReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new HotfixActorBoundaryAnalyzer());
        var analyzerOptions = optionsProvider is null
            ? null
            : new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, optionsProvider);
        return await compilation.WithAnalyzers(analyzers, analyzerOptions)
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);
    }

    private sealed class TestAnalyzerConfigOptionsProvider(
        IReadOnlyDictionary<string, string> options) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions globalOptions = new TestAnalyzerConfigOptions(options);

        public override AnalyzerConfigOptions GlobalOptions => globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestAnalyzerConfigOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => TestAnalyzerConfigOptions.Empty;
    }

    private sealed class TestAnalyzerConfigOptions(
        IReadOnlyDictionary<string, string> options) : AnalyzerConfigOptions
    {
        public static readonly TestAnalyzerConfigOptions Empty = new(
            new Dictionary<string, string>(StringComparer.Ordinal));

        public override bool TryGetValue(string key, out string value)
        {
            if (options.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }

    public static MetadataReference CreateReference(string assemblyName, string source)
    {
        return CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source)],
                CreateDefaultReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .ToMetadataReference();
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
