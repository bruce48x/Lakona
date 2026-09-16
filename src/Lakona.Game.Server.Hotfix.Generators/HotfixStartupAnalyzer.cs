using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HotfixStartupAnalyzer : DiagnosticAnalyzer
{
    private const string AttributeNamespace = "Lakona.Game.Server.Hotfix.Abstractions.";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        HotfixGeneratorDiagnostics.StartupTypeShape,
        HotfixGeneratorDiagnostics.DuplicateStartupRoot,
        HotfixGeneratorDiagnostics.StartupRootRequired,
        HotfixGeneratorDiagnostics.StartupMethodShape,
        HotfixGeneratorDiagnostics.DuplicateStartupMethod);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var rootAttribute = start.Compilation.GetTypeByMetadataName(AttributeNamespace + "HotfixStartupAttribute");
            if (rootAttribute is null) return;
            var actorsAttribute = start.Compilation.GetTypeByMetadataName(AttributeNamespace + "HotfixConfigureActorsAttribute");
            var servicesAttribute = start.Compilation.GetTypeByMetadataName(AttributeNamespace + "HotfixConfigureServicesAttribute");
            var actorBuilder = start.Compilation.GetTypeByMetadataName(AttributeNamespace + "ActorHostBuilder");
            var serviceCollection = start.Compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection");
            var roots = new ConcurrentBag<INamedTypeSymbol>();
            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                var isRoot = HasAttribute(type, rootAttribute);
                if (isRoot)
                {
                    roots.Add(type);
                    if (!type.IsStatic || type.TypeKind != TypeKind.Class || !IsVisibleNonGeneric(type))
                        Report(symbolContext, HotfixGeneratorDiagnostics.StartupTypeShape, type, type.ToDisplayString());
                }

                CheckMethods(symbolContext, type, isRoot, actorsAttribute, servicesAttribute, actorBuilder);
                CheckMethods(symbolContext, type, isRoot, servicesAttribute, actorsAttribute, serviceCollection);
            }, SymbolKind.NamedType);
            start.RegisterCompilationEndAction(end =>
            {
                var ordered = roots.OrderBy(type => type.ToDisplayString(), StringComparer.Ordinal).ToArray();
                if (ordered.Length < 2) return;
                var names = string.Join(", ", ordered.Select(type => type.ToDisplayString()));
                foreach (var root in ordered)
                    end.ReportDiagnostic(Diagnostic.Create(HotfixGeneratorDiagnostics.DuplicateStartupRoot,
                        root.Locations.FirstOrDefault(location => location.IsInSource), names));
            });
        });
    }

    private static bool IsVisibleNonGeneric(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility != Accessibility.Public || current.Arity != 0 || current.IsFileLocal)
                return false;
        return true;
    }

    private static void CheckMethods(SymbolAnalysisContext context, INamedTypeSymbol type, bool isRoot,
        INamedTypeSymbol? attribute, INamedTypeSymbol? otherAttribute, INamedTypeSymbol? parameterType)
    {
        if (attribute is null) return;
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.PartialDefinitionPart is null && HasAttribute(method, attribute)).ToArray();
        foreach (var method in methods)
        {
            if (!isRoot)
                Report(context, HotfixGeneratorDiagnostics.StartupRootRequired, method, method.ToDisplayString());
            if (methods.Length > 1)
                Report(context, HotfixGeneratorDiagnostics.DuplicateStartupMethod, method,
                    type.ToDisplayString(), attribute.Name.Replace("Attribute", ""));
            if (method.DeclaredAccessibility != Accessibility.Public || !method.IsStatic || method.IsGenericMethod ||
                method.IsAsync || method.PartialImplementationPart?.IsAsync == true || !method.ReturnsVoid ||
                method.Parameters.Length != 1 || method.Parameters[0].RefKind != RefKind.None ||
                !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, parameterType) ||
                HasAttribute(method, otherAttribute))
                Report(context, HotfixGeneratorDiagnostics.StartupMethodShape, method,
                    method.ToDisplayString(), attribute.Name.Replace("Attribute", ""), parameterType?.ToDisplayString() ?? "required parameter");
        }
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attribute) => attribute is not null &&
        symbol.GetAttributes().Any(item => SymbolEqualityComparer.Default.Equals(item.AttributeClass, attribute));

    private static void Report(SymbolAnalysisContext context, DiagnosticDescriptor descriptor, ISymbol symbol, params object[] args) =>
        context.ReportDiagnostic(Diagnostic.Create(descriptor, symbol.Locations.FirstOrDefault(location => location.IsInSource), args));
}
