using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HotfixFeatureLifecycleAnalyzer : DiagnosticAnalyzer
    {
        private const string HotfixFeatureAttributeMetadataName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixFeatureAttribute";
        private const string HotfixGameFeatureMetadataName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixGameFeature";
        private const string HotfixFeatureContextMetadataName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixFeatureContext";
        private const string HotfixFeatureStartCallMetadataName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixFeatureStartCall";
        private const string HotfixFeatureStopCallMetadataName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixFeatureStopCall";
        private const string ValueTaskMetadataName = "System.Threading.Tasks.ValueTask";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(
                HotfixGeneratorDiagnostics.HotfixFeatureMustInheritHotfixGameFeature,
                HotfixGeneratorDiagnostics.HotfixFeatureConfigureShape,
                HotfixGeneratorDiagnostics.HotfixFeatureLifecycleHookShape,
                HotfixGeneratorDiagnostics.HotfixFeatureOnReloadUnsupported);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(static startContext =>
            {
                var hotfixFeatureAttribute = startContext.Compilation.GetTypeByMetadataName(HotfixFeatureAttributeMetadataName);
                var hotfixGameFeature = startContext.Compilation.GetTypeByMetadataName(HotfixGameFeatureMetadataName);
                var hotfixFeatureContext = startContext.Compilation.GetTypeByMetadataName(HotfixFeatureContextMetadataName);
                var hotfixFeatureStartCall = startContext.Compilation.GetTypeByMetadataName(HotfixFeatureStartCallMetadataName);
                var hotfixFeatureStopCall = startContext.Compilation.GetTypeByMetadataName(HotfixFeatureStopCallMetadataName);
                var valueTask = startContext.Compilation.GetTypeByMetadataName(ValueTaskMetadataName);

                if (hotfixFeatureAttribute is null ||
                    hotfixGameFeature is null ||
                    hotfixFeatureContext is null ||
                    hotfixFeatureStartCall is null ||
                    hotfixFeatureStopCall is null ||
                    valueTask is null)
                {
                    return;
                }

                startContext.RegisterSymbolAction(symbolContext =>
                {
                    AnalyzeType(
                        symbolContext,
                        (INamedTypeSymbol)symbolContext.Symbol,
                        hotfixFeatureAttribute,
                        hotfixGameFeature,
                        hotfixFeatureContext,
                        hotfixFeatureStartCall,
                        hotfixFeatureStopCall,
                        valueTask);
                }, SymbolKind.NamedType);
            });
        }

        private static void AnalyzeType(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            INamedTypeSymbol hotfixFeatureAttribute,
            INamedTypeSymbol hotfixGameFeature,
            INamedTypeSymbol hotfixFeatureContext,
            INamedTypeSymbol hotfixFeatureStartCall,
            INamedTypeSymbol hotfixFeatureStopCall,
            INamedTypeSymbol valueTask)
        {
            if (!HasHotfixFeatureAttribute(type, hotfixFeatureAttribute))
            {
                return;
            }

            var typeLocation = type.Locations.FirstOrDefault(static item => item.IsInSource);
            if (!DerivesFrom(type, hotfixGameFeature) && typeLocation is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.HotfixFeatureMustInheritHotfixGameFeature,
                    typeLocation,
                    type.ToDisplayString()));
            }

            AnalyzeConfigure(context, type, hotfixFeatureContext, typeLocation);
            AnalyzeLifecycleHook(context, type, "StartAsync", hotfixFeatureStartCall, valueTask, typeLocation);
            AnalyzeLifecycleHook(context, type, "StopAsync", hotfixFeatureStopCall, valueTask, typeLocation);
            AnalyzeOnReload(context, type, typeLocation);
        }

        private static void AnalyzeConfigure(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            INamedTypeSymbol hotfixFeatureContext,
            Location? typeLocation)
        {
            var methods = GetPublicOrdinaryMethods(type, "Configure");
            if (methods.Length == 1 && IsValidConfigure(methods[0], hotfixFeatureContext))
            {
                return;
            }

            var location = GetDiagnosticLocation(methods, typeLocation);
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.HotfixFeatureConfigureShape,
                    location,
                    type.ToDisplayString()));
            }
        }

        private static void AnalyzeLifecycleHook(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            string methodName,
            INamedTypeSymbol callType,
            INamedTypeSymbol valueTask,
            Location? typeLocation)
        {
            var methods = GetPublicOrdinaryMethods(type, methodName);
            if (methods.Length == 0)
            {
                return;
            }

            if (methods.Length == 1 && IsValidLifecycleHook(methods[0], callType, valueTask))
            {
                return;
            }

            var location = GetDiagnosticLocation(methods, typeLocation);
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.HotfixFeatureLifecycleHookShape,
                    location,
                    type.ToDisplayString(),
                    methodName,
                    callType.Name));
            }
        }

        private static void AnalyzeOnReload(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            Location? typeLocation)
        {
            var methods = GetPublicOrdinaryMethods(type, "OnReload");
            if (methods.Length == 0)
            {
                return;
            }

            var location = GetDiagnosticLocation(methods, typeLocation);
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.HotfixFeatureOnReloadUnsupported,
                    location,
                    type.ToDisplayString()));
            }
        }

        private static ImmutableArray<IMethodSymbol> GetPublicOrdinaryMethods(INamedTypeSymbol type, string name)
        {
            return type.GetMembers(name)
                .OfType<IMethodSymbol>()
                .Where(static method => method.MethodKind == MethodKind.Ordinary && method.DeclaredAccessibility == Accessibility.Public)
                .ToImmutableArray();
        }

        private static bool HasHotfixFeatureAttribute(INamedTypeSymbol type, INamedTypeSymbol hotfixFeatureAttribute)
        {
            return type.GetAttributes()
                .Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, hotfixFeatureAttribute));
        }

        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol expectedBaseType)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, expectedBaseType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidConfigure(IMethodSymbol method, INamedTypeSymbol hotfixFeatureContext)
        {
            return method.IsStatic &&
                !method.IsGenericMethod &&
                method.ReturnsVoid &&
                method.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, hotfixFeatureContext);
        }

        private static bool IsValidLifecycleHook(
            IMethodSymbol method,
            INamedTypeSymbol callType,
            INamedTypeSymbol valueTask)
        {
            return method.IsStatic &&
                !method.IsGenericMethod &&
                method.ReturnType is INamedTypeSymbol returnType &&
                returnType.Arity == 0 &&
                SymbolEqualityComparer.Default.Equals(returnType, valueTask) &&
                method.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, callType);
        }

        private static Location? GetDiagnosticLocation(ImmutableArray<IMethodSymbol> methods, Location? typeLocation)
        {
            return methods
                .SelectMany(static method => method.Locations)
                .FirstOrDefault(static location => location.IsInSource) ?? typeLocation;
        }
    }
}
