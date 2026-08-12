using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static Lakona.Game.Server.Hotfix.Generators.GeneratorSymbolFacts;
using static Lakona.Game.Server.Hotfix.Generators.HotfixActorGenerator;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class ActorAccessEmitter
    {
        internal static void Append(StringBuilder builder, HotfixActorApiInfo[] contracts)
        {
            var hasStartupActors = contracts.Any(static contract => contract.StartupKeyType is not null);

            builder.AppendLine("namespace Lakona.Game.Server.Hotfix");
            builder.AppendLine("{");
            AppendActorAccessRoot(builder, contracts, hasStartupActors);
            builder.AppendLine();
            ActorSelectorEmitter.AppendLocalActorSelector(builder);
            builder.AppendLine();
            ActorRouteEmitter.AppendActorRouteSelector(builder);
            builder.AppendLine();
            ActorSelectorEmitter.AppendActorPlacementSelector(builder);
            if (hasStartupActors)
            {
                builder.AppendLine();
                StartupActorEmitter.AppendStartupActorSelector(builder);
            }

            builder.AppendLine();
            AppendGeneratedActorMetadata(builder, contracts);
            builder.AppendLine("}");
        }

        private static void AppendActorAccessRoot(
            StringBuilder builder,
            HotfixActorApiInfo[] contracts,
            bool hasStartupActors)
        {
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// Provides generated, strongly typed selectors for calling and provisioning logical actors.");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("/// <remarks>");
            builder.AppendLine("/// Use Route for normal actor calls, Local only after proving current-node ownership,");
            builder.AppendLine("/// Place for cluster-aware activation creation, and Startup for registered startup actors.");
            builder.AppendLine("/// Selecting an actor does not implicitly create it.");
            builder.AppendLine("/// </remarks>");
            builder.AppendLine("public sealed class ActorAccess");
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            builder.AppendLine("    private readonly global::System.IServiceProvider _services;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.RemoteActorOptions _options;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorPlacementService _placement;");
            if (hasStartupActors)
            {
                builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IStartupActorInvoker _startup;");
            }

            builder.AppendLine();
            builder.AppendLine("    /// <summary>");
            builder.AppendLine("    /// Initializes the generated actor access root. Application dependency injection supplies these services.");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine("    public ActorAccess(");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorRuntime runtime,");
            builder.AppendLine("        global::System.IServiceProvider services,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.RemoteActorOptions options,");
            builder.Append("        global::Lakona.Game.Server.Actors.IActorPlacementService placement");
            if (hasStartupActors)
            {
                builder.AppendLine(",");
                builder.AppendLine("        global::Lakona.Game.Server.Actors.IStartupActorInvoker startup)");
            }
            else
            {
                builder.AppendLine(")");
            }

            builder.AppendLine("    {");
            builder.AppendLine("        _runtime = runtime;");
            builder.AppendLine("        _services = services;");
            builder.AppendLine("        _options = options;");
            builder.AppendLine("        _placement = placement;");
            if (hasStartupActors)
            {
                builder.AppendLine("        _startup = startup;");
            }

            builder.AppendLine("    }");

            foreach (var contract in DistinctActorKeyContracts(contracts))
            {
                var keyType = contract.KeyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var accessibility = ResolveKeyOverloadAccessibility(contracts, contract.KeyType);
                builder.AppendLine();
                builder.AppendLine("    /// <summary>");
                builder.AppendLine("    /// Selects an actor known to be hosted by the current process.");
                builder.AppendLine("    /// </summary>");
                builder.AppendLine("    /// <typeparam name=\"TActor\">The actor implementation type.</typeparam>");
                builder.AppendLine("    /// <param name=\"id\">The actor's stable business key.</param>");
                builder.AppendLine("    /// <returns>A selector that dispatches only to the current process.</returns>");
                builder.AppendLine("    /// <remarks>This selector never performs directory lookup or remote routing.</remarks>");
                builder.Append("    ").Append(accessibility).Append(" LocalActor<TActor> Local<TActor>(").Append(keyType).AppendLine(" id)");
                builder.Append("        where TActor : global::Lakona.Game.Server.Actors.Actor<").Append(keyType).AppendLine(">");
                builder.AppendLine("    {");
                builder.Append("        return new LocalActor<TActor>(this, global::Lakona.Game.Server.Actors.ActorIdentity.Create<TActor, ").Append(keyType).AppendLine(">(id));");
                builder.AppendLine("    }");
                builder.AppendLine();
                builder.AppendLine("    /// <summary>");
                builder.AppendLine("    /// Selects an existing logical actor and routes calls to its current activation.");
                builder.AppendLine("    /// </summary>");
                builder.AppendLine("    /// <typeparam name=\"TActor\">The actor implementation type.</typeparam>");
                builder.AppendLine("    /// <param name=\"id\">The actor's stable business key.</param>");
                builder.AppendLine("    /// <returns>A directory-backed local-or-remote call selector.</returns>");
                builder.AppendLine("    /// <remarks>This selector does not create a missing actor.</remarks>");
                builder.Append("    ").Append(accessibility).Append(" ActorRoute<TActor> Route<TActor>(").Append(keyType).AppendLine(" id)");
                builder.Append("        where TActor : global::Lakona.Game.Server.Actors.Actor<").Append(keyType).AppendLine(">");
                builder.AppendLine("    {");
                builder.Append("        return new ActorRoute<TActor>(this, global::Lakona.Game.Server.Actors.ActorIdentity.Create<TActor, ").Append(keyType).AppendLine(">(id));");
                builder.AppendLine("    }");
                builder.AppendLine();
                builder.AppendLine("    /// <summary>");
                builder.AppendLine("    /// Selects a logical actor for cluster-aware activation creation.");
                builder.AppendLine("    /// </summary>");
                builder.AppendLine("    /// <typeparam name=\"TActor\">The actor implementation type.</typeparam>");
                builder.AppendLine("    /// <param name=\"id\">The actor's stable business key.</param>");
                builder.AppendLine("    /// <returns>A selector exposing strict creation and idempotent ensure operations.</returns>");
                builder.AppendLine("    /// <remarks>Placement selects a host but never relocates an existing activation.</remarks>");
                builder.Append("    ").Append(accessibility).Append(" ActorPlacement<TActor, ").Append(keyType).Append("> Place<TActor>(").Append(keyType).AppendLine(" id)");
                builder.Append("        where TActor : global::Lakona.Game.Server.Actors.Actor<").Append(keyType).AppendLine(">");
                builder.AppendLine("    {");
                builder.Append("        return new ActorPlacement<TActor, ").Append(keyType).AppendLine(">(_placement, id);");
                builder.AppendLine("    }");
            }

            foreach (var contract in DistinctStartupKeyContracts(contracts))
            {
                var keyType = contract.StartupKeyType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var accessibility = ResolveStartupKeyOverloadAccessibility(contracts, contract.StartupKeyType);
                builder.AppendLine();
                builder.AppendLine("    /// <summary>");
                builder.AppendLine("    /// Selects an actor whose activation is owned by a registered startup actor group.");
                builder.AppendLine("    /// </summary>");
                builder.AppendLine("    /// <typeparam name=\"TActor\">The startup actor implementation type.</typeparam>");
                builder.AppendLine("    /// <param name=\"key\">The registered startup group key.</param>");
                builder.AppendLine("    /// <returns>A selector routed through startup ownership.</returns>");
                builder.Append("    ").Append(accessibility).Append(" StartupActor<TActor, ").Append(keyType).Append("> Startup<TActor>(").Append(keyType).AppendLine(" key)");
                builder.AppendLine("        where TActor : global::Lakona.Game.Server.Actors.Actor");
                builder.AppendLine("    {");
                builder.Append("        return new StartupActor<TActor, ").Append(keyType).AppendLine(">(_startup, this, key);");
                builder.AppendLine("    }");
            }

            builder.AppendLine();
            builder.AppendLine("    internal global::Lakona.Game.Server.Actors.IActorRuntime Runtime => _runtime;");
            builder.AppendLine("    internal TModule GetModule<TModule>() where TModule : class => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<TModule>(_services);");
            builder.AppendLine("    internal global::Lakona.Game.Server.Hotfix.IHotfixRuntimeAccessor HotfixRuntime => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Hotfix.IHotfixRuntimeAccessor>(_services);");
            builder.AppendLine("    internal global::Lakona.Game.Server.Actors.IRemoteActorInvoker Remote => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Actors.IRemoteActorInvoker>(_services);");
            builder.AppendLine("    internal global::Lakona.Game.Server.Actors.RemoteActorOptions Options => _options;");
            builder.AppendLine("    internal global::Lakona.Game.Server.Actors.IActorDirectory Directory => GetModule<global::Lakona.Game.Server.Actors.IActorDirectory>();");
            builder.AppendLine("    internal global::Lakona.Game.Server.Actors.IActorDirectoryCache DirectoryCache => GetModule<global::Lakona.Game.Server.Actors.IActorDirectoryCache>();");
            builder.AppendLine("}");
        }

        private static IEnumerable<HotfixActorApiInfo> DistinctActorKeyContracts(HotfixActorApiInfo[] contracts)
        {
            var keys = new List<ITypeSymbol>();
            foreach (var contract in contracts)
            {
                if (keys.Any(key => SymbolEqualityComparer.Default.Equals(key, contract.KeyType)))
                {
                    continue;
                }

                keys.Add(contract.KeyType);
                yield return contract;
            }
        }

        private static IEnumerable<HotfixActorApiInfo> DistinctStartupKeyContracts(HotfixActorApiInfo[] contracts)
        {
            var keys = new List<ITypeSymbol>();
            foreach (var contract in contracts)
            {
                if (contract.StartupKeyType is null ||
                    keys.Any(key => SymbolEqualityComparer.Default.Equals(key, contract.StartupKeyType)))
                {
                    continue;
                }

                keys.Add(contract.StartupKeyType);
                yield return contract;
            }
        }

        private static string ResolveKeyOverloadAccessibility(HotfixActorApiInfo[] contracts, ITypeSymbol keyType)
        {
            return contracts.Any(contract =>
                SymbolEqualityComparer.Default.Equals(contract.KeyType, keyType) &&
                contract.ApiAccessibility == "public")
                ? "public"
                : "internal";
        }

        private static string ResolveStartupKeyOverloadAccessibility(HotfixActorApiInfo[] contracts, ITypeSymbol keyType)
        {
            return contracts.Any(contract =>
                SymbolEqualityComparer.Default.Equals(contract.StartupKeyType, keyType) &&
                contract.ApiAccessibility == "public")
                ? "public"
                : "internal";
        }

        internal static void AppendActorCallApi(StringBuilder builder)
        {
            builder.AppendLine("    /// <summary>Calls an actor behavior and returns its reply.</summary>");
            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorEntry<TActor, TRequest, TResult> method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        var actorMethod = new global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod(method.MethodName, method.MethodId, method.PassCancellationToken);");
            builder.AppendLine("        return CallCoreAsync<TRequest, TResult>(actorMethod, request, cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    /// <summary>Calls a completion-aware actor behavior that has no reply value.</summary>");
            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorEntry<TActor, TRequest> method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        var actorMethod = new global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod(method.MethodName, method.MethodId, method.PassCancellationToken);");
            builder.AppendLine("        return CallCoreAsync(actorMethod, request, cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    /// <summary>Posts an actor behavior and completes after local or remote acceptance.</summary>");
            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorEntry<TActor, TRequest> method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        var actorMethod = new global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod(method.MethodName, method.MethodId, method.PassCancellationToken);");
            builder.AppendLine("        return PostCoreAsync(actorMethod, request, cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
        }

        private static void AppendGeneratedActorMetadata(StringBuilder builder, HotfixActorApiInfo[] contracts)
        {
            builder.AppendLine("internal static class GeneratedActorMetadata<TActor>");
            builder.AppendLine("    where TActor : global::Lakona.Game.Server.Actors.Actor");
            builder.AppendLine("{");
            builder.AppendLine("    public static readonly string ActorName = ResolveActorName();");
            builder.AppendLine();
            builder.AppendLine("    public static global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod ResolveBehaviorMethod(");
            builder.AppendLine("        global::System.Delegate method,");
            builder.AppendLine("        global::System.Type requestType,");
            builder.AppendLine("        global::System.Type resultType)");
            builder.AppendLine("    {");
            builder.AppendLine("        var methodInfo = method.Method;");
            builder.AppendLine("        var declaringTypeName = methodInfo.DeclaringType?.FullName;");
            builder.AppendLine("        var methodName = methodInfo.Name;");
            builder.AppendLine();

            foreach (var contract in contracts)
            {
                var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var behaviorTypeName = GetRuntimeTypeFullName(contract.Behavior);
                builder.Append("        if (typeof(TActor) == typeof(").Append(actorType).AppendLine("))");
                builder.AppendLine("        {");
                foreach (var method in contract.Methods)
                {
                    var requestType = method.RequestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var resultType = method.ResultType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    builder.Append("            if (declaringTypeName == \"").Append(EscapeStringLiteral(behaviorTypeName)).AppendLine("\"");
                    builder.Append("                && methodName == \"").Append(EscapeStringLiteral(method.Name)).AppendLine("\"");
                    builder.Append("                && requestType == typeof(").Append(requestType).AppendLine(")");
                    if (resultType is null)
                    {
                        builder.AppendLine("                && resultType == null)");
                    }
                    else
                    {
                        builder.Append("                && resultType == typeof(").Append(resultType).AppendLine("))");
                    }

                    builder.AppendLine("            {");
                    builder.AppendLine("                return new global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod(");
                    builder.Append("                    \"").Append(EscapeStringLiteral(method.WireName)).AppendLine("\",");
                    builder.Append("                    ").Append(GetRemoteMethodId(method)).AppendLine("UL,");
                    builder.Append(method.HasCancellationToken ? "                    true" : "                    false").AppendLine(");");
                    builder.AppendLine("            }");
                    builder.AppendLine();
                }

                builder.Append("            throw new global::System.ArgumentException(\"The supplied behavior method is not a generated actor behavior method for ")
                    .Append(EscapeStringLiteral(contract.Actor.Name))
                    .AppendLine(".\", nameof(method));");
                builder.AppendLine("        }");
                builder.AppendLine();
            }

            builder.AppendLine("        throw new global::System.ArgumentException(\"The actor type is not part of the generated actor API.\", nameof(TActor));");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static string ResolveActorName()");
            builder.AppendLine("    {");
            foreach (var contract in contracts)
            {
                var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                builder.Append("        if (typeof(TActor) == typeof(").Append(actorType).AppendLine("))");
                builder.AppendLine("        {");
                builder.Append("            return \"").Append(EscapeStringLiteral(ResolveActorName(contract.Actor))).AppendLine("\";");
                builder.AppendLine("        }");
                builder.AppendLine();
            }

            builder.AppendLine("        throw new global::System.ArgumentException(\"The actor type is not part of the generated actor API.\", nameof(TActor));");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }
    }
}
