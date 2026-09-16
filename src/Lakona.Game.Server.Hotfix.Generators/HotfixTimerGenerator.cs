using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Lakona.Game.Server.Hotfix.Generators.GeneratorSymbolFacts;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class HotfixTimerGenerator
    {
        internal static void Register(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(context.CompilationProvider, ValidateActorTimers);
        }

        private static void ValidateActorTimers(SourceProductionContext context, Compilation compilation)
        {
            foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
            {
                var behavior = type.GetAttributes().FirstOrDefault(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == "Lakona.Game.Server.Hotfix.Abstractions.HotfixBehaviorOfAttribute");
                var actorType = behavior is not null && behavior.ConstructorArguments.Length == 1
                    ? behavior.ConstructorArguments[0].Value as ITypeSymbol : null;
                foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                {
                    if (!HasAttribute(method, "Lakona.Game.Server.Hotfix.Abstractions.ActorTimerAttribute")) continue;
                    var valid = IsActorType(actorType) && !method.IsStatic && !method.IsGenericMethod
                        && method.ReturnType.ToDisplayString() == "System.Threading.Tasks.ValueTask"
                        && method.Parameters.Length == 2
                        && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, actorType)
                        && method.Parameters.All(parameter => parameter.RefKind == RefKind.None)
                        && method.Parameters[1].Type is INamedTypeSymbol tick
                        && tick.OriginalDefinition.ToDisplayString() == "Lakona.Game.Server.Hotfix.Timers.TimerTick<TArgs>"
                        && !method.GetAttributes().Any(attribute => attribute.AttributeClass?.Name is
                            "ActorMethodAttribute" or "ActorIgnoreAttribute" or "ActorStartAttribute" or "ActorStopAttribute");
                    if (!valid)
                        context.ReportDiagnostic(Diagnostic.Create(HotfixGeneratorDiagnostics.ActorTimerMethodShape,
                            method.Locations.FirstOrDefault(), method.ToDisplayString()));
                    else
                        HotfixTimerArgsValidation.Validate(context, compilation, method,
                            ((INamedTypeSymbol)method.Parameters[1].Type).TypeArguments[0]);
                }
            }
        }

        private static bool IsActorType(ITypeSymbol? type)
        {
            for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
            {
                if (current.ToDisplayString() == "Lakona.Game.Server.Actors.Actor") return true;
            }
            return false;
        }

    }
}
