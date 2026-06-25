using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

internal static class GeneratorTestHost
{
    public static GeneratorRunResult Run(string source)
    {
        return Run(source, CreateDefaultReferences());
    }

    public static GeneratorRunResult RunWithReference(string appSource, string referencedSource)
    {
        var references = CreateDefaultReferences();
        var referencedCompilation = CSharpCompilation.Create(
            "Shared",
            new[] { CSharpSyntaxTree.ParseText(referencedSource) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = referencedCompilation.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
        }

        stream.Position = 0;
        var sharedReference = MetadataReference.CreateFromStream(stream);
        return Run(appSource, references.Concat(new[] { sharedReference }).ToArray());
    }

    private static GeneratorRunResult Run(string source, IReadOnlyList<MetadataReference> references)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
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

    private static MetadataReference[] CreateDefaultReferences()
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Concat(new[]
            {
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Server.Hotfix.Abstractions.HotfixStateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Rpc.Core.RpcServiceAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(IServiceProvider).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Rpc.Server.RpcSession).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Configuration.IConfiguration).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Cluster.NodeId).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Server.Actors.Actor<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Server.Hosting.ILakonaGameGeneratedServiceRegistration).Assembly.Location)
            })
            .Distinct(MetadataReferencePathComparer.Instance)
            .ToArray();
        return references;
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
