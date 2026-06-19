using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HotfixActorBoundaryAnalyzer : DiagnosticAnalyzer
    {
        private const string ActorMetadataName = "Lakona.Game.Server.Actors.Actor";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(HotfixGeneratorDiagnostics.ActorMustNotDeclareBusinessMethod);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        }

        private static void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
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

        private static bool DerivesFromActor(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                var original = current.OriginalDefinition;
                if (string.Equals(original.ToDisplayString(), ActorMetadataName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
