using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

internal static class GeneratorTestHost
{
    public static GeneratorRunResult Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Concat(new[]
            {
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Server.Hotfix.Abstractions.HotfixStateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch).Assembly.Location)
            })
            .Distinct(MetadataReferencePathComparer.Instance)
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HotfixGenerator();
        CSharpGeneratorDriver.Create(generator).RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updated,
            out var diagnostics);

        return new GeneratorRunResult(
            string.Join(
                Environment.NewLine,
                updated.SyntaxTrees.Skip(1).Select(static tree => tree.ToString())),
            diagnostics,
            updated.GetDiagnostics());
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

internal sealed record GeneratorRunResult(
    string GeneratedSource,
    IReadOnlyList<Diagnostic> GeneratorDiagnostics,
    IReadOnlyList<Diagnostic> CompilationDiagnostics)
{
    public IReadOnlyList<Diagnostic> ErrorDiagnostics =>
        GeneratorDiagnostics.Concat(CompilationDiagnostics)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
}
