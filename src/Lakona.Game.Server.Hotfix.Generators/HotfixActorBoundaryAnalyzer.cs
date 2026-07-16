using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Lakona.Game.Server.Hotfix.Generators
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HotfixActorBoundaryAnalyzer : DiagnosticAnalyzer
    {
        private const string ActorMetadataName = "Lakona.Game.Server.Actors.Actor";
        private const string HotfixBehaviorOfMetadataName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixBehaviorOfAttribute";
        private const string HotfixLifecycleMetadataName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixLifecycleAttribute";
        private const string HotfixServiceMetadataName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixServiceAttribute";
        private const string HotfixTimerMetadataName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixTimerAttribute";
        private const string HotfixComponentMetadataName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixComponentAttribute";
        private const string ActivatorUtilitiesConstructorMetadataName = "Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute";
        private const string HotfixProjectKey = "build_property.LakonaHotfixProject";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(
                HotfixGeneratorDiagnostics.ActorMustNotDeclareBusinessMethod,
                HotfixGeneratorDiagnostics.HotfixBehaviorTargetMustDeriveActor,
                HotfixGeneratorDiagnostics.DuplicateHotfixBehaviorForActor,
                HotfixGeneratorDiagnostics.HotfixBehaviorMustBeSealedPartial,
                HotfixGeneratorDiagnostics.HotfixBehaviorNameMustMatchActor,
                HotfixGeneratorDiagnostics.ActorStateMemberAccessMustStayInBehavior,
                HotfixGeneratorDiagnostics.HotfixModuleMustNotOwnData,
                HotfixGeneratorDiagnostics.HotfixServiceModuleShape,
                HotfixGeneratorDiagnostics.HotfixServiceEntryMustBeInstance,
                HotfixGeneratorDiagnostics.HotfixConcreteTypeRequiresRole,
                HotfixGeneratorDiagnostics.HotfixStaticStateForbidden,
                HotfixGeneratorDiagnostics.HotfixComponentModuleShape);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(static startContext =>
            {
                var hotfixBehaviorOfAttribute = startContext.Compilation.GetTypeByMetadataName(HotfixBehaviorOfMetadataName);
                var hotfixLifecycleAttribute = startContext.Compilation.GetTypeByMetadataName(HotfixLifecycleMetadataName);
                var hotfixServiceAttribute = startContext.Compilation.GetTypeByMetadataName(HotfixServiceMetadataName);
                var hotfixTimerAttribute = startContext.Compilation.GetTypeByMetadataName(HotfixTimerMetadataName);
                var hotfixComponentAttribute = startContext.Compilation.GetTypeByMetadataName(HotfixComponentMetadataName);
                var hotfixModuleAttributes = new[]
                {
                    hotfixBehaviorOfAttribute,
                    hotfixLifecycleAttribute,
                    hotfixServiceAttribute,
                    hotfixTimerAttribute,
                    hotfixComponentAttribute
                }.Where(static attribute => attribute is not null).Cast<INamedTypeSymbol>().ToArray();
                var isHotfixProject = IsEnabled(
                    startContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions,
                    HotfixProjectKey);
                var behaviorReports = new ConcurrentBag<(string ActorDisplay, INamedTypeSymbol Behavior)>();

                startContext.RegisterSymbolAction(symbolContext =>
                {
                    var type = (INamedTypeSymbol)symbolContext.Symbol;
                    AnalyzeActorType(symbolContext, type);
                    AnalyzeBehaviorType(symbolContext, type, hotfixBehaviorOfAttribute, behaviorReports);
                    AnalyzeHotfixModuleStorage(symbolContext, type, hotfixModuleAttributes);
                    AnalyzeServiceModuleShape(symbolContext, type, hotfixServiceAttribute, hotfixLifecycleAttribute);
                    AnalyzeComponentModuleShape(symbolContext, type, hotfixComponentAttribute);
                    if (isHotfixProject)
                    {
                        AnalyzeHotfixProjectType(symbolContext, type, hotfixModuleAttributes);
                    }
                }, SymbolKind.NamedType);

                startContext.RegisterOperationAction(
                    operationContext => AnalyzeActorStateAccess(
                        operationContext,
                        ((IFieldReferenceOperation)operationContext.Operation).Field,
                        hotfixBehaviorOfAttribute),
                    OperationKind.FieldReference);
                startContext.RegisterOperationAction(
                    operationContext => AnalyzeActorStateAccess(
                        operationContext,
                        ((IPropertyReferenceOperation)operationContext.Operation).Property,
                        hotfixBehaviorOfAttribute),
                    OperationKind.PropertyReference);
                startContext.RegisterOperationAction(
                    operationContext => AnalyzePrimaryConstructorParameterWrite(
                        operationContext,
                        ((ISimpleAssignmentOperation)operationContext.Operation).Target,
                        hotfixModuleAttributes),
                    OperationKind.SimpleAssignment);
                startContext.RegisterOperationAction(
                    operationContext => AnalyzePrimaryConstructorParameterWrite(
                        operationContext,
                        ((ICompoundAssignmentOperation)operationContext.Operation).Target,
                        hotfixModuleAttributes),
                    OperationKind.CompoundAssignment);
                startContext.RegisterOperationAction(
                    operationContext => AnalyzePrimaryConstructorParameterWrite(
                        operationContext,
                        ((IIncrementOrDecrementOperation)operationContext.Operation).Target,
                        hotfixModuleAttributes),
                    OperationKind.Increment,
                    OperationKind.Decrement);
                startContext.RegisterOperationAction(
                    operationContext => AnalyzePrimaryConstructorParameterEscape(
                        operationContext,
                        (IArgumentOperation)operationContext.Operation,
                        hotfixModuleAttributes),
                    OperationKind.Argument);

                startContext.RegisterCompilationEndAction(endContext =>
                {
                    var duplicateGroups = behaviorReports
                        .GroupBy(static report => report.ActorDisplay, StringComparer.Ordinal)
                        .OrderBy(static group => group.Key, StringComparer.Ordinal)
                        .Where(static group => group.Count() > 1);

                    foreach (var group in duplicateGroups)
                    {
                        var duplicates = group
                            .OrderBy(static report => GetLocationSortKey(report.Behavior), StringComparer.Ordinal)
                            .Skip(1)
                            .Select(static report => report.Behavior);

                        foreach (var duplicate in duplicates)
                        {
                            var location = duplicate.Locations.FirstOrDefault(static item => item.IsInSource);
                            if (location is not null)
                            {
                                endContext.ReportDiagnostic(Diagnostic.Create(
                                    HotfixGeneratorDiagnostics.DuplicateHotfixBehaviorForActor,
                                    location,
                                    group.Key));
                            }
                        }
                    }
                });
            });
        }

        private static bool IsEnabled(AnalyzerConfigOptions options, string key)
        {
            if (!options.TryGetValue(key, out var value))
            {
                return false;
            }

            return string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value?.Trim(), "1", StringComparison.Ordinal);
        }

        private static void AnalyzeHotfixProjectType(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            IReadOnlyList<INamedTypeSymbol> hotfixModuleAttributes)
        {
            if (type.DeclaringSyntaxReferences.Length == 0 || type.TypeKind != TypeKind.Class)
            {
                return;
            }

            if (type.IsStatic)
            {
                AnalyzeStaticHotfixState(context, type);
                return;
            }

            if (IsHotfixModule(type, hotfixModuleAttributes))
            {
                return;
            }

            var location = type.Locations.FirstOrDefault(static item => item.IsInSource);
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.HotfixConcreteTypeRequiresRole,
                    location,
                    type.ToDisplayString()));
            }
        }

        private static void AnalyzeStaticHotfixState(SymbolAnalysisContext context, INamedTypeSymbol type)
        {
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (!field.IsImplicitlyDeclared && field.IsStatic && !field.IsConst)
                {
                    ReportStaticState(context, type, field);
                }
            }

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (!property.IsImplicitlyDeclared && property.IsStatic &&
                    IsAutoProperty(property, context.CancellationToken))
                {
                    ReportStaticState(context, type, property);
                }
            }

            foreach (var @event in type.GetMembers().OfType<IEventSymbol>())
            {
                if (!@event.IsImplicitlyDeclared && @event.IsStatic)
                {
                    ReportStaticState(context, type, @event);
                }
            }
        }

        private static void ReportStaticState(SymbolAnalysisContext context, INamedTypeSymbol type, ISymbol member)
        {
            var location = member.Locations.FirstOrDefault(static item => item.IsInSource);
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.HotfixStaticStateForbidden,
                    location,
                    type.ToDisplayString(),
                    member.Name));
            }
        }

        private static void AnalyzeComponentModuleShape(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            INamedTypeSymbol? hotfixComponentAttribute)
        {
            if (!HasAttribute(type, hotfixComponentAttribute))
            {
                return;
            }

            if (type.TypeKind == TypeKind.Class &&
                !type.IsStatic &&
                !type.IsAbstract &&
                type.IsSealed &&
                type.TypeParameters.Length == 0 &&
                type.ContainingType is null &&
                ResolveActivationConstructor(type) is not null)
            {
                return;
            }

            var location = type.Locations.FirstOrDefault(static item => item.IsInSource);
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.HotfixComponentModuleShape,
                    location,
                    type.ToDisplayString()));
            }
        }

        private static void AnalyzeServiceModuleShape(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            INamedTypeSymbol? hotfixServiceAttribute,
            INamedTypeSymbol? hotfixLifecycleAttribute)
        {
            if (!HasAttribute(type, hotfixServiceAttribute) && !HasAttribute(type, hotfixLifecycleAttribute))
            {
                return;
            }

            if (type.TypeKind != TypeKind.Class ||
                type.IsStatic ||
                type.IsAbstract ||
                !type.IsSealed ||
                type.TypeParameters.Length != 0 ||
                ResolveActivationConstructor(type) is null)
            {
                var location = type.Locations.FirstOrDefault(static item => item.IsInSource);
                if (location is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.HotfixServiceModuleShape,
                        location,
                        type.ToDisplayString()));
                }
            }

            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary ||
                    method.DeclaredAccessibility != Accessibility.Public ||
                    !method.IsStatic ||
                    IsDisposeMethod(method))
                {
                    continue;
                }

                var location = method.Locations.FirstOrDefault(static item => item.IsInSource);
                if (location is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.HotfixServiceEntryMustBeInstance,
                        location,
                        method.ToDisplayString()));
                }
            }
        }

        private static bool HasAttribute(INamedTypeSymbol type, INamedTypeSymbol? attributeType)
        {
            return attributeType is not null && type.GetAttributes().Any(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));
        }

        private static bool IsDisposeMethod(IMethodSymbol method)
        {
            return method.Parameters.Length == 0 &&
                (string.Equals(method.Name, "Dispose", StringComparison.Ordinal) ||
                 string.Equals(method.Name, "DisposeAsync", StringComparison.Ordinal));
        }

        private static void AnalyzeHotfixModuleStorage(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            IReadOnlyList<INamedTypeSymbol> hotfixModuleAttributes)
        {
            if (!IsHotfixModule(type, hotfixModuleAttributes))
            {
                return;
            }

            var activationConstructor = ResolveActivationConstructor(type);
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsImplicitlyDeclared || field.IsConst)
                {
                    continue;
                }

                if (field.IsStatic ||
                    field.DeclaredAccessibility != Accessibility.Private ||
                    !field.IsReadOnly ||
                    activationConstructor is null ||
                    !IsDirectConstructorDependency(field, activationConstructor, context.CancellationToken))
                {
                    ReportOwnedData(context, type, field);
                }
            }

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsImplicitlyDeclared || !IsAutoProperty(property, context.CancellationToken))
                {
                    continue;
                }

                if (property.IsStatic ||
                    property.DeclaredAccessibility != Accessibility.Private ||
                    property.SetMethod is not null ||
                    activationConstructor is null ||
                    !IsDirectConstructorDependency(property, activationConstructor, context.CancellationToken))
                {
                    ReportOwnedData(context, type, property);
                }
            }

            foreach (var @event in type.GetMembers().OfType<IEventSymbol>().Where(static member => !member.IsImplicitlyDeclared))
            {
                ReportOwnedData(context, type, @event);
            }
        }

        private static IMethodSymbol? ResolveActivationConstructor(INamedTypeSymbol type)
        {
            var constructors = type.InstanceConstructors
                .Where(static constructor => constructor.DeclaredAccessibility == Accessibility.Public)
                .ToArray();
            var marked = constructors
                .Where(static constructor => constructor.GetAttributes().Any(attribute =>
                    string.Equals(
                        attribute.AttributeClass?.ToDisplayString(),
                        ActivatorUtilitiesConstructorMetadataName,
                        StringComparison.Ordinal)))
                .ToArray();

            return marked.Length == 1
                ? marked[0]
                : marked.Length == 0 && constructors.Length == 1
                    ? constructors[0]
                    : null;
        }

        private static bool IsDirectConstructorDependency(
            ISymbol member,
            IMethodSymbol activationConstructor,
            System.Threading.CancellationToken cancellationToken)
        {
            var assignments = 0;
            foreach (var syntaxReference in member.DeclaringSyntaxReferences)
            {
                var syntax = syntaxReference.GetSyntax(cancellationToken);
                var initializer = syntax switch
                {
                    VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                    PropertyDeclarationSyntax property => property.Initializer?.Value,
                    _ => null
                };
                if (initializer is null)
                {
                    continue;
                }

                if (!IsDirectParameterReference(initializer, activationConstructor))
                {
                    return false;
                }

                assignments++;
            }

            foreach (var syntaxReference in activationConstructor.DeclaringSyntaxReferences)
            {
                var syntax = syntaxReference.GetSyntax(cancellationToken);
                if (syntax is not ConstructorDeclarationSyntax constructor)
                {
                    continue;
                }

                foreach (var assignment in constructor.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    if (!IsMemberAssignmentTarget(assignment.Left, member))
                    {
                        continue;
                    }

                    if (assignment.Ancestors().TakeWhile(node => node != constructor).Any(static node =>
                            node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax) ||
                        !IsDirectParameterReference(assignment.Right, activationConstructor))
                    {
                        return false;
                    }

                    assignments++;
                }
            }

            return assignments == 1;
        }

        private static bool IsMemberAssignmentTarget(ExpressionSyntax expression, ISymbol member)
        {
            return expression switch
            {
                IdentifierNameSyntax identifier => string.Equals(identifier.Identifier.ValueText, member.Name, StringComparison.Ordinal),
                MemberAccessExpressionSyntax
                {
                    Expression: ThisExpressionSyntax,
                    Name: IdentifierNameSyntax identifier
                } => string.Equals(identifier.Identifier.ValueText, member.Name, StringComparison.Ordinal),
                _ => false
            };
        }

        private static bool IsDirectParameterReference(
            ExpressionSyntax expression,
            IMethodSymbol activationConstructor)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    return IsDirectParameterReference(parenthesized.Expression, activationConstructor);
                case CastExpressionSyntax cast:
                    return IsDirectParameterReference(cast.Expression, activationConstructor);
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression):
                    return IsDirectParameterReference(postfix.Operand, activationConstructor);
                case BinaryExpressionSyntax coalesce
                    when coalesce.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceExpression) &&
                         coalesce.Right is ThrowExpressionSyntax:
                    return IsDirectParameterReference(coalesce.Left, activationConstructor);
                case IdentifierNameSyntax identifier:
                    return activationConstructor.Parameters.Any(parameter =>
                        string.Equals(parameter.Name, identifier.Identifier.ValueText, StringComparison.Ordinal));
                default:
                    return false;
            }
        }

        private static bool IsAutoProperty(IPropertySymbol property, System.Threading.CancellationToken cancellationToken)
        {
            return property.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .OfType<PropertyDeclarationSyntax>()
                .Any(static declaration =>
                    declaration.ExpressionBody is null &&
                    declaration.AccessorList is not null &&
                    declaration.AccessorList.Accessors.All(accessor => accessor.Body is null && accessor.ExpressionBody is null));
        }

        private static void AnalyzePrimaryConstructorParameterWrite(
            OperationAnalysisContext context,
            IOperation target,
            IReadOnlyList<INamedTypeSymbol> hotfixModuleAttributes)
        {
            if (target is not IParameterReferenceOperation parameterReference ||
                !IsCapturedPrimaryConstructorDependency(parameterReference.Parameter, hotfixModuleAttributes))
            {
                return;
            }

            ReportOwnedData(context, parameterReference.Parameter.ContainingType, parameterReference.Parameter);
        }

        private static void AnalyzePrimaryConstructorParameterEscape(
            OperationAnalysisContext context,
            IArgumentOperation argument,
            IReadOnlyList<INamedTypeSymbol> hotfixModuleAttributes)
        {
            if (argument.Parameter?.RefKind is not RefKind.Ref and not RefKind.Out ||
                argument.Value is not IParameterReferenceOperation parameterReference ||
                !IsCapturedPrimaryConstructorDependency(parameterReference.Parameter, hotfixModuleAttributes))
            {
                return;
            }

            ReportOwnedData(context, parameterReference.Parameter.ContainingType, parameterReference.Parameter);
        }

        private static bool IsCapturedPrimaryConstructorDependency(
            IParameterSymbol parameter,
            IReadOnlyList<INamedTypeSymbol> hotfixModuleAttributes)
        {
            return parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor &&
                IsHotfixModule(constructor.ContainingType, hotfixModuleAttributes) &&
                parameter.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax() is ParameterSyntax { Parent.Parent: TypeDeclarationSyntax });
        }

        private static bool IsHotfixModule(
            INamedTypeSymbol type,
            IReadOnlyList<INamedTypeSymbol> hotfixModuleAttributes)
        {
            return type.GetAttributes().Any(attribute =>
                attribute.AttributeClass is not null &&
                hotfixModuleAttributes.Any(moduleAttribute =>
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, moduleAttribute)));
        }

        private static void ReportOwnedData(SymbolAnalysisContext context, INamedTypeSymbol type, ISymbol member)
        {
            var location = member.Locations.FirstOrDefault(static item => item.IsInSource);
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.HotfixModuleMustNotOwnData,
                    location,
                    type.ToDisplayString(),
                    member.Name));
            }
        }

        private static void ReportOwnedData(OperationAnalysisContext context, INamedTypeSymbol type, ISymbol member)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                HotfixGeneratorDiagnostics.HotfixModuleMustNotOwnData,
                context.Operation.Syntax.GetLocation(),
                type.ToDisplayString(),
                member.Name));
        }

        private static void AnalyzeActorStateAccess(
            OperationAnalysisContext context,
            ISymbol member,
            INamedTypeSymbol? hotfixBehaviorOfAttribute)
        {
            if (member.DeclaredAccessibility == Accessibility.Public ||
                member.ContainingType is not INamedTypeSymbol actorType ||
                !DerivesFromGenericActor(actorType))
            {
                return;
            }

            var accessingType = context.ContainingSymbol?.ContainingType;
            if (SymbolEqualityComparer.Default.Equals(accessingType, actorType) ||
                (accessingType is not null && IsBehaviorOf(accessingType, actorType, hotfixBehaviorOfAttribute)))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                HotfixGeneratorDiagnostics.ActorStateMemberAccessMustStayInBehavior,
                context.Operation.Syntax.GetLocation(),
                actorType.ToDisplayString(),
                member.Name));
        }

        private static bool IsBehaviorOf(
            INamedTypeSymbol accessingType,
            INamedTypeSymbol actorType,
            INamedTypeSymbol? hotfixBehaviorOfAttribute)
        {
            if (hotfixBehaviorOfAttribute is null)
            {
                return false;
            }

            return accessingType.GetAttributes().Any(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, hotfixBehaviorOfAttribute) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is INamedTypeSymbol behaviorActor &&
                SymbolEqualityComparer.Default.Equals(behaviorActor, actorType));
        }

        private static void AnalyzeActorType(SymbolAnalysisContext context, INamedTypeSymbol type)
        {
            if (!DerivesFromActor(type))
            {
                return;
            }

            foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (member.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                if (IsAllowedLifecycleOverride(member))
                {
                    continue;
                }

                var location = member.Locations.FirstOrDefault(static item => item.IsInSource);
                if (location is null)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.ActorMustNotDeclareBusinessMethod,
                    location,
                    type.ToDisplayString(),
                    member.Name));
            }
        }

        private static void AnalyzeBehaviorType(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            INamedTypeSymbol? hotfixBehaviorOfAttribute,
            ConcurrentBag<(string ActorDisplay, INamedTypeSymbol Behavior)> behaviorReports)
        {
            if (hotfixBehaviorOfAttribute is null)
            {
                return;
            }

            var attribute = type.GetAttributes()
                .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, hotfixBehaviorOfAttribute));
            if (attribute is null ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol actor)
            {
                return;
            }

            var location = type.Locations.FirstOrDefault(static item => item.IsInSource);
            if (!DerivesFromGenericActor(actor))
            {
                if (location is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.HotfixBehaviorTargetMustDeriveActor,
                        location,
                        type.ToDisplayString(),
                        actor.ToDisplayString()));
                }

                return;
            }

            var actorDisplay = actor.ToDisplayString();
            behaviorReports.Add((actorDisplay, type));

            if (type.IsStatic || !type.IsSealed || !IsPartial(type, context.CancellationToken))
            {
                if (location is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.HotfixBehaviorMustBeSealedPartial,
                        location,
                        type.ToDisplayString()));
                }
            }

            var expectedName = GetActorPrefix(actor.Name) + "Behavior";
            if (!string.Equals(type.Name, expectedName, StringComparison.Ordinal))
            {
                if (location is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.HotfixBehaviorNameMustMatchActor,
                        location,
                        type.ToDisplayString(),
                        actor.ToDisplayString(),
                        expectedName));
                }
            }
        }

        private static bool DerivesFromActor(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                var original = current.OriginalDefinition;
                if (IsActorBaseType(original))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DerivesFromGenericActor(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                var original = current.OriginalDefinition;
                if (IsActorBaseType(original) && original.Arity == 1)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsActorBaseType(INamedTypeSymbol type)
        {
            if (!string.Equals(type.Name, "Actor", StringComparison.Ordinal))
            {
                return false;
            }

            if (type.Arity is not 0 and not 1)
            {
                return false;
            }

            return string.Equals(
                type.ContainingNamespace.ToDisplayString() + "." + type.Name,
                ActorMetadataName,
                StringComparison.Ordinal);
        }

        private static bool IsPartial(INamedTypeSymbol type, System.Threading.CancellationToken cancellationToken)
        {
            return type.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
                .Any(static declaration => declaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword));
        }

        private static string GetActorPrefix(string actorName)
        {
            const string suffix = "Actor";
            return actorName.EndsWith(suffix, StringComparison.Ordinal) && actorName.Length > suffix.Length
                ? actorName.Substring(0, actorName.Length - suffix.Length)
                : actorName;
        }

        private static string GetLocationSortKey(INamedTypeSymbol type)
        {
            var location = type.Locations.FirstOrDefault(static item => item.IsInSource);
            if (location is null)
            {
                return type.ToDisplayString();
            }

            var lineSpan = location.GetLineSpan();
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}:{1:D8}:{2:D8}:{3}",
                lineSpan.Path,
                lineSpan.StartLinePosition.Line,
                lineSpan.StartLinePosition.Character,
                type.ToDisplayString());
        }

        private static bool IsAllowedLifecycleOverride(IMethodSymbol method)
        {
            if (!method.IsOverride)
            {
                return false;
            }

            if (method.Name is not "OnActivateAsync" and not "OnDeactivateAsync")
            {
                return false;
            }

            if (method.Parameters.Length != 1)
            {
                return false;
            }

            return string.Equals(
                method.Parameters[0].Type.ToDisplayString(),
                "System.Threading.CancellationToken",
                StringComparison.Ordinal)
                && string.Equals(
                    method.ReturnType.ToDisplayString(),
                    "System.Threading.Tasks.ValueTask",
                    StringComparison.Ordinal);
        }
    }
}
