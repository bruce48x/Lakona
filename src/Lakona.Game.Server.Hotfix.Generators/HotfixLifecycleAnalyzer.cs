using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HotfixLifecycleAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(HotfixGeneratorDiagnostics.LifecycleContract);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!HasAttribute(type, "Lakona.Game.Server.Hotfix.Abstractions.HotfixLifecycleAttribute")) return;
        if (HasAttribute(type, "Lakona.Game.Server.Hotfix.Abstractions.HotfixServiceAttribute"))
            Report("cannot combine [HotfixLifecycle] and [HotfixService]");
        var contracts = type.AllInterfaces.Where(contract => Methods(contract).Any(method =>
            method.Parameters.Any(parameter => IsLifecycleCall(parameter.Type)))).ToArray();
        if (contracts.Length == 0)
            Report("implement a lifecycle interface with methods accepting HotfixLifecycleCall<TRequest>");
        foreach (var contract in contracts)
        {
            foreach (var method in Methods(contract))
            {
                var valid = !method.IsStatic && !method.IsGenericMethod && method.MethodKind == MethodKind.Ordinary &&
                    !method.ReturnsByRef && !method.ReturnsByRefReadonly &&
                    method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.None &&
                    IsLifecycleCall(method.Parameters[0].Type) &&
                    method.ReturnType is INamedTypeSymbol result &&
                    result.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" && result.Name == "ValueTask";
                if (!valid)
                    Report($"contract method '{method.ToDisplayString()}' must be an instance non-generic method accepting HotfixLifecycleCall<TRequest> and returning ValueTask or ValueTask<TResult>");
            }
        }

        void Report(string reason) => context.ReportDiagnostic(Diagnostic.Create(
            HotfixGeneratorDiagnostics.LifecycleContract,
            type.Locations.FirstOrDefault(location => location.IsInSource), type.ToDisplayString(), reason));
    }

    private static IEnumerable<IMethodSymbol> Methods(INamedTypeSymbol contract) =>
        contract.AllInterfaces.Concat(new[] { contract }).SelectMany(item => item.GetMembers().OfType<IMethodSymbol>());

    private static bool IsLifecycleCall(ITypeSymbol type) => type is INamedTypeSymbol named &&
        named.OriginalDefinition.ToDisplayString() == "Lakona.Game.Server.Hotfix.HotfixLifecycleCall<TRequest>";

    private static bool HasAttribute(ISymbol symbol, string name) =>
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == name);
}
