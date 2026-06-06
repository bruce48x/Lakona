using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Rpc.Analyzers.Tests;

internal static class AnalyzerTestHelpers
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);

    public static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "TestAssembly",
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        var references = TrustedPlatformReferences()
            .Concat(ProjectReferences())
            .Concat(additionalReferences ?? Array.Empty<MetadataReference>())
            .GroupBy(static reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First());

        return CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public static GeneratorDriverRunResult RunGenerator(
        CSharpCompilation compilation,
        IDictionary<string, string>? globalOptions,
        out Compilation outputCompilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new ULinkRpcSourceGenerator() },
            parseOptions: ParseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(globalOptions));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out _);
        return driver.GetRunResult();
    }

    public static async Task<ImmutableArray<Diagnostic>> RunContractIdAnalyzerAsync(CSharpCompilation compilation)
    {
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new RpcContractIdAnalyzer());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    public static MetadataReference EmitReference(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
        {
            var errors = string.Join(Environment.NewLine, emit.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException(errors);
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    public static IReadOnlyList<Diagnostic> ErrorDiagnostics(Compilation compilation) =>
        compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

    private static IEnumerable<MetadataReference> ProjectReferences()
    {
        yield return MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(RpcClientRuntime).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(RpcServiceRegistry).Assembly.Location);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");

        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            yield return MetadataReference.CreateFromFile(path);
    }

    private sealed class TestAnalyzerConfigOptionsProvider(IDictionary<string, string>? globalOptions)
        : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _globalOptions = new TestAnalyzerConfigOptions(globalOptions);

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestAnalyzerConfigOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => TestAnalyzerConfigOptions.Empty;
    }

    private sealed class TestAnalyzerConfigOptions(IDictionary<string, string>? values) : AnalyzerConfigOptions
    {
        public static readonly TestAnalyzerConfigOptions Empty = new(null);

        public override bool TryGetValue(string key, out string value)
        {
            if (values is not null && values.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
