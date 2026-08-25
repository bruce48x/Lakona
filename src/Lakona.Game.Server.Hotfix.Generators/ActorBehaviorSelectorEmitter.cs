using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static Lakona.Game.Server.Hotfix.Generators.GeneratorSymbolFacts;
using static Lakona.Game.Server.Hotfix.Generators.HotfixActorGenerator;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class ActorBehaviorSelectorEmitter
    {
        internal static void Append(StringBuilder builder, HotfixActorApiInfo[] contracts)
        {
            builder.AppendLine("namespace Lakona.Game.Server.Hotfix");
            builder.AppendLine("{");
            builder.AppendLine("internal static class GeneratedHotfixActorSelectorExtensions");
            builder.AppendLine("{");

            foreach (var contract in contracts)
            {
                foreach (var method in DistinctSelectorSignatures(contract.Methods))
                {
                    AppendActorSelectorOverload(builder, contract, method, "global::Lakona.Game.Server.Hotfix.ActorRoute", "CallAsync", isPost: false);
                    builder.AppendLine();
                    AppendActorSelectorOverload(builder, contract, method, "global::Lakona.Game.Server.Hotfix.LocalActor", "CallAsync", isPost: false);
                    builder.AppendLine();
                    if (method.ResultType is null)
                    {
                        AppendActorSelectorOverload(builder, contract, method, "global::Lakona.Game.Server.Hotfix.ActorRoute", "PostAsync", isPost: true);
                        builder.AppendLine();
                        AppendActorSelectorOverload(builder, contract, method, "global::Lakona.Game.Server.Hotfix.LocalActor", "PostAsync", isPost: true);
                        builder.AppendLine();
                    }

                    if (contract.StartupKeyType is not null)
                    {
                        AppendStartupSelectorOverload(builder, contract, method, "CallAsync", isPost: false);
                        builder.AppendLine();
                        if (method.ResultType is null)
                        {
                            AppendStartupSelectorOverload(builder, contract, method, "PostAsync", isPost: true);
                            builder.AppendLine();
                        }
                    }
                }
            }

            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("internal static class GeneratedHotfixActorSelectorCache");
            builder.AppendLine("{");
            builder.AppendLine("    internal static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Delegate, global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod> Methods = new();");
            builder.AppendLine("}");
            builder.AppendLine("}");
        }

        private static IEnumerable<HotfixActorMethodInfo> DistinctSelectorSignatures(
            IEnumerable<HotfixActorMethodInfo> methods)
        {
            return methods
                .GroupBy(
                    static method => CreateSelectorSignatureKey(method),
                    System.StringComparer.Ordinal)
                .Select(static group => group.First());
        }

        private static string CreateSelectorSignatureKey(HotfixActorMethodInfo method)
        {
            return GetRuntimeTypeIdentity(method.RequestType) + "|" +
                (method.ResultType is null ? "void" : GetRuntimeTypeIdentity(method.ResultType)) + "|" +
                method.HasCancellationToken;
        }

        private static void AppendActorSelectorOverload(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            HotfixActorMethodInfo method,
            string selectorType,
            string operationName,
            bool isPost)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            builder.Append("    public static ").Append(GetSelectorReturnType(method, isPost)).Append(' ').Append(operationName).AppendLine("(");
            builder.Append("        this ").Append(selectorType).Append('<').Append(actorType).AppendLine("> target,");
            AppendBehaviorSelectorParameter(builder, contract, method);
            AppendSelectorRequestParameters(builder, method);
            builder.AppendLine("    {");
            AppendSelectorBody(builder, contract, method, isPost);
            builder.AppendLine("    }");
        }

        private static void AppendStartupSelectorOverload(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            HotfixActorMethodInfo method,
            string operationName,
            bool isPost)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var keyType = contract.StartupKeyType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            builder.Append("    public static ").Append(GetSelectorReturnType(method, isPost)).Append(' ').Append(operationName).AppendLine("(");
            builder.Append("        this global::Lakona.Game.Server.Hotfix.StartupActor<").Append(actorType).Append(", ").Append(keyType).AppendLine("> target,");
            AppendBehaviorSelectorParameter(builder, contract, method);
            AppendSelectorRequestParameters(builder, method);
            builder.AppendLine("    {");
            AppendSelectorBody(builder, contract, method, isPost);
            builder.AppendLine("    }");
        }

        private static string GetSelectorReturnType(HotfixActorMethodInfo method, bool isPost)
        {
            if (isPost || method.ResultType is null)
            {
                return "global::System.Threading.Tasks.ValueTask";
            }

            return "global::System.Threading.Tasks.ValueTask<" +
                method.ResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">";
        }

        private static void AppendBehaviorSelectorParameter(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            HotfixActorMethodInfo method)
        {
            var behaviorType = contract.Behavior.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var requestType = method.RequestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var delegateName = method.ResultType is null
                ? method.HasCancellationToken ? "HotfixActorPost" : "HotfixActorPostNoCancellation"
                : method.HasCancellationToken ? "HotfixActorCall" : "HotfixActorCallNoCancellation";

            builder.Append("        [global::Lakona.Game.Server.Hotfix.Abstractions.HotfixMethodSelector] global::System.Func<").Append(behaviorType)
                .Append(", global::Lakona.Game.Server.Hotfix.Abstractions.Actors.").Append(delegateName)
                .Append('<').Append(actorType).Append(", ").Append(requestType);
            if (method.ResultType is not null)
            {
                builder.Append(", ").Append(method.ResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            builder.AppendLine(">> selector,");
        }

        private static void AppendSelectorRequestParameters(StringBuilder builder, HotfixActorMethodInfo method)
        {
            builder.Append("        ").Append(method.RequestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).AppendLine(" request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        }

        private static void AppendSelectorBody(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            HotfixActorMethodInfo method,
            bool isPost)
        {
            var behaviorType = contract.Behavior.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var requestType = method.RequestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var resultType = method.ResultType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(selector);");
            builder.AppendLine("        if (!global::Lakona.Game.Server.Hotfix.GeneratedHotfixActorSelectorCache.Methods.TryGetValue(selector, out var method))");
            builder.AppendLine("        {");
            builder.Append("            var selected = selector((").Append(behaviorType)
                .Append(")global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(")
                .Append(behaviorType).AppendLine(")));");
            builder.Append("            method = global::Lakona.Game.Server.Hotfix.GeneratedActorMetadata<").Append(actorType)
                .Append(">.ResolveBehaviorMethod(selected, typeof(").Append(requestType).Append("), ");
            builder.AppendLine(resultType is null ? "resultType: null);" : "typeof(" + resultType + "));");
            builder.AppendLine("            method = global::Lakona.Game.Server.Hotfix.GeneratedHotfixActorSelectorCache.Methods.GetOrAdd(selector, method);");
            builder.AppendLine("        }");
            builder.Append("        var entry = new global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorEntry<")
                .Append(actorType).Append(", ").Append(requestType);
            if (resultType is not null)
            {
                builder.Append(", ").Append(resultType);
            }

            builder.AppendLine(">(method.MethodName, method.RemoteMethodId, method.PassCancellationToken);");
            builder.Append("        return target.").Append(isPost ? "PostAsync" : "CallAsync").AppendLine("(entry, request, cancellationToken);");
        }
    }
}
