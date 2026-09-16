using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using static Lakona.Game.Server.Hotfix.Generators.GeneratorSymbolFacts;

namespace Lakona.Game.Server.Hotfix.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HotfixDependencyAnalyzer : DiagnosticAnalyzer
{
    private const string ComponentAttribute = "Lakona.Game.Server.Hotfix.Abstractions.HotfixComponentAttribute";
    private const string ConfigureAttribute = "Lakona.Game.Server.Hotfix.Abstractions.HotfixConfigureServicesAttribute";
    private const string RegistrationInterface = "Lakona.Game.Server.Hotfix.IHotfixGeneratedServiceRegistration";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        HotfixGeneratorDiagnostics.ComponentDependencyCycle,
        HotfixGeneratorDiagnostics.PossibleComponentDependencyCycle);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(Analyze);
    }

    private static void Analyze(CompilationAnalysisContext context)
    {
        var types = EnumerateTypes(context.Compilation.Assembly.GlobalNamespace).ToArray();
        var customRegistration = types.Any(type =>
            type.GetMembers().OfType<IMethodSymbol>().Any(method => HasAttribute(method, ConfigureAttribute)) ||
            (type.AllInterfaces.Any(contract => contract.ToDisplayString() == RegistrationInterface) &&
                type.ToDisplayString() != "Lakona.Game.Server.Hotfix.Generated.GeneratedHotfixComponentRegistration"));
        var constructors = new Dictionary<INamedTypeSymbol, IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var type in types)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (type.TypeKind == TypeKind.Class && type.IsSealed && !type.IsStatic && type.Arity == 0 &&
                type.ContainingType is null && HasAttribute(type, ComponentAttribute) &&
                HotfixActorBoundaryAnalyzer.ResolveActivationConstructor(type) is { } constructor)
                constructors.Add(type, constructor);
        }

        // Iterative DFS keeps the analyzer bounded by the declared graph, including long chains.
        var states = new Dictionary<INamedTypeSymbol, byte>(SymbolEqualityComparer.Default);
        var path = new List<INamedTypeSymbol>();
        var stack = new Stack<(INamedTypeSymbol Type, IEnumerator<(INamedTypeSymbol Type, IParameterSymbol Parameter)> Edges)>();
        foreach (var root in constructors.Keys.OrderBy(type => type.ToDisplayString(), StringComparer.Ordinal))
        {
            if (states.ContainsKey(root)) continue;
            Enter(root);
            while (stack.Count != 0)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var frame = stack.Peek();
                if (!frame.Edges.MoveNext())
                {
                    frame.Edges.Dispose();
                    stack.Pop();
                    path.RemoveAt(path.Count - 1);
                    states[frame.Type] = 2;
                    continue;
                }
                var edge = frame.Edges.Current;
                if (!states.TryGetValue(edge.Type, out var state))
                {
                    Enter(edge.Type);
                    continue;
                }
                if (state != 1) continue;
                var start = path.FindIndex(type => SymbolEqualityComparer.Default.Equals(type, edge.Type));
                var chain = string.Join(" -> ", path.Skip(start).Append(edge.Type).Select(type => type.ToDisplayString()));
                context.ReportDiagnostic(Diagnostic.Create(customRegistration
                        ? HotfixGeneratorDiagnostics.PossibleComponentDependencyCycle
                        : HotfixGeneratorDiagnostics.ComponentDependencyCycle,
                    edge.Parameter.Locations.FirstOrDefault(location => location.IsInSource), chain));
            }
        }

        void Enter(INamedTypeSymbol type)
        {
            states[type] = 1;
            path.Add(type);
            stack.Push((type, Dependencies(constructors[type], constructors).GetEnumerator()));
        }
    }

    private static IEnumerable<(INamedTypeSymbol Type, IParameterSymbol Parameter)> Dependencies(
        IMethodSymbol constructor, IReadOnlyDictionary<INamedTypeSymbol, IMethodSymbol> constructors)
    {
        foreach (var parameter in constructor.Parameters)
        {
            // Keyed services and interfaces require the actual registration graph.
            if (parameter.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() is
                    "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute" or
                    "Microsoft.Extensions.DependencyInjection.ServiceKeyAttribute")) continue;
            var dependency = parameter.Type as INamedTypeSymbol;
            if (dependency?.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                dependency = dependency.TypeArguments[0] as INamedTypeSymbol;
            if (dependency is not null && constructors.ContainsKey(dependency))
                yield return (dependency, parameter);
        }
    }
}
