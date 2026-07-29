using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Lakona.Game.Server.Hotfix.Generators.GeneratorSymbolFacts;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class HotfixActorGenerator
    {
        private const string HotfixBehaviorOfAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixBehaviorOfAttribute";
        private const string ActorStartAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.ActorStartAttribute";
        private const string ActorStopAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.ActorStopAttribute";
        private const string ActorHostBuilderName = "Lakona.Game.Server.Hotfix.Abstractions.ActorHostBuilder";

        private static readonly DiagnosticDescriptor UnsupportedHotfixBehaviorWrapperTarget = new DiagnosticDescriptor(
            "LKNHOTFIX021",
            "Hotfix behavior cannot receive generated actor ref wrappers",
            "Hotfix behavior '{0}' cannot receive generated actor entries because generation requires a non-file-local, non-generic, top-level sealed partial class",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        internal static void Register(IncrementalGeneratorInitializationContext context)
        {
            var contracts = context.CompilationProvider
                .Select(static (compilation, cancellationToken) =>
                    new HotfixActorGenerationInput(
                        compilation.Assembly.Identity.Name,
                        DiscoverHotfixBehaviors(compilation, cancellationToken).ToArray(),
                        DiscoverStartupRegistrations(compilation, cancellationToken).ToArray()));

            context.RegisterSourceOutput(contracts, GenerateActorContracts);
        }

        private static void GenerateActorContracts(SourceProductionContext context, HotfixActorGenerationInput input)
        {
            if (input.Behaviors.Length == 0)
            {
                return;
            }

            var supported = new List<HotfixActorApiInfo>();

            foreach (var actorGroup in input.Behaviors
                .GroupBy(static behavior => behavior.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), System.StringComparer.Ordinal))
            {
                var behaviors = actorGroup.ToArray();
                if (behaviors.Length > 1)
                {
                    foreach (var duplicate in behaviors.Skip(1))
                    {
                        var location = duplicate.Behavior.Locations.FirstOrDefault(static item => item.IsInSource);
                        context.ReportDiagnostic(Diagnostic.Create(
                            HotfixGeneratorDiagnostics.DuplicateHotfixBehaviorForActor,
                            location,
                            duplicate.Actor.ToDisplayString()));
                    }

                    continue;
                }

                var behavior = behaviors[0];
                if (IsUnsupportedBehaviorWrapperTarget(behavior))
                {
                    var location = behavior.Behavior.Locations.FirstOrDefault(static item => item.IsInSource);
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedHotfixBehaviorWrapperTarget,
                        location,
                        behavior.Behavior.ToDisplayString()));
                    continue;
                }

                var startupKeyType = input.StartupRegistrations
                    .Where(registration => SymbolEqualityComparer.Default.Equals(registration.Actor, behavior.Actor))
                    .Select(static registration => registration.KeyType)
                    .FirstOrDefault();
                var contract = CreateBehaviorActorContract(behavior, input.AssemblyName, startupKeyType);
                foreach (var diagnostic in contract.Diagnostics)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        diagnostic.Descriptor,
                        diagnostic.Location,
                        diagnostic.Arguments));
                }

                if (contract.IsSupported)
                {
                    supported.Add(contract);
                }
            }

            var supportedArray = supported
                .OrderBy(static contract => contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .ToArray();

            if (supportedArray.Length == 0)
            {
                return;
            }

            context.AddSource(
                "GeneratedHotfixActorRefs.g.cs",
                SourceText.From(
                    GenerateActorContractsSource(supportedArray),
                    Encoding.UTF8));
        }

        private static bool IsUnsupportedBehaviorWrapperTarget(HotfixBehaviorInfo behavior)
        {
            return IsFileLocalBehavior(behavior) ||
                behavior.ContainingTypes.Length > 0 ||
                behavior.Behavior.TypeKind != TypeKind.Class ||
                behavior.Behavior.IsStatic ||
                !behavior.Behavior.IsSealed ||
                behavior.Behavior.TypeParameters.Length > 0 ||
                !IsPartial(behavior.Declaration);
        }

        private static bool IsFileLocalBehavior(HotfixBehaviorInfo behavior)
        {
            return HasFileModifier(behavior.Declaration) ||
                behavior.ContainingTypes.Any(static containingType => HasFileModifier(containingType.Declaration));
        }

        private static string GenerateActorContractsSource(HotfixActorApiInfo[] contracts)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine();

            foreach (var contract in contracts)
            {
                AppendActorApiMetadata(builder, contract);
                builder.AppendLine();
            }

            ActorAccessEmitter.Append(builder, contracts);
            builder.AppendLine();
            ActorBehaviorSelectorEmitter.Append(builder, contracts);
            builder.AppendLine();
            AppendActorRegistration(builder, contracts);
            return builder.ToString();
        }

        private static void AppendActorApiMetadata(StringBuilder builder, HotfixActorApiInfo contract)
        {
            var namespaceName = contract.Actor.ContainingNamespace.IsGlobalNamespace
                ? null
                : contract.Actor.ContainingNamespace.ToDisplayString();

            if (namespaceName != null)
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine();
                builder.AppendLine("{");
            }

            AppendHotfixActorApiMetadata(builder, contract);

            if (namespaceName != null)
            {
                builder.AppendLine("}");
            }
        }

        private static void AppendActorContract(StringBuilder builder, HotfixActorApiInfo contract)
        {
            var prefix = GetActorPrefix(contract.Actor.Name);
            var actorsType = prefix + "Actors";
            var routeRefType = prefix + "RouteRef";
            var localRefType = prefix + "LocalRef";
            var placementRefType = prefix + "PlacementRef";
            var startupRefType = prefix + "StartupRef";
            var keyType = contract.KeyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var namespaceName = contract.Actor.ContainingNamespace.IsGlobalNamespace
                ? null
                : contract.Actor.ContainingNamespace.ToDisplayString();

            if (namespaceName != null)
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine();
                builder.AppendLine("{");
            }

            AppendHotfixActorApiMetadata(builder, contract);
            builder.AppendLine();
            AppendHotfixActorsClass(builder, contract, actorsType, routeRefType, localRefType, placementRefType, startupRefType, keyType);
            builder.AppendLine();
            AppendHotfixDistributedRef(builder, contract, routeRefType, keyType);
            builder.AppendLine();
            AppendHotfixPlacementRef(builder, contract, placementRefType, keyType);
            builder.AppendLine();
            AppendHotfixLocalRef(builder, contract, localRefType, keyType);
            if (contract.StartupKeyType is not null)
            {
                builder.AppendLine();
                AppendHotfixStartupRef(builder, contract, startupRefType);
            }

            if (namespaceName != null)
            {
                builder.AppendLine("}");
            }
        }

        private static void AppendHotfixActorApiMetadata(StringBuilder builder, HotfixActorApiInfo contract)
        {
            builder.Append("internal static class ").Append(contract.Actor.Name).AppendLine("ApiMetadata");
            builder.AppendLine("{");
            builder.AppendLine("    public const string MethodKeyMetadataName = \"lakona-game.actor-api.method-key\";");
            builder.AppendLine("    public const string MethodIdMetadataName = \"lakona-game.actor-api.method-id\";");
            builder.AppendLine("    public const string ActorTypeMetadataName = \"lakona-game.actor-api.actor-type\";");
            builder.AppendLine("    public const string MethodMetadataName = \"lakona-game.actor-api.method\";");
            builder.AppendLine("    public const string RequestTypeMetadataName = \"lakona-game.actor-api.request-type\";");
            builder.AppendLine("    public const string ResultTypeMetadataName = \"lakona-game.actor-api.result-type\";");
            foreach (var method in contract.Methods)
            {
                builder.Append("    public const string ")
                    .Append(CreateActorApiMethodKeyConstantName(method))
                    .Append(" = \"")
                    .Append(EscapeStringLiteral(method.MethodKey))
                    .AppendLine("\";");
                builder.Append("    public const ulong ")
                    .Append(CreateActorApiMethodIdConstantName(method))
                    .Append(" = ")
                    .Append(GetRemoteMethodId(method))
                    .AppendLine("UL;");
            }

            builder.AppendLine("}");
        }

        private static void AppendHotfixActorsClass(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            string actorsType,
            string routeRefType,
            string localRefType,
            string placementRefType,
            string startupRefType,
            string keyType)
        {
            builder.Append(contract.ApiAccessibility).Append(" sealed class ").Append(actorsType).AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            builder.AppendLine("    private readonly global::System.IServiceProvider _services;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.RemoteActorOptions _options;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorDirectory _directory;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorDirectoryCache _directoryCache;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorPlacementService _placement;");
            if (contract.StartupKeyType is not null) builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IStartupActorInvoker _startupActors;");
            builder.AppendLine();
            builder.Append("    public ").Append(actorsType).AppendLine("(");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorRuntime runtime,");
            builder.AppendLine("        global::System.IServiceProvider services,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.RemoteActorOptions options,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorDirectory directory,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorDirectoryCache directoryCache,");
            builder.Append("        global::Lakona.Game.Server.Actors.IActorPlacementService placement");
            if (contract.StartupKeyType is not null) builder.AppendLine(",").AppendLine("        global::Lakona.Game.Server.Actors.IStartupActorInvoker startupActors)");
            else builder.AppendLine(")");
            builder.AppendLine("    {");
            builder.AppendLine("        _runtime = runtime;");
            builder.AppendLine("        _services = services;");
            builder.AppendLine("        _options = options;");
            builder.AppendLine("        _directory = directory;");
            builder.AppendLine("        _directoryCache = directoryCache;");
            builder.AppendLine("        _placement = placement;");
            if (contract.StartupKeyType is not null) builder.AppendLine("        _startupActors = startupActors;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public ").Append(localRefType).Append(" Local(").Append(keyType).AppendLine(" id)");
            builder.AppendLine("    {");
            builder.Append("        return new ").Append(localRefType).AppendLine("(_runtime, id);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public ").Append(routeRefType).Append(" Route(").Append(keyType).AppendLine(" id)");
            builder.AppendLine("    {");
            builder.Append("        return new ").Append(routeRefType).AppendLine("(_runtime, _services, _options, _directory, _directoryCache, id);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public ").Append(placementRefType).Append(" Place(").Append(keyType).AppendLine(" id)");
            builder.AppendLine("    {");
            builder.Append("        return new ").Append(placementRefType).AppendLine("(_placement, id);");
            builder.AppendLine("    }");
            if (contract.StartupKeyType is not null)
            {
                var startupKeyType = contract.StartupKeyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                builder.AppendLine();
                builder.Append("    public ").Append(startupRefType).Append(" Startup(").Append(startupKeyType).AppendLine(" key)");
                builder.AppendLine("    {");
                builder.Append("        return new ").Append(startupRefType).AppendLine("(_startupActors, _runtime, key);");
                builder.AppendLine("    }");
            }
            builder.AppendLine("}");
        }

        private static void AppendHotfixPlacementRef(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            string refType,
            string keyType)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Append(contract.ApiAccessibility).Append(" readonly partial struct ").Append(refType).AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorPlacementService _placement;");
            builder.Append("    private readonly ").Append(keyType).AppendLine(" _id;");
            builder.AppendLine();
            builder.Append("    public ").Append(refType).AppendLine("(");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorPlacementService placement,");
            builder.Append("        ").Append(keyType).AppendLine(" id)");
            builder.AppendLine("    {");
            builder.AppendLine("        _placement = placement;");
            builder.AppendLine("        _id = id;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Server.Actors.ActorPlacementResult> CreateAsync(");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.Append("        return _placement.PlaceAsync<").Append(actorType).Append(", ").Append(keyType).AppendLine(">(");
            builder.AppendLine("            _id,");
            builder.AppendLine("            global::Lakona.Game.Server.Actors.ActorPlacementCreateMode.Create,");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Server.Actors.ActorPlacementResult> EnsureAsync(");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.Append("        return _placement.PlaceAsync<").Append(actorType).Append(", ").Append(keyType).AppendLine(">(");
            builder.AppendLine("            _id,");
            builder.AppendLine("            global::Lakona.Game.Server.Actors.ActorPlacementCreateMode.Ensure,");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private static void AppendHotfixStartupRef(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            string refType)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var keyType = contract.StartupKeyType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var actorName = ResolveActorName(contract.Actor);
            builder.Append(contract.ApiAccessibility).Append(" readonly partial struct ").Append(refType).AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IStartupActorInvoker _startup;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            builder.Append("    private readonly ").Append(keyType).AppendLine(" _key;");
            builder.AppendLine();
            builder.Append("    public ").Append(refType).AppendLine("(");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IStartupActorInvoker startup,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorRuntime runtime,");
            builder.Append("        ").Append(keyType).AppendLine(" key)");
            builder.AppendLine("    {");
            builder.AppendLine("        _startup = startup;");
            builder.AppendLine("        _runtime = runtime;");
            builder.AppendLine("        _key = key;");
            builder.AppendLine("    }");
            builder.AppendLine();
            AppendHotfixActorCallApi(builder, contract);
            builder.AppendLine();
            AppendHotfixResolveBehaviorMethod(builder, contract);
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask __lakona_CallAsync<TRequest>(global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method, TRequest request, global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtime = _runtime;");
            builder.Append("        return _startup.CallAsync<").Append(actorType).Append(", ").Append(keyType).AppendLine(", TRequest>(");
            AppendStartupInvocationArguments(builder, actorType, actorName, isResult: false, isPost: false);
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask<TResult> __lakona_CallAsync<TRequest, TResult>(global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method, TRequest request, global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtime = _runtime;");
            builder.Append("        return _startup.CallAsync<").Append(actorType).Append(", ").Append(keyType).AppendLine(", TRequest, TResult>(");
            AppendStartupInvocationArguments(builder, actorType, actorName, isResult: true, isPost: false);
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask __lakona_PostAsync<TRequest>(global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method, TRequest request, global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runtime = _runtime;");
            builder.Append("        return _startup.PostAsync<").Append(actorType).Append(", ").Append(keyType).AppendLine(", TRequest>(");
            AppendStartupInvocationArguments(builder, actorType, actorName, isResult: false, isPost: true);
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private static void AppendStartupInvocationArguments(
            StringBuilder builder,
            string actorType,
            string actorName,
            bool isResult,
            bool isPost)
        {
            builder.AppendLine("            _key,");
            builder.Append("            \"").Append(EscapeStringLiteral(actorName)).AppendLine("\",");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            request,");
            if (isPost)
            {
                builder.Append("            (actorId, value, ct) => global::System.Threading.Tasks.ValueTask.FromResult(runtime.TryTell<").Append(actorType).AppendLine(">(actorId,");
            }
            else if (isResult)
            {
                builder.Append("            (actorId, value, ct) => runtime.AskAsync<").Append(actorType).AppendLine(", TResult>(actorId,");
            }
            else
            {
                builder.Append("            (actorId, value, ct) => runtime.TellAsync<").Append(actorType).AppendLine(">(actorId,");
            }
            builder.Append("                (actor, innerCt) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeActorAsync");
            if (isResult) builder.Append("<TResult>");
            builder.AppendLine("(");
            builder.AppendLine("                    method.RemoteMethodId, actor, value, innerCt),");
            builder.Append("                ct)");
            if (isPost) builder.Append(")");
            builder.AppendLine(",");
            builder.AppendLine("            cancellationToken);");
        }

        private static void AppendHotfixDistributedRef(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            string refType,
            string keyType)
        {
            builder.Append(contract.ApiAccessibility).Append(" readonly partial struct ").Append(refType).AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            builder.AppendLine("    private readonly global::System.IServiceProvider _services;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.RemoteActorOptions _options;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorDirectory _directory;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorDirectoryCache _directoryCache;");
            builder.Append("    private readonly ").Append(keyType).AppendLine(" _id;");
            builder.AppendLine("    private global::Lakona.Game.Server.Actors.IRemoteActorInvoker _remote => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Actors.IRemoteActorInvoker>(_services);");
            builder.AppendLine("    private global::Lakona.Game.Server.Actors.IRemoteActorSerializer _serializer => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Actors.IRemoteActorSerializer>(_services);");
            builder.AppendLine();
            builder.Append("    public ").Append(refType).AppendLine("(");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorRuntime runtime,");
            builder.AppendLine("        global::System.IServiceProvider services,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.RemoteActorOptions options,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorDirectory directory,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorDirectoryCache directoryCache,");
            builder.Append("        ").Append(keyType).AppendLine(" id)");
            builder.AppendLine("    {");
            builder.AppendLine("        _runtime = runtime;");
            builder.AppendLine("        _services = services;");
            builder.AppendLine("        _options = options;");
            builder.AppendLine("        _directory = directory;");
            builder.AppendLine("        _directoryCache = directoryCache;");
            builder.AppendLine("        _id = id;");
            builder.AppendLine("    }");

            builder.AppendLine();
            AppendHotfixActorCallApi(builder, contract);
            builder.AppendLine();
            AppendHotfixResolveBehaviorMethod(builder, contract);
            builder.AppendLine();
            AppendHotfixDistributedCallNoResultHelper(builder, contract);
            builder.AppendLine();
            AppendHotfixPostViaTellHelper(builder);
            builder.AppendLine();
            AppendHotfixDistributedTellHelper(builder, contract);
            builder.AppendLine();
            AppendHotfixDistributedAskHelper(builder, contract);

            builder.AppendLine();
            AppendHotfixResolveNodeMethod(builder, ResolveActorName(contract.Actor), indentLevel: 1);
            builder.AppendLine();
            AppendHotfixIsLocationFailureMethod(builder, indentLevel: 1);

            builder.AppendLine("}");
        }

        private static void AppendHotfixLocalRef(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            string refType,
            string keyType)
        {
            builder.Append(contract.ApiAccessibility).Append(" readonly partial struct ").Append(refType).AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            builder.Append("    private readonly ").Append(keyType).AppendLine(" _id;");
            builder.AppendLine();
            builder.Append("    public ").Append(refType).Append("(global::Lakona.Game.Server.Actors.IActorRuntime runtime, ").Append(keyType).AppendLine(" id)");
            builder.AppendLine("    {");
            builder.AppendLine("        _runtime = runtime;");
            builder.AppendLine("        _id = id;");
            builder.AppendLine("    }");

            builder.AppendLine();
            AppendHotfixActorCallApi(builder, contract);
            builder.AppendLine();
            AppendHotfixResolveBehaviorMethod(builder, contract);
            builder.AppendLine();
            AppendHotfixCallNoResultHelper(builder);
            builder.AppendLine();
            AppendHotfixLocalPostHelper(builder, contract);
            builder.AppendLine();
            AppendHotfixLocalTellHelper(builder, contract);
            builder.AppendLine();
            AppendHotfixLocalTryTellHelper(builder, contract);
            builder.AppendLine();
            AppendHotfixLocalAskHelper(builder, contract);

            builder.AppendLine("}");
        }

        private static void AppendHotfixActorCallApi(StringBuilder builder, HotfixActorApiInfo contract)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(");
            builder.Append("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorEntry<").Append(actorType).AppendLine(", TRequest, TResult> method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        var actorMethod = new global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod(method.MethodName, method.MethodId, method.PassCancellationToken);");
            builder.AppendLine("        return __lakona_CallAsync<TRequest, TResult>(actorMethod, request, cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(");
            builder.Append("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorEntry<").Append(actorType).AppendLine(", TRequest> method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        var actorMethod = new global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod(method.MethodName, method.MethodId, method.PassCancellationToken);");
            builder.AppendLine("        return __lakona_CallAsync<TRequest>(actorMethod, request, cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(");
            builder.Append("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorEntry<").Append(actorType).AppendLine(", TRequest> method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        var actorMethod = new global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod(method.MethodName, method.MethodId, method.PassCancellationToken);");
            builder.AppendLine("        return __lakona_PostAsync<TRequest>(actorMethod, request, cancellationToken);");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixResolveBehaviorMethod(StringBuilder builder, HotfixActorApiInfo contract)
        {
            builder.AppendLine("    private static global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod __lakona_ResolveBehaviorMethod(");
            builder.AppendLine("        global::System.Delegate method,");
            builder.AppendLine("        global::System.Type requestType,");
            builder.AppendLine("        global::System.Type resultType)");
            builder.AppendLine("    {");
            builder.AppendLine("        var methodInfo = method.Method;");
            builder.AppendLine("        var declaringTypeName = methodInfo.DeclaringType?.FullName;");
            builder.AppendLine("        var methodName = methodInfo.Name;");
            builder.AppendLine();

            var behaviorTypeName = GetRuntimeTypeFullName(contract.Behavior);
            foreach (var method in contract.Methods)
            {
                var requestType = method.RequestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var resultType = method.ResultType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                builder.Append("        if (declaringTypeName == \"").Append(EscapeStringLiteral(behaviorTypeName)).AppendLine("\"");
                builder.Append("            && methodName == \"").Append(EscapeStringLiteral(method.Name)).AppendLine("\"");
                builder.Append("            && requestType == typeof(").Append(requestType).AppendLine(")");
                if (resultType == null)
                {
                    builder.AppendLine("            && resultType == null)");
                }
                else
                {
                    builder.Append("            && resultType == typeof(").Append(resultType).AppendLine("))");
                }

                builder.AppendLine("        {");
                builder.AppendLine("            return new global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod(");
                builder.Append("                \"").Append(EscapeStringLiteral(method.Name)).AppendLine("\",");
                builder.Append("                ").Append(GetRemoteMethodId(method)).AppendLine("UL,");
                builder.Append(method.HasCancellationToken ? "                true" : "                false").AppendLine(");");
                builder.AppendLine("        }");
                builder.AppendLine();
            }

            builder.Append("        throw new global::System.ArgumentException(\"The supplied behavior method is not a generated actor behavior method for ")
                .Append(EscapeStringLiteral(contract.Actor.Name))
                .AppendLine(".\", nameof(method));");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixCallNoResultHelper(StringBuilder builder)
        {
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask __lakona_CallAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        return __lakona_TellAsync<TRequest>(");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            method.PassCancellationToken,");
            builder.AppendLine("            request,");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask<TResult> __lakona_CallAsync<TRequest, TResult>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        return __lakona_AskAsync<TRequest, TResult>(");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            method.PassCancellationToken,");
            builder.AppendLine("            request,");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixDistributedCallNoResultHelper(StringBuilder builder, HotfixActorApiInfo contract)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var actorName = ResolveActorName(contract.Actor);

            builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask __lakona_CallAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.AppendLine("        if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.AppendLine("        {");
            builder.Append("            await _runtime.TellAsync<").Append(actorType).AppendLine(">(");
            builder.AppendLine("                actorId,");
            builder.AppendLine("                (actor, ct) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeActorAsync(");
            builder.AppendLine("                    method.RemoteMethodId, actor, request, ct),");
            builder.AppendLine("                cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            return;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var node = await ResolveNodeAsync(actorId, method.MethodName, cancellationToken).ConfigureAwait(false);");
            AppendHotfixRemoteInvocationSetup(builder, actorName, "method.RemoteMethodId", "node", indentLevel: 2, includeActorId: false, methodIdIsExpression: true);
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = await _remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.Append("            global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(result, actorId, \"")
                .Append(EscapeStringLiteral(actorName))
                .AppendLine("\", method.MethodName, node, correlationId);");
            builder.AppendLine("        }");
            builder.AppendLine("        catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.AppendLine("        {");
            builder.AppendLine("            _directoryCache.Remove(actorId);");
            builder.AppendLine("            throw;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask<TResult> __lakona_CallAsync<TRequest, TResult>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        return __lakona_AskAsync<TRequest, TResult>(");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            method.PassCancellationToken,");
            builder.AppendLine("            request,");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixPostViaTellHelper(StringBuilder builder)
        {
            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask __lakona_PostAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        return __lakona_TellAsync<TRequest>(");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            method.PassCancellationToken,");
            builder.AppendLine("            request,");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixLocalPostHelper(StringBuilder builder, HotfixActorApiInfo contract)
        {
            var actorName = ResolveActorName(contract.Actor);

            builder.AppendLine("    private global::System.Threading.Tasks.ValueTask __lakona_PostAsync<TRequest>(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorBehaviorMethod method,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
            builder.AppendLine("        var result = __lakona_TryTell<TRequest>(");
            builder.AppendLine("            method.MethodName,");
            builder.AppendLine("            method.RemoteMethodId,");
            builder.AppendLine("            method.PassCancellationToken,");
            builder.AppendLine("            request,");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("        if (result == global::Lakona.Game.Server.Actors.ActorTellResult.Accepted)");
            builder.AppendLine("        {");
            builder.AppendLine("            return default;");
            builder.AppendLine("        }");
            builder.AppendLine();
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.AppendLine("        var status = result == global::Lakona.Game.Server.Actors.ActorTellResult.MailboxFull");
            builder.AppendLine("            ? global::Lakona.Game.Server.Actors.ActorCallStatus.Backpressure");
            builder.AppendLine("            : result == global::Lakona.Game.Server.Actors.ActorTellResult.ActorNotFound");
            builder.AppendLine("                ? global::Lakona.Game.Server.Actors.ActorCallStatus.ActorNotFound");
            builder.AppendLine("                : global::Lakona.Game.Server.Actors.ActorCallStatus.Failed;");
            builder.Append("        throw new global::Lakona.Game.Server.Actors.ActorCallException(status, actorId, \"")
                .Append(EscapeStringLiteral(actorName))
                .AppendLine("\", method.MethodName, \"Local actor post was rejected with result \" + result + \".\");");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixLocalTellHelper(
            StringBuilder builder,
            HotfixActorApiInfo contract)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.AppendLine("    internal async global::System.Threading.Tasks.ValueTask __lakona_TellAsync<TRequest>(");
            builder.AppendLine("        string behaviorMethodName,");
            builder.AppendLine("        ulong remoteMethodId,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.Append("        await _runtime.TellAsync<").Append(actorType).AppendLine(">(");
            builder.AppendLine("            actorId,");
            builder.AppendLine("            (actor, ct) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeActorAsync(");
            builder.AppendLine("                remoteMethodId, actor, request, ct),");
            builder.AppendLine("            cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixLocalTryTellHelper(
            StringBuilder builder,
            HotfixActorApiInfo contract)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.AppendLine("    internal global::Lakona.Game.Server.Actors.ActorTellResult __lakona_TryTell<TRequest>(");
            builder.AppendLine("        string behaviorMethodName,");
            builder.AppendLine("        ulong remoteMethodId,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.Append("        return _runtime.TryTell<").Append(actorType).AppendLine(">(");
            builder.AppendLine("            actorId,");
            builder.AppendLine("            (actor, ct) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeActorAsync(");
            builder.AppendLine("                remoteMethodId, actor, request, ct),");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixLocalAskHelper(
            StringBuilder builder,
            HotfixActorApiInfo contract)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.AppendLine("    internal async global::System.Threading.Tasks.ValueTask<TResult> __lakona_AskAsync<TRequest, TResult>(");
            builder.AppendLine("        string behaviorMethodName,");
            builder.AppendLine("        ulong remoteMethodId,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.Append("        return await _runtime.AskAsync<").Append(actorType).AppendLine(", TResult>(");
            builder.AppendLine("            actorId,");
            builder.AppendLine("            (actor, ct) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeActorAsync<TResult>(");
            builder.AppendLine("                remoteMethodId, actor, request, ct),");
            builder.AppendLine("            cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixDistributedTellHelper(
            StringBuilder builder,
            HotfixActorApiInfo contract)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var actorName = ResolveActorName(contract.Actor);

            builder.AppendLine("    internal async global::System.Threading.Tasks.ValueTask __lakona_TellAsync<TRequest>(");
            builder.AppendLine("        string behaviorMethodName,");
            builder.AppendLine("        ulong remoteMethodId,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.AppendLine("        if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.AppendLine("        {");
            builder.Append("            await _runtime.TellAsync<").Append(actorType).AppendLine(">(");
            builder.AppendLine("                actorId,");
            builder.AppendLine("                (actor, ct) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeActorAsync(");
            builder.AppendLine("                    remoteMethodId, actor, request, ct),");
            builder.AppendLine("                cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            return;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var node = await ResolveNodeAsync(actorId, behaviorMethodName, cancellationToken).ConfigureAwait(false);");
            AppendHotfixRemoteInvocationSetup(builder, actorName, "remoteMethodId", "node", indentLevel: 2, includeActorId: false, methodIdIsExpression: true);
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = await _remote.TellAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.Append("            global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureAccepted(result, actorId, \"")
                .Append(EscapeStringLiteral(actorName))
                .AppendLine("\", behaviorMethodName, node, correlationId);");
            builder.AppendLine("        }");
            builder.AppendLine("        catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.AppendLine("        {");
            builder.AppendLine("            _directoryCache.Remove(actorId);");
            builder.AppendLine("            throw;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixDistributedAskHelper(
            StringBuilder builder,
            HotfixActorApiInfo contract)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var actorName = ResolveActorName(contract.Actor);

            builder.AppendLine("    internal async global::System.Threading.Tasks.ValueTask<TResult> __lakona_AskAsync<TRequest, TResult>(");
            builder.AppendLine("        string behaviorMethodName,");
            builder.AppendLine("        ulong remoteMethodId,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.AppendLine("        if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.AppendLine("        {");
            builder.Append("            return await _runtime.AskAsync<").Append(actorType).AppendLine(", TResult>(");
            builder.AppendLine("                actorId,");
            builder.AppendLine("                (actor, ct) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeActorAsync<TResult>(");
            builder.AppendLine("                    remoteMethodId, actor, request, ct),");
            builder.AppendLine("                cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var node = await ResolveNodeAsync(actorId, behaviorMethodName, cancellationToken).ConfigureAwait(false);");
            AppendHotfixRemoteInvocationSetup(builder, actorName, "remoteMethodId", "node", indentLevel: 2, includeActorId: false, methodIdIsExpression: true);
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = await _remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.Append("            global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(result, actorId, \"")
                .Append(EscapeStringLiteral(actorName))
                .AppendLine("\", behaviorMethodName, node, correlationId);");
            builder.AppendLine("            return _serializer.Deserialize<TResult>(result.Payload);");
            builder.AppendLine("        }");
            builder.AppendLine("        catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.AppendLine("        {");
            builder.AppendLine("            _directoryCache.Remove(actorId);");
            builder.AppendLine("            throw;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
        }

        private static void AppendActorRegistration(StringBuilder builder, HotfixActorApiInfo[] contracts)
        {
            builder.AppendLine("public sealed class GeneratedHotfixActorRegistration :");
            builder.AppendLine("    global::Lakona.Game.Server.Hotfix.IHotfixGeneratedServiceRegistration");
            builder.AppendLine("{");
            builder.AppendLine("    public void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            builder.AppendLine("    {");
            builder.AppendLine("        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<global::Lakona.Game.Server.Hotfix.ActorAccess>(services);");

            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private static IEnumerable<StartupRegistrationInfo> DiscoverStartupRegistrations(
            Compilation compilation,
            CancellationToken cancellationToken)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var invocation in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
                        method.Name != "RegisterStartup" ||
                        method.TypeArguments.Length != 2 ||
                        method.ContainingType.ToDisplayString() != ActorHostBuilderName)
                    {
                        continue;
                    }

                    yield return new StartupRegistrationInfo(
                        (INamedTypeSymbol)method.TypeArguments[0],
                        method.TypeArguments[1]);
                }
            }
        }

        private static IEnumerable<HotfixBehaviorInfo> DiscoverHotfixBehaviors(Compilation compilation, CancellationToken cancellationToken)
        {
            foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryCreateHotfixBehavior(type, out var behavior))
                {
                    yield return behavior!;
                }
            }
        }

        private static bool TryCreateHotfixBehavior(INamedTypeSymbol type, out HotfixBehaviorInfo? info)
        {
            info = null;
            var attribute = type.GetAttributes()
                .FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == HotfixBehaviorOfAttributeName);
            if (attribute is null ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol actor)
            {
                return false;
            }

            var declaration = type.DeclaringSyntaxReferences
                .Select(static reference => reference.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault();
            if (declaration is null)
            {
                return false;
            }

            info = new HotfixBehaviorInfo(type, actor, declaration, CreateContainingTypes(type, declaration));
            return true;
        }

        private static HotfixActorApiInfo CreateBehaviorActorContract(
            HotfixBehaviorInfo behavior,
            string hotfixAssemblyName,
            ITypeSymbol? startupKeyType)
        {
            var diagnostics = new List<HotfixGeneratorDiagnosticInfo>();
            var keyType = GetActorKeyType(behavior.Actor);
            if (keyType == null)
            {
                diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                    HotfixGeneratorDiagnostics.HotfixBehaviorTargetMustDeriveActor,
                    behavior.Behavior.Locations.FirstOrDefault(),
                    behavior.Behavior.ToDisplayString(),
                    behavior.Actor.ToDisplayString()));
            }

            var methods = new List<HotfixActorMethodInfo>();
            foreach (var method in behavior.Behavior.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary ||
                    method.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (HasAttribute(method, ActorStartAttributeName) ||
                    HasAttribute(method, ActorStopAttributeName))
                {
                    continue;
                }

                if (!TryCreateBehaviorActorMethod(behavior, method, hotfixAssemblyName, diagnostics, out var methodInfo))
                {
                    continue;
                }

                methods.Add(methodInfo!);
            }

            foreach (var duplicate in methods
                .GroupBy(static method => method.MethodKey)
                .Where(static group => group.Count() > 1))
            {
                foreach (var duplicateMethod in duplicate.Skip(1))
                {
                    diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                        HotfixGeneratorDiagnostics.DuplicateHotfixBehaviorActorApiMethodKey,
                        duplicateMethod.Location,
                        duplicateMethod.Name,
                        duplicate.Key));
                }
            }

            foreach (var duplicate in methods
                .SelectMany(static method => EnumerateGeneratedActorWrapperSignatures(method)
                    .Select(signature => new
                    {
                        Method = method,
                        Signature = signature
                    }))
                .GroupBy(static item => item.Signature)
                .Where(static group => group.Count() > 1))
            {
                foreach (var duplicateMethod in duplicate.Skip(1))
                {
                    diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                        HotfixGeneratorDiagnostics.DuplicateHotfixBehaviorActorApiGeneratedSignature,
                        duplicateMethod.Method.Location,
                        duplicateMethod.Method.Name,
                        duplicate.Key));
                }
            }

            var resolvedKeyType = keyType ?? behavior.Actor;
            var apiAccessibility = IsPubliclyExposable(behavior.Actor) && IsPubliclyExposable(resolvedKeyType)
                ? "public"
                : "internal";

            return new HotfixActorApiInfo(
                behavior.Behavior,
                behavior.Actor,
                resolvedKeyType,
                startupKeyType,
                apiAccessibility,
                diagnostics.Count == 0 ? methods.ToArray() : Array.Empty<HotfixActorMethodInfo>(),
                diagnostics.ToArray());
        }

        private static bool IsPubliclyExposable(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol arrayType)
            {
                return IsPubliclyExposable(arrayType.ElementType);
            }

            if (type is INamedTypeSymbol namedType)
            {
                if (namedType.DeclaredAccessibility != Accessibility.Public)
                {
                    return false;
                }

                if (namedType.ContainingType is not null && !IsPubliclyExposable(namedType.ContainingType))
                {
                    return false;
                }

                foreach (var typeArgument in namedType.TypeArguments)
                {
                    if (!IsPubliclyExposable(typeArgument))
                    {
                        return false;
                    }
                }

                return true;
            }

            return type.DeclaredAccessibility == Accessibility.Public;
        }

        private static bool TryCreateBehaviorActorMethod(
            HotfixBehaviorInfo behavior,
            IMethodSymbol method,
            string hotfixAssemblyName,
            List<HotfixGeneratorDiagnosticInfo> diagnostics,
            out HotfixActorMethodInfo? methodInfo)
        {
            methodInfo = null;
            var location = method.Locations.FirstOrDefault() ?? behavior.Behavior.Locations.FirstOrDefault();
            if (!IsSupportedBehaviorActorMethodShape(behavior.Actor, method, out var resultType))
            {
                diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                    HotfixGeneratorDiagnostics.HotfixBehaviorActorApiMethodShape,
                    location,
                    method.Name,
                    behavior.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                return false;
            }

            var requestType = method.Parameters[1].Type;
            if (ContainsTypeFromAssembly(requestType, hotfixAssemblyName))
            {
                diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                    HotfixGeneratorDiagnostics.HotfixBehaviorActorApiTypeBoundary,
                    location,
                    method.Name,
                    "request",
                    requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                return false;
            }

            if (resultType != null && ContainsTypeFromAssembly(resultType, hotfixAssemblyName))
            {
                diagnostics.Add(new HotfixGeneratorDiagnosticInfo(
                    HotfixGeneratorDiagnostics.HotfixBehaviorActorApiTypeBoundary,
                    location,
                    method.Name,
                    "return result",
                    resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                return false;
            }

            var methodKey = CreateBehaviorActorMethodKey(behavior.Actor, method.Name, requestType, resultType);
            methodInfo = HotfixActorMethodInfo.Create(
                method,
                requestType,
                resultType,
                method.Parameters.Length == 3,
                methodKey);
            return true;
        }

        private static bool IsSupportedBehaviorActorMethodShape(
            INamedTypeSymbol actor,
            IMethodSymbol method,
            out ITypeSymbol? resultType)
        {
            resultType = null;
            if (method.TypeParameters.Length > 0 ||
                method.IsStatic ||
                method.IsExtensionMethod ||
                !IsValueTask(method.ReturnType, out resultType) ||
                (resultType != null && (resultType.IsRefLikeType || ContainsTypeParameter(resultType))))
            {
                return false;
            }

            if (method.Parameters.Length != 2 && method.Parameters.Length != 3)
            {
                return false;
            }

            var receiver = method.Parameters[0];
            if (receiver.RefKind != RefKind.None ||
                !SymbolEqualityComparer.Default.Equals(receiver.Type, actor))
            {
                return false;
            }

            if (!IsSupportedRequestParameter(method.Parameters[1]))
            {
                return false;
            }

            return method.Parameters.Length == 2 ||
                (method.Parameters[2].RefKind == RefKind.None && IsCancellationToken(method.Parameters[2].Type));
        }

        private static ITypeSymbol? GetActorKeyType(INamedTypeSymbol symbol)
        {
            for (var current = symbol.BaseType; current != null; current = current.BaseType)
            {
                if (current.Arity == 1 &&
                    current.Name == "Actor" &&
                    current.ContainingNamespace.ToDisplayString() == "Lakona.Game.Server.Actors")
                {
                    return current.TypeArguments[0];
                }
            }

            return null;
        }

        private static bool IsSupportedRequestParameter(IParameterSymbol parameter)
        {
            return parameter.RefKind == RefKind.None &&
                parameter.Type.TypeKind != TypeKind.Pointer &&
                !parameter.Type.IsRefLikeType &&
                !ContainsTypeParameter(parameter.Type) &&
                !IsCancellationToken(parameter.Type);
        }

        private static bool ContainsTypeParameter(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.TypeParameter)
            {
                return true;
            }

            if (type is IArrayTypeSymbol arrayType)
            {
                return ContainsTypeParameter(arrayType.ElementType);
            }

            if (type is IPointerTypeSymbol pointerType)
            {
                return ContainsTypeParameter(pointerType.PointedAtType);
            }

            return type is INamedTypeSymbol namedType &&
                namedType.TypeArguments.Any(ContainsTypeParameter);
        }

        private static bool ContainsTypeFromAssembly(ITypeSymbol type, string assemblyName)
        {
            if (string.Equals(type.ContainingAssembly?.Identity.Name, assemblyName, System.StringComparison.Ordinal))
            {
                return true;
            }

            if (type is IArrayTypeSymbol arrayType)
            {
                return ContainsTypeFromAssembly(arrayType.ElementType, assemblyName);
            }

            if (type is IPointerTypeSymbol pointerType)
            {
                return ContainsTypeFromAssembly(pointerType.PointedAtType, assemblyName);
            }

            return type is INamedTypeSymbol namedType &&
                namedType.TypeArguments.Any(argument => ContainsTypeFromAssembly(argument, assemblyName));
        }

        private static string CreateBehaviorActorMethodKey(
            INamedTypeSymbol actorType,
            string methodName,
            ITypeSymbol requestType,
            ITypeSymbol? resultType)
        {
            return "actor:" +
                GetRuntimeTypeIdentity(actorType) +
                "|method:" +
                methodName +
                "|request:" +
                GetRuntimeTypeIdentity(requestType) +
                "|result:" +
                (resultType == null ? "void" : GetRuntimeTypeIdentity(resultType));
        }

        private static bool IsValueTask(ITypeSymbol type, out ITypeSymbol? resultType)
        {
            resultType = null;
            if (type is INamedTypeSymbol namedType &&
                namedType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
                namedType.Name == "ValueTask")
            {
                if (namedType.Arity == 0)
                {
                    return true;
                }

                if (namedType.Arity == 1)
                {
                    resultType = namedType.TypeArguments[0];
                    return true;
                }
            }

            return false;
        }

        private static bool IsCancellationToken(ITypeSymbol type)
        {
            return type.Name == "CancellationToken" &&
                type.ContainingNamespace.ToDisplayString() == "System.Threading";
        }

        private static string SanitizeIdentifierPart(string value)
        {
            var builder = new StringBuilder();
            foreach (var character in value)
            {
                if (builder.Length == 0)
                {
                    builder.Append(SyntaxFacts.IsIdentifierStartCharacter(character) ? character : '_');
                    continue;
                }

                builder.Append(SyntaxFacts.IsIdentifierPartCharacter(character) ? character : '_');
            }

            return builder.Length == 0 ? "field" : builder.ToString();
        }

        private static void AppendHotfixActorIdSetup(StringBuilder builder, int indentLevel)
        {
            builder.Append(Indent(indentLevel))
                .AppendLine("var actorId = global::Lakona.Game.Server.Actors.ActorId.From(_id.ToString());");
        }

        private static void AppendHotfixRemoteInvocationSetup(
            StringBuilder builder,
            string actorName,
            string methodId,
            string nodeExpression,
            int indentLevel,
            bool includeActorId,
            bool methodIdIsExpression = false)
        {
            var indent = Indent(indentLevel);
            if (includeActorId)
            {
                AppendHotfixActorIdSetup(builder, indentLevel);
            }

            builder.Append(indent).AppendLine("var payload = _serializer.Serialize(request);");
            builder.Append(indent).AppendLine("var correlationId = global::System.Guid.NewGuid().ToString(\"N\");");
            builder.Append(indent).AppendLine("var deadline = global::System.DateTimeOffset.UtcNow.Add(_options.DefaultTimeout);");
            builder.Append(indent).AppendLine("var metadata = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal)");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).Append("    [global::Lakona.Game.Server.Hotfix.HotfixActorApiMetadata.MethodIdKey] = ");
            if (methodIdIsExpression)
            {
                builder.Append(methodId);
            }
            else
            {
                builder.Append(methodId).Append("UL");
            }

            builder.AppendLine(".ToString(global::System.Globalization.CultureInfo.InvariantCulture)");
            builder.Append(indent).AppendLine("};");
            builder.Append(indent)
                .Append("var invocation = new global::Lakona.Game.Server.Actors.RemoteActorInvocation(")
                .Append(nodeExpression)
                .Append(", actorId, \"")
                .Append(EscapeStringLiteral(actorName))
                .Append("\", global::Lakona.Game.Server.Hotfix.HotfixActorApiMetadata.ActorMessageKind, payload, deadline, correlationId, metadata);")
                .AppendLine();
        }

        private static void AppendHotfixResolveNodeMethod(
            StringBuilder builder,
            string actorName,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent).AppendLine("private async global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Cluster.NodeId> ResolveNodeAsync(");
            builder.Append(indent).AppendLine("    global::Lakona.Game.Server.Actors.ActorId actorId,");
            builder.Append(indent).AppendLine("    string methodName,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken)");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    if (!_directoryCache.TryGet(actorId, out var node))");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        var record = await _directory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);");
            builder.Append(indent).AppendLine("        if (record is null)");
            builder.Append(indent).AppendLine("        {");
            builder.Append(indent).AppendLine("            throw new global::Lakona.Game.Server.Actors.ActorNotFoundException(");
            builder.Append(indent).AppendLine("                actorId,");
            builder.Append(indent).Append("                \"").Append(EscapeStringLiteral(actorName)).AppendLine("\",");
            builder.Append(indent).AppendLine("                methodName,");
            builder.Append(indent).AppendLine("                \"Actor was not found in actor directory.\");");
            builder.Append(indent).AppendLine("        }");
            builder.AppendLine();
            builder.Append(indent).AppendLine("        node = record.Node;");
            builder.Append(indent).AppendLine("        _directoryCache.Set(actorId, node);");
            builder.Append(indent).AppendLine("    }");
            builder.AppendLine();
            builder.Append(indent).AppendLine("    return node;");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendHotfixIsLocationFailureMethod(StringBuilder builder, int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent).AppendLine("private static bool IsLocationFailure(global::Lakona.Game.Server.Actors.ActorCallException exception)");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    return exception.Status == global::Lakona.Game.Server.Actors.ActorCallStatus.ActorNotFound");
            builder.Append(indent).AppendLine("        || exception.Status == global::Lakona.Game.Server.Actors.ActorCallStatus.NodeUnavailable;");
            builder.Append(indent).AppendLine("}");
        }

        internal static string DisplayReturnType(HotfixActorMethodInfo method)
        {
            if (method.ResultType == null)
            {
                return "global::System.Threading.Tasks.ValueTask";
            }

            return "global::System.Threading.Tasks.ValueTask<" +
                method.ResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                ">";
        }

        internal static ulong GetRemoteMethodId(HotfixActorMethodInfo method)
        {
            return CreateMethodId(method.MethodKey);
        }

        private static string GetActorPrefix(string actorName)
        {
            return actorName.EndsWith("Actor", System.StringComparison.Ordinal) && actorName.Length > "Actor".Length
                ? actorName.Substring(0, actorName.Length - "Actor".Length)
                : actorName;
        }

        internal static string ResolveActorName(INamedTypeSymbol actor)
        {
            foreach (var attribute in actor.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() ==
                        "Lakona.Game.Server.Actors.ActorNameAttribute" &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is string explicitName &&
                    !string.IsNullOrWhiteSpace(explicitName))
                {
                    return explicitName;
                }
            }

            return LowerFirst(GetActorPrefix(actor.Name));
        }

        internal static string CreateActorApiMethodKeyConstantName(HotfixActorMethodInfo method)
        {
            var builder = new StringBuilder();
            builder.Append(SanitizeIdentifierPart(method.Name));
            builder.Append('_');
            builder.Append(SanitizeIdentifierPart(GetRuntimeTypeFullName(method.RequestType)));
            builder.Append("_MethodKey");
            return builder.ToString();
        }

        internal static string CreateActorApiMethodIdConstantName(HotfixActorMethodInfo method)
        {
            var builder = new StringBuilder();
            builder.Append(SanitizeIdentifierPart(method.Name));
            builder.Append('_');
            builder.Append(SanitizeIdentifierPart(GetRuntimeTypeFullName(method.RequestType)));
            builder.Append("_MethodId");
            return builder.ToString();
        }

        private static ulong CreateMethodId(string methodKey)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            unchecked
            {
                var hash = offsetBasis;
                foreach (var value in Encoding.UTF8.GetBytes(methodKey))
                {
                    hash ^= value;
                    hash *= prime;
                }

                return hash;
            }
        }

        private static IEnumerable<string> EnumerateGeneratedActorWrapperSignatures(HotfixActorMethodInfo method)
        {
            yield return CreateGeneratedActorWrapperSignature(method.Name, method.RequestType);
            if (method.ResultType == null)
            {
                yield return CreateGeneratedActorWrapperSignature("Try" + method.Name, method.RequestType);
            }
        }

        private static string CreateGeneratedActorWrapperSignature(string methodName, ITypeSymbol requestType)
        {
            return methodName + "(" + GetRuntimeTypeIdentity(requestType) + ")";
        }

        internal static string GetAccessibility(INamedTypeSymbol symbol)
        {
            switch (symbol.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return "public";
                case Accessibility.Internal:
                    return "internal";
                case Accessibility.Private:
                    return "private";
                case Accessibility.Protected:
                    return "protected";
                case Accessibility.ProtectedOrInternal:
                    return "protected internal";
                case Accessibility.ProtectedAndInternal:
                    return "private protected";
                default:
                    return "internal";
            }
        }

        internal static string LowerFirst(string value)
        {
            if (value.Length == 0)
            {
                return value;
            }

            return char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        internal static string Indent(int indentLevel)
        {
            return new string(' ', indentLevel * 4);
        }

        private static ContainingTypeInfo[] CreateContainingTypes(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration)
        {
            var containingDeclarations = new List<TypeDeclarationSyntax>();
            for (var current = declaration.Parent; current != null; current = current.Parent)
            {
                if (current is TypeDeclarationSyntax containingDeclaration)
                {
                    containingDeclarations.Add(containingDeclaration);
                }
            }

            containingDeclarations.Reverse();

            var containingSymbols = new List<INamedTypeSymbol>();
            for (var current = symbol.ContainingType; current != null; current = current.ContainingType)
            {
                containingSymbols.Add(current);
            }

            containingSymbols.Reverse();

            return containingDeclarations
                .Zip(containingSymbols, (typeDeclaration, typeSymbol) => new ContainingTypeInfo(typeSymbol, typeDeclaration))
                .ToArray();
        }

        internal sealed class HotfixBehaviorInfo
        {
            public HotfixBehaviorInfo(INamedTypeSymbol behavior, INamedTypeSymbol actor, TypeDeclarationSyntax declaration, ContainingTypeInfo[] containingTypes)
            {
                Behavior = behavior;
                Actor = actor;
                Declaration = declaration;
                ContainingTypes = containingTypes;
            }

            public INamedTypeSymbol Behavior { get; }

            public INamedTypeSymbol Actor { get; }

            public TypeDeclarationSyntax Declaration { get; }

            public ContainingTypeInfo[] ContainingTypes { get; }
        }

        internal sealed class ContainingTypeInfo
        {
            public ContainingTypeInfo(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration)
            {
                Symbol = symbol;
                Declaration = declaration;
            }

            public INamedTypeSymbol Symbol { get; }

            public TypeDeclarationSyntax Declaration { get; }
        }

        internal sealed class HotfixActorGenerationInput
        {
            public HotfixActorGenerationInput(
                string assemblyName,
                HotfixBehaviorInfo[] behaviors,
                StartupRegistrationInfo[] startupRegistrations)
            {
                AssemblyName = assemblyName;
                Behaviors = behaviors;
                StartupRegistrations = startupRegistrations;
            }

            public string AssemblyName { get; }

            public HotfixBehaviorInfo[] Behaviors { get; }

            public StartupRegistrationInfo[] StartupRegistrations { get; }
        }

        internal sealed class StartupRegistrationInfo
        {
            public StartupRegistrationInfo(INamedTypeSymbol actor, ITypeSymbol keyType)
            {
                Actor = actor;
                KeyType = keyType;
            }

            public INamedTypeSymbol Actor { get; }

            public ITypeSymbol KeyType { get; }
        }

        internal sealed class HotfixActorApiInfo
        {
            public HotfixActorApiInfo(
                INamedTypeSymbol behavior,
                INamedTypeSymbol actor,
                ITypeSymbol keyType,
                ITypeSymbol? startupKeyType,
                string apiAccessibility,
                HotfixActorMethodInfo[] methods,
                HotfixGeneratorDiagnosticInfo[] diagnostics)
            {
                Behavior = behavior;
                Actor = actor;
                KeyType = keyType;
                StartupKeyType = startupKeyType;
                ApiAccessibility = apiAccessibility;
                Methods = methods;
                Diagnostics = diagnostics;
            }

            public INamedTypeSymbol Behavior { get; }

            public INamedTypeSymbol Actor { get; }

            public ITypeSymbol KeyType { get; }

            public ITypeSymbol? StartupKeyType { get; }

            public string ApiAccessibility { get; }

            public HotfixActorMethodInfo[] Methods { get; }

            public HotfixGeneratorDiagnosticInfo[] Diagnostics { get; }

            public bool IsSupported => Diagnostics.Length == 0 && Methods.Length > 0;
        }

        internal sealed class HotfixGeneratorDiagnosticInfo
        {
            public HotfixGeneratorDiagnosticInfo(
                DiagnosticDescriptor descriptor,
                Location? location,
                params object[] arguments)
            {
                Descriptor = descriptor;
                Location = location;
                Arguments = arguments;
            }

            public DiagnosticDescriptor Descriptor { get; }

            public Location? Location { get; }

            public object[] Arguments { get; }
        }

        internal sealed class HotfixActorMethodInfo
        {
            private HotfixActorMethodInfo(
                string name,
                ITypeSymbol requestType,
                ITypeSymbol? resultType,
                bool hasCancellationToken,
                Location? location,
                string methodKey)
            {
                Name = name;
                RequestType = requestType;
                ResultType = resultType;
                HasCancellationToken = hasCancellationToken;
                Location = location;
                MethodKey = methodKey;
            }

            public string Name { get; }

            public ITypeSymbol RequestType { get; }

            public ITypeSymbol? ResultType { get; }

            public bool HasCancellationToken { get; }

            public Location? Location { get; }

            public string MethodKey { get; }

            public static HotfixActorMethodInfo Create(
                IMethodSymbol method,
                ITypeSymbol requestType,
                ITypeSymbol? resultType,
                bool hasCancellationToken,
                string methodKey)
            {
                return new HotfixActorMethodInfo(
                    method.Name,
                    requestType,
                    resultType,
                    hasCancellationToken,
                    method.Locations.FirstOrDefault(),
                    methodKey);
            }
        }
    }
}
