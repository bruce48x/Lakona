using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lakona.Game.Server.Hotfix.Generators
{
    public sealed partial class HotfixGenerator
    {
        private const string HotfixTimerAttributeName =
            "Lakona.Game.Server.Hotfix.Abstractions.HotfixTimerAttribute";

        private static IEnumerable<HotfixTimerInfo> DiscoverHotfixTimers(
            Compilation compilation,
            CancellationToken cancellationToken)
        {
            foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!HasAttribute(type, HotfixTimerAttributeName))
                {
                    continue;
                }

                var declaration = type.DeclaringSyntaxReferences
                    .Select(static reference => reference.GetSyntax())
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault();
                if (declaration is not null)
                {
                    yield return new HotfixTimerInfo(type, declaration);
                }
            }
        }

        private static void GenerateTimerEntries(SourceProductionContext context, HotfixTimerInfo[] timers)
        {
            foreach (var timer in timers)
            {
                var location = timer.Type.Locations.FirstOrDefault(static item => item.IsInSource);
                if (timer.Type.TypeKind != TypeKind.Class ||
                    timer.Type.IsStatic ||
                    !timer.Type.IsSealed ||
                    timer.Type.TypeParameters.Length != 0 ||
                    timer.Type.ContainingType is not null ||
                    HasFileModifier(timer.Declaration) ||
                    !IsPartial(timer.Declaration))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.HotfixTimerMustBeSealedPartial,
                        location,
                        timer.Type.ToDisplayString()));
                    continue;
                }

                var methods = new List<HotfixTimerMethodInfo>();
                foreach (var method in timer.Type.GetMembers().OfType<IMethodSymbol>())
                {
                    if (method.MethodKind != MethodKind.Ordinary ||
                        method.DeclaredAccessibility != Accessibility.Public ||
                        IsDisposeMethod(method))
                    {
                        continue;
                    }

                    if (!TryCreateTimerMethod(timer.Type, method, out var methodInfo))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            HotfixGeneratorDiagnostics.HotfixTimerMethodShape,
                            method.Locations.FirstOrDefault(),
                            method.ToDisplayString()));
                        continue;
                    }

                    methods.Add(methodInfo!);
                }

                foreach (var duplicate in methods
                    .GroupBy(static method => method.Name)
                    .Where(static group => group.Count() > 1))
                {
                    foreach (var method in duplicate)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            HotfixGeneratorDiagnostics.HotfixTimerMethodShape,
                            method.Location,
                            method.Name));
                    }
                }
            }
        }

        private static bool TryCreateTimerMethod(
            INamedTypeSymbol callbackType,
            IMethodSymbol method,
            out HotfixTimerMethodInfo? info)
        {
            info = null;
            if (method.IsStatic ||
                method.TypeParameters.Length != 0 ||
                method.ReturnType.ToDisplayString() != "System.Threading.Tasks.ValueTask" ||
                method.Parameters.Length != 1 ||
                method.Parameters[0].RefKind != RefKind.None ||
                method.Parameters[0].Type is not INamedTypeSymbol { IsGenericType: true } tickType ||
                tickType.ConstructedFrom.ToDisplayString() != "Lakona.Game.Server.Hotfix.Abstractions.Timers.TimerTick<TArgs>")
            {
                return false;
            }

            var argsType = tickType.TypeArguments[0];
            var methodKey = "timer:" + GetRuntimeTypeIdentity(callbackType) +
                "|method:" + method.Name +
                "|args:" + GetRuntimeTypeIdentity(argsType);
            info = new HotfixTimerMethodInfo(
                method.Name,
                argsType,
                methodKey,
                method.Locations.FirstOrDefault());
            return true;
        }

        private static bool IsDisposeMethod(IMethodSymbol method)
        {
            return method.Parameters.Length == 0 &&
                (method.Name == "Dispose" && method.ReturnsVoid ||
                 method.Name == "DisposeAsync" &&
                 method.ReturnType.ToDisplayString() == "System.Threading.Tasks.ValueTask");
        }

        private sealed class HotfixTimerInfo
        {
            public HotfixTimerInfo(INamedTypeSymbol type, TypeDeclarationSyntax declaration)
            {
                Type = type;
                Declaration = declaration;
            }

            public INamedTypeSymbol Type { get; }

            public TypeDeclarationSyntax Declaration { get; }
        }

        private sealed class HotfixTimerMethodInfo
        {
            public HotfixTimerMethodInfo(
                string name,
                ITypeSymbol argsType,
                string methodKey,
                Location? location)
            {
                Name = name;
                ArgsType = argsType;
                MethodKey = methodKey;
                Location = location;
            }

            public string Name { get; }

            public ITypeSymbol ArgsType { get; }

            public string MethodKey { get; }

            public Location? Location { get; }
        }
    }
}
