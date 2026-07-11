using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Lakona.Game.Server.Hotfix.Generators
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HotfixActorBoundaryAnalyzer : DiagnosticAnalyzer
    {
        private const string ActorMetadataName = "Lakona.Game.Server.Actors.Actor";
        private const string HotfixBehaviorOfMetadataName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixBehaviorOfAttribute";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(
                HotfixGeneratorDiagnostics.ActorMustNotDeclareBusinessMethod,
                HotfixGeneratorDiagnostics.HotfixBehaviorTargetMustDeriveActor,
                HotfixGeneratorDiagnostics.DuplicateHotfixBehaviorForActor,
                HotfixGeneratorDiagnostics.HotfixBehaviorMustBeStaticPartial,
                HotfixGeneratorDiagnostics.HotfixBehaviorNameMustMatchActor,
                HotfixGeneratorDiagnostics.ActorStateMemberAccessMustStayInBehavior);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(static startContext =>
            {
                var hotfixBehaviorOfAttribute = startContext.Compilation.GetTypeByMetadataName(HotfixBehaviorOfMetadataName);
                var behaviorReports = new ConcurrentBag<(string ActorDisplay, INamedTypeSymbol Behavior)>();

                startContext.RegisterSymbolAction(symbolContext =>
                {
                    var type = (INamedTypeSymbol)symbolContext.Symbol;
                    AnalyzeActorType(symbolContext, type);
                    AnalyzeBehaviorType(symbolContext, type, hotfixBehaviorOfAttribute, behaviorReports);
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

            if (!type.IsStatic || !IsPartial(type, context.CancellationToken))
            {
                if (location is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.HotfixBehaviorMustBeStaticPartial,
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
