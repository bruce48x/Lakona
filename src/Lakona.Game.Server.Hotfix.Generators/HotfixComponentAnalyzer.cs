using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Lakona.Game.Server.Hotfix.Generators.GeneratorSymbolFacts;

namespace Lakona.Game.Server.Hotfix.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HotfixComponentAnalyzer : DiagnosticAnalyzer
{
    private const string Prefix = "Lakona.Game.Server.Hotfix.Abstractions.";
    private static readonly DiagnosticDescriptor Contract = new("LAKONA20061", "Invalid component contract",
        "{0}", "Lakona.Game.Hotfix", DiagnosticSeverity.Error, true, customTags: WellKnownDiagnosticTags.CompilationEnd);
    private static readonly DiagnosticDescriptor StateDeclaration = new("LAKONA20062", "Invalid component state",
        "{0}", "Lakona.Game.Hotfix", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor StateAccess = new("LAKONA20063", "Component state belongs to its owner",
        "'{0}' may only be accessed by its state class or the component that constructor-injects it", "Lakona.Game.Hotfix", DiagnosticSeverity.Error, true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Contract, StateDeclaration, StateAccess);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var options = HotfixGeneratorOptions.From(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
            var owners = new ConcurrentBag<(INamedTypeSymbol State, INamedTypeSymbol Owner)>();
            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                var component = HasAttribute(type, Prefix + "HotfixComponentAttribute");
                var state = HasAttribute(type, Prefix + "ComponentStateAttribute");
                if (state && (!options.GenerateStableRpcServices || type.TypeKind != TypeKind.Class || !type.IsSealed
                    || type.IsAbstract || type.IsGenericType || type.DeclaredAccessibility != Accessibility.Public
                    || type.ContainingType is not null || !type.InstanceConstructors.Any(ctor => ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0)))
                    Report(StateDeclaration, type, "ComponentState must be a public sealed non-generic ServerApp class with a public parameterless constructor.");
                var constructors = type.InstanceConstructors.Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public).ToArray();
                if (component && (options.GenerateStableRpcServices || constructors.Length != 1 || type.DeclaredAccessibility != Accessibility.Public))
                    Report(Contract, type, "HotfixComponent must be public, live in Hotfix, and have exactly one public constructor.");
                foreach (var constructor in constructors)
                {
                    var states = constructor.Parameters.Where(parameter => HasAttribute(parameter.Type, Prefix + "ComponentStateAttribute")).ToArray();
                    if (states.Length > (component ? 1 : 0)) Report(Contract, constructor, "Only a component may inject state, and it may own at most one state.");
                    if (component && states.Length == 1 && states[0].Type is INamedTypeSymbol stateType) owners.Add((stateType, type));
                    foreach (var parameter in constructor.Parameters)
                    {
                        if ((HasAttribute(parameter.Type, Prefix + "HotfixComponentAttribute") || HasAttribute(parameter.Type, Prefix + "ComponentStateAttribute"))
                            && (parameter.IsOptional || HasAttribute(parameter, "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute")))
                            Report(Contract, parameter, "Component and state injection must use required, unkeyed concrete parameters.");
                        if (component && parameter.Type.ToDisplayString() is "System.IServiceProvider" or "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory")
                            Report(Contract, parameter, "Component dependencies must be constructor-injected directly; service locators are unsupported.");
                        if (parameter.Type is INamedTypeSymbol named && named.OriginalDefinition.ToDisplayString() != "Microsoft.Extensions.Logging.ILogger<TCategoryName>" && named.TypeArguments.Any(argument => HasAttribute(argument, Prefix + "HotfixComponentAttribute")))
                            Report(Contract, parameter, "Inject the concrete component directly; collections and wrappers are unsupported.");
                    }
                }
                foreach (var member in type.GetMembers())
                {
                    var valueType = member switch
                    {
                        IFieldSymbol field => field.Type,
                        IPropertySymbol property => property.Type,
                        IMethodSymbol method when method.MethodKind == MethodKind.Ordinary => method.ReturnType,
                        _ => null
                    };
                    if (state && member is IFieldSymbol or IPropertySymbol && valueType is not null && ForbiddenStateType(valueType))
                        Report(StateDeclaration, member, "ComponentState cannot retain delegates, tasks, service providers, or Hotfix logic.");
                    if (!state && valueType is not null && HasAttribute(valueType, Prefix + "ComponentStateAttribute")
                        && member.DeclaredAccessibility != Accessibility.Private)
                        Report(Contract, member, "The injected state must remain private to its owning component.");
                    if (state && member is IEventSymbol)
                        Report(StateDeclaration, member, "ComponentState cannot retain callbacks or event subscriptions.");
                }
                var methods = type.GetMembers().OfType<IMethodSymbol>().ToArray();
                var configurations = methods.Where(method => method.PartialDefinitionPart is null
                    && HasAttribute(method, Prefix + "ComponentConfigureServicesAttribute")).ToArray();
                if (configurations.Length > 1)
                    Report(Contract, type, "A HotfixComponent may declare at most one ComponentConfigureServices method.");
                foreach (var method in configurations)
                    if (!component || !options.IsHotfixProject || method.DeclaredAccessibility != Accessibility.Public
                        || !method.IsStatic || method.IsGenericMethod || !method.ReturnsVoid
                        || method.IsAsync || method.PartialImplementationPart?.IsAsync == true
                        || method.Parameters.Length != 2
                        || method.Parameters[0].Type.ToDisplayString() != "Microsoft.Extensions.DependencyInjection.IServiceCollection"
                        || method.Parameters[1].Type.ToDisplayString() != "Microsoft.Extensions.Configuration.IConfiguration"
                        || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsOptional))
                        Report(Contract, method, "ComponentConfigureServices must belong to a HotfixComponent in Hotfix and be public static non-generic void(IServiceCollection, IConfiguration), without async or optional parameters.");
                var starts = methods.Where(method => HasAttribute(method, Prefix + "ComponentStartAttribute")).ToArray();
                var stops = methods.Where(method => HasAttribute(method, Prefix + "ComponentStopAttribute")).ToArray();
                if (starts.Length > 1 || stops.Length > 1) Report(Contract, type, "A component may declare at most one hook of each kind.");
                foreach (var method in starts.Concat(stops).Distinct(SymbolEqualityComparer.Default).OfType<IMethodSymbol>())
                {
                    if (!component || method.DeclaredAccessibility != Accessibility.Public || method.IsStatic || method.IsGenericMethod
                        || method.ReturnType.ToDisplayString() != "System.Threading.Tasks.ValueTask" || method.Parameters.Length != 1
                        || method.Parameters[0].Type.ToDisplayString() != "System.Threading.CancellationToken"
                        || method.Parameters[0].RefKind != RefKind.None || method.Parameters[0].IsOptional
                        || (starts.Contains(method, SymbolEqualityComparer.Default) && stops.Contains(method, SymbolEqualityComparer.Default)))
                        Report(Contract, method, "A component hook must belong to HotfixComponent and be public instance non-generic ValueTask(CancellationToken), with one lifecycle attribute.");
                }
                void Report(DiagnosticDescriptor rule, ISymbol symbol, string message) =>
                    symbolContext.ReportDiagnostic(Diagnostic.Create(rule, symbol.Locations.FirstOrDefault(location => location.IsInSource), message));
            }, SymbolKind.NamedType);

            start.RegisterOperationAction(operationContext =>
            {
                if (options.IsHotfixProject) AnalyzeUnmarkedStateWrite(operationContext);
                var member = operationContext.Operation switch
                {
                    IFieldReferenceOperation field => (ISymbol)field.Field,
                    IPropertyReferenceOperation property => property.Property,
                    IInvocationOperation invocation => invocation.TargetMethod,
                    IObjectCreationOperation creation => creation.Constructor,
                    _ => null
                };
                if (member?.ContainingType is not { } state || !HasAttribute(state, Prefix + "ComponentStateAttribute")) return;
                var caller = operationContext.ContainingSymbol.ContainingType;
                if (SymbolEqualityComparer.Default.Equals(caller, state)) return;
                var owner = caller is not null && HasAttribute(caller, Prefix + "HotfixComponentAttribute")
                    && caller.InstanceConstructors.Any(ctor => ctor.Parameters.Any(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, state)));
                if (!owner || operationContext.Operation is IObjectCreationOperation)
                    operationContext.ReportDiagnostic(Diagnostic.Create(StateAccess, operationContext.Operation.Syntax.GetLocation(), state.ToDisplayString()));
            }, OperationKind.FieldReference, OperationKind.PropertyReference, OperationKind.Invocation, OperationKind.ObjectCreation);
            start.RegisterCompilationEndAction(end =>
            {
                foreach (var group in owners.GroupBy(pair => pair.State, SymbolEqualityComparer.Default).Where(group => group.Count() > 1))
                foreach (var pair in group)
                    end.ReportDiagnostic(Diagnostic.Create(Contract, pair.Owner.Locations.FirstOrDefault(), $"Component state '{pair.State}' has more than one owner."));
            });
        });
    }

    private static void AnalyzeUnmarkedStateWrite(OperationAnalysisContext context)
    {
        var instance = context.Operation switch
        {
            IFieldReferenceOperation field => field.Instance,
            IPropertyReferenceOperation property => property.Instance,
            _ => null
        };
        if (instance is null || !IsWrite(context.Operation)) return;
        var component = context.ContainingSymbol.ContainingType;
        if (component is null || !HasAttribute(component, Prefix + "HotfixComponentAttribute")
            || HotfixActorBoundaryAnalyzer.ResolveActivationConstructor(component) is not { } constructor) return;

        var receiver = Unwrap(instance);
        var dependency = receiver switch
        {
            IParameterReferenceOperation parameter when SymbolEqualityComparer.Default.Equals(parameter.Parameter.ContainingSymbol, constructor)
                => parameter.Parameter.Type,
            IFieldReferenceOperation field when field.Instance is IInstanceReferenceOperation
                && SymbolEqualityComparer.Default.Equals(field.Field.ContainingType, component)
                && HotfixActorBoundaryAnalyzer.IsDirectConstructorDependency(field.Field, constructor, context.CancellationToken)
                => field.Field.Type,
            IPropertyReferenceOperation property when property.Instance is IInstanceReferenceOperation
                && SymbolEqualityComparer.Default.Equals(property.Property.ContainingType, component)
                && HotfixActorBoundaryAnalyzer.IsDirectConstructorDependency(property.Property, constructor, context.CancellationToken)
                => property.Property.Type,
            _ => null
        };
        if (dependency is null) return;
        var stateType = IsServerAppType(dependency) ? dependency
            : instance.Type is { } converted && IsServerAppType(converted) ? converted : null;
        if (stateType is null || !stateType.IsReferenceType || HasAttribute(stateType, Prefix + "ComponentStateAttribute")) return;
        context.ReportDiagnostic(Diagnostic.Create(StateDeclaration, context.Operation.Syntax.GetLocation(),
            $"Component '{component.ToDisplayString()}' directly writes injected ServerApp dependency '{stateType.ToDisplayString()}'. "
            + "Use an explicitly marked [ComponentState] with framework-owned registration, or use a stable resource API instead of direct state mutation."));
    }

    private static bool IsServerAppType(ITypeSymbol type) => type.ContainingAssembly?.GetAttributes().Any(attribute =>
        attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"
        && attribute.ConstructorArguments.Length == 2
        && attribute.ConstructorArguments[0].Value is "LakonaProjectRole"
        && attribute.ConstructorArguments[1].Value is string role
        && string.Equals(role.Trim(), "ServerApp", StringComparison.OrdinalIgnoreCase)) == true;

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            if (operation is IConversionOperation { OperatorMethod: null } conversion) operation = conversion.Operand;
            else if (operation is IParenthesizedOperation parenthesized) operation = parenthesized.Operand;
            else return operation;
        }
    }

    private static bool IsWrite(IOperation operation)
    {
        // Tuple targets and transparent wrappers still write the same member.
        while (operation.Parent is IConversionOperation or IParenthesizedOperation or ITupleOperation)
            operation = operation.Parent;
        return operation.Parent switch
        {
            IAssignmentOperation assignment => assignment.Target == operation,
            IIncrementOrDecrementOperation increment => increment.Target == operation,
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out,
            _ => false
        };
    }

    private static bool ForbiddenStateType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return ForbiddenStateType(array.ElementType);
        if (type is not INamedTypeSymbol named) return false;
        var name = named.OriginalDefinition.ToDisplayString();
        return named.TypeKind == TypeKind.Delegate || name is "System.Delegate" or "System.MulticastDelegate"
            or "System.IServiceProvider" or "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"
            or "System.Threading.Tasks.Task" or "System.Threading.Tasks.Task<TResult>"
            or "System.Threading.Tasks.ValueTask" or "System.Threading.Tasks.ValueTask<TResult>"
            || HasAttribute(type, Prefix + "HotfixComponentAttribute")
            || HasAttribute(type, Prefix + "HotfixBehaviorOfAttribute")
            || named.TypeArguments.Any(ForbiddenStateType);
    }
}
