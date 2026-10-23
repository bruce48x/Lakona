using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ActorStartArgumentsAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(HotfixGeneratorDiagnostics.ActorStartArgumentsContract);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.Method);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() ==
            "Lakona.Game.Server.Hotfix.Abstractions.ActorStartAttribute")) return;
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type is not INamedTypeSymbol call || call.Arity != 1 ||
                call.OriginalDefinition.ToDisplayString() != "Lakona.Game.Server.Hotfix.Abstractions.ActorStartCall<TArgs>") continue;
            var args = call.TypeArguments[0];
            if (args.TypeKind == TypeKind.Error) continue;
            var reasons = new List<string>();
            if (args.TypeKind != TypeKind.Class || args.IsAbstract)
                reasons.Add("must be a concrete class; use a non-abstract DTO class");
            for (var type = args as INamedTypeSymbol; type is not null; type = type.ContainingType)
                if (type.Arity != 0)
                {
                    reasons.Add("must not be generic or nested in a generic type; wrap the data in a non-generic DTO");
                    break;
                }
            if (!args.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "MemoryPack.MemoryPackableAttribute"))
                reasons.Add("is missing [MemoryPackable]; add the attribute and make the DTO partial");
            var assembly = args.ContainingAssembly;
            var isHotfix = assembly?.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute" &&
                attribute.ConstructorArguments.Length == 2 &&
                attribute.ConstructorArguments[0].Value as string == "LakonaProjectRole" &&
                string.Equals((attribute.ConstructorArguments[1].Value as string)?.Trim(), "Hotfix", StringComparison.OrdinalIgnoreCase)) == true;
            if (isHotfix || (SymbolEqualityComparer.Default.Equals(assembly, context.Compilation.Assembly) &&
                HotfixGeneratorOptions.From(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions).IsHotfixProject))
                reasons.Add("belongs to a Hotfix assembly; move the DTO to App or another stable shared assembly");
            if (reasons.Count != 0)
                context.ReportDiagnostic(Diagnostic.Create(HotfixGeneratorDiagnostics.ActorStartArgumentsContract,
                    parameter.Locations.FirstOrDefault(), args.ToDisplayString(), assembly?.Identity.Name ?? "<unknown>", string.Join("; ", reasons)));
        }
    }
}
