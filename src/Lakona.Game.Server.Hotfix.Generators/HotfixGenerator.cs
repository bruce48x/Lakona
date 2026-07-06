using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Lakona.Game.Server.Hotfix.Generators
{
    [Generator]
    public sealed class HotfixGenerator : IIncrementalGenerator
    {
        private const string HotfixStateAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixStateAttribute";
        private const string HotfixBehaviorOfAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixBehaviorOfAttribute";
        private const string RpcServiceAttributeName = "Lakona.Rpc.Core.RpcServiceAttribute";
        private const string RpcMethodAttributeName = "Lakona.Rpc.Core.RpcMethodAttribute";
        private const string DefaultGeneratedServerNamespace = "Server.App.Generated";
        private const string StableRpcServicesKey = "build_property.LakonaHotfixGenerateStableRpcServices";

        private static readonly DiagnosticDescriptor UnsupportedHotfixBehaviorWrapperTarget = new DiagnosticDescriptor(
            "ULGHOTFIX021",
            "Hotfix behavior cannot receive generated actor ref wrappers",
            "Hotfix behavior '{0}' cannot receive generated actor ref wrappers because generated extension methods require a non-file-local, non-generic, top-level static partial class",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var options = context.AnalyzerConfigOptionsProvider.Select(static (provider, cancellationToken) =>
            {
                _ = cancellationToken;
                return HotfixGeneratorOptions.From(provider.GlobalOptions);
            });

            var states = context.SyntaxProvider
                .CreateSyntaxProvider(
                    IsStateCandidate,
                    GetState)
                .Where(IsNotNull);

            context.RegisterSourceOutput(states, GenerateState);
            context.RegisterSourceOutput(states, GenerateStateCaller);

            var services = context.CompilationProvider.Combine(options)
                .Select(static (input, cancellationToken) =>
                {
                    var (compilation, generatorOptions) = input;
                    if (!generatorOptions.GenerateStableRpcServices)
                    {
                        return [];
                    }

                    return DiscoverRpcServiceContracts(compilation, cancellationToken)
                    .Select(static contract => new HotfixRpcServiceInfo(
                        contract,
                        DefaultGeneratedServerNamespace,
                        DefaultGeneratedServerNamespace))
                    .ToArray();
                });

            context.RegisterSourceOutput(services, GenerateRpcServices);

            var actorContracts = context.CompilationProvider.Combine(options)
                .Select(static (input, cancellationToken) =>
                {
                    var (compilation, _) = input;
                    return new HotfixActorGenerationInput(
                        compilation.Assembly.Identity.Name,
                        DiscoverHotfixBehaviors(compilation, cancellationToken).ToArray());
                });

            context.RegisterSourceOutput(actorContracts, GenerateActorContracts);
        }

        private static bool IsStateCandidate(SyntaxNode node, CancellationToken cancellationToken)
        {
            return node is TypeDeclarationSyntax declaration && declaration.AttributeLists.Count > 0;
        }

        private static HotfixStateInfo? GetState(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            var declaration = (TypeDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken);
            if (symbol == null)
            {
                return null;
            }

            var hasAttribute = symbol.GetAttributes().Any(attribute =>
                attribute.AttributeClass != null &&
                attribute.AttributeClass.ToDisplayString() == HotfixStateAttributeName);

            return hasAttribute ? new HotfixStateInfo(symbol, declaration) : null;
        }

        private static bool IsNotNull(HotfixStateInfo? state)
        {
            return state != null;
        }

        private static void GenerateRpcServices(SourceProductionContext context, HotfixRpcServiceInfo[] services)
        {
            var supported = new List<HotfixRpcServiceInfo>();
            foreach (var service in services)
            {
                if (!ValidateRpcService(context, service))
                {
                    continue;
                }

                supported.Add(service);
                context.AddSource(
                    CreateRpcServiceHintName(service.Contract),
                    SourceText.From(GenerateRpcServiceSource(service), Encoding.UTF8));
            }

            if (supported.Count == 0)
            {
                return;
            }

            context.AddSource("GeneratedHotfixServices.g.cs", SourceText.From(GenerateRpcServiceExtensionSource(supported.ToArray()), Encoding.UTF8));
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

                var contract = CreateBehaviorActorContract(behavior, input.AssemblyName);
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
            GenerateActorWrappers(context, input.Behaviors, supportedArray);
        }

        private static void GenerateActorWrappers(
            SourceProductionContext context,
            HotfixBehaviorInfo[] behaviors,
            HotfixActorApiInfo[] contracts)
        {
            var behaviorGroups = behaviors
                .GroupBy(static behavior => behavior.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), System.StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.ToArray(), System.StringComparer.Ordinal);

            foreach (var contract in contracts)
            {
                var actorKey = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!behaviorGroups.TryGetValue(actorKey, out var actorBehaviors))
                {
                    continue;
                }

                context.AddSource(
                    CreateActorWrapperHintName(actorBehaviors[0].Behavior),
                    SourceText.From(GenerateActorWrapperSource(contract, actorBehaviors[0]), Encoding.UTF8));
            }
        }

        private static bool IsUnsupportedBehaviorWrapperTarget(HotfixBehaviorInfo behavior)
        {
            return IsFileLocalBehavior(behavior) ||
                behavior.ContainingTypes.Length > 0 ||
                behavior.Behavior.TypeKind != TypeKind.Class ||
                !behavior.Behavior.IsStatic ||
                behavior.Behavior.TypeParameters.Length > 0 ||
                !IsPartial(behavior.Declaration);
        }

        private static bool IsFileLocalBehavior(HotfixBehaviorInfo behavior)
        {
            return HasFileModifier(behavior.Declaration) ||
                behavior.ContainingTypes.Any(static containingType => HasFileModifier(containingType.Declaration));
        }

        private static bool HasFileModifier(TypeDeclarationSyntax declaration)
        {
            return declaration.Modifiers.Any(static modifier =>
                string.Equals(modifier.ValueText, "file", System.StringComparison.Ordinal));
        }

        private static string GenerateActorWrapperSource(HotfixActorApiInfo contract, HotfixBehaviorInfo behavior)
        {
            var namespaceName = behavior.Behavior.ContainingNamespace.IsGlobalNamespace
                ? null
                : behavior.Behavior.ContainingNamespace.ToDisplayString();
            var prefix = GetActorPrefix(contract.Actor.Name);
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine();

            if (namespaceName != null)
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine();
                builder.AppendLine("{");
            }

            AppendBehaviorContainingTypes(builder, behavior, namespaceName != null ? 1 : 0);
            AppendBehaviorWrapperType(builder, contract, behavior, prefix, namespaceName != null ? 1 + behavior.ContainingTypes.Length : behavior.ContainingTypes.Length);
            CloseBehaviorContainingTypes(builder, behavior, namespaceName != null ? 1 : 0);

            if (namespaceName != null)
            {
                builder.AppendLine("}");
            }

            return builder.ToString();
        }

        private static void AppendBehaviorContainingTypes(StringBuilder builder, HotfixBehaviorInfo behavior, int indentLevel)
        {
            foreach (var containingType in behavior.ContainingTypes)
            {
                AppendTypeHeader(builder, containingType.Declaration, indentLevel);
                builder.Append(Indent(indentLevel)).AppendLine("{");
                indentLevel++;
            }
        }

        private static void CloseBehaviorContainingTypes(StringBuilder builder, HotfixBehaviorInfo behavior, int indentLevel)
        {
            for (var index = behavior.ContainingTypes.Length - 1; index >= 0; index--)
            {
                builder.Append(Indent(indentLevel + index)).AppendLine("}");
            }
        }

        private static void AppendBehaviorWrapperType(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            HotfixBehaviorInfo behavior,
            string prefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent).Append(GetAccessibility(behavior.Behavior)).Append(" static partial class ").Append(behavior.Behavior.Name).AppendLine();
            builder.Append(indent).AppendLine("{");

            var refTypes = new[]
            {
                prefix + "Ref",
                prefix + "LocalRef",
                prefix + "RemoteRef",
            };

            foreach (var method in contract.Methods)
            {
                foreach (var refType in refTypes)
                {
                    AppendBehaviorWrapperMethod(builder, contract, method, refType, indentLevel + 1);
                    builder.AppendLine();
                    if (method.ResultType == null && string.Equals(refType, prefix + "LocalRef", StringComparison.Ordinal))
                    {
                        AppendBehaviorTryTellWrapperMethod(builder, contract, method, refType, indentLevel + 1);
                        builder.AppendLine();
                    }
                }
            }

            builder.Append(indent).AppendLine("}");
        }

        private static void AppendBehaviorTryTellWrapperMethod(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            HotfixActorMethodInfo method,
            string refType,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var continuationIndent = Indent(indentLevel + 1);
            var actorNamespace = contract.Actor.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : contract.Actor.ContainingNamespace.ToDisplayString() + ".";
            var receiverType = "global::" + actorNamespace + refType;
            var requestType = method.RequestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var remoteKind = GetRemoteKind(method);

            builder.Append(indent).AppendLine("[global::Lakona.Game.Server.Hotfix.Abstractions.GeneratedHotfixActorRefMethodAttribute]");
            builder.Append(indent)
                .Append("public static global::Lakona.Game.Server.Actors.ActorTellResult Try")
                .Append(method.Name)
                .AppendLine("(");
            builder.Append(continuationIndent)
                .Append("this ")
                .Append(receiverType)
                .AppendLine(" self,");
            builder.Append(continuationIndent)
                .Append(requestType)
                .Append(" request,")
                .AppendLine();
            builder.Append(continuationIndent)
                .AppendLine("global::System.Threading.CancellationToken cancellationToken = default)");

            builder.Append(indent).AppendLine("{");
            builder.Append(indent).Append("    return self.__lakona_TryTell<").Append(requestType).AppendLine(">(");
            builder.Append(indent).Append("        \"").Append(EscapeStringLiteral(method.Name)).AppendLine("\",");
            builder.Append(indent).Append("        \"").Append(EscapeStringLiteral(remoteKind)).AppendLine("\",");
            builder.Append(indent).Append(method.HasCancellationToken ? "        true," : "        false,").AppendLine();
            builder.Append(indent).AppendLine("        request,");
            builder.Append(indent).AppendLine("        cancellationToken);");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendBehaviorWrapperMethod(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            HotfixActorMethodInfo method,
            string refType,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var continuationIndent = Indent(indentLevel + 1);
            var actorNamespace = contract.Actor.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : contract.Actor.ContainingNamespace.ToDisplayString() + ".";
            var receiverType = "global::" + actorNamespace + refType;
            var requestType = method.RequestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var remoteKind = GetRemoteKind(method);

            builder.Append(indent).AppendLine("[global::Lakona.Game.Server.Hotfix.Abstractions.GeneratedHotfixActorRefMethodAttribute]");
            builder.Append(indent)
                .Append("public static ")
                .Append(DisplayReturnType(method))
                .Append(' ')
                .Append(method.Name)
                .AppendLine("(");
            builder.Append(continuationIndent)
                .Append("this ")
                .Append(receiverType)
                .AppendLine(" self,");
            builder.Append(continuationIndent)
                .Append(requestType)
                .Append(" request,")
                .AppendLine();
            builder.Append(continuationIndent)
                .AppendLine("global::System.Threading.CancellationToken cancellationToken = default)");

            builder.Append(indent).AppendLine("{");
            if (method.ResultType == null)
            {
                builder.Append(indent).Append("    return self.__lakona_TellAsync<").Append(requestType).AppendLine(">(");
            }
            else
            {
                builder.Append(indent)
                    .Append("    return self.__lakona_AskAsync<")
                    .Append(requestType)
                    .Append(", ")
                    .Append(method.ResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine(">(");
            }

            builder.Append(indent).Append("        \"").Append(EscapeStringLiteral(method.Name)).AppendLine("\",");
            builder.Append(indent).Append("        \"").Append(EscapeStringLiteral(remoteKind)).AppendLine("\",");
            builder.Append(indent).Append(method.HasCancellationToken ? "        true," : "        false,").AppendLine();
            builder.Append(indent).AppendLine("        request,");
            builder.Append(indent).AppendLine("        cancellationToken);");
            builder.Append(indent).AppendLine("}");
        }

        private static string GenerateActorContractsSource(HotfixActorApiInfo[] contracts)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine();

            foreach (var contract in contracts)
            {
                AppendActorContract(builder, contract);
                builder.AppendLine();
            }

            AppendActorRegistration(builder, contracts);
            return builder.ToString();
        }

        private static void AppendActorContract(StringBuilder builder, HotfixActorApiInfo contract)
        {
            var prefix = GetActorPrefix(contract.Actor.Name);
            var actorsType = prefix + "Actors";
            var distributedRefType = prefix + "Ref";
            var localRefType = prefix + "LocalRef";
            var remoteRefType = prefix + "RemoteRef";
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
            AppendHotfixActorsClass(builder, contract, actorsType, distributedRefType, localRefType, remoteRefType, keyType);
            builder.AppendLine();
            AppendHotfixDistributedRef(builder, contract, distributedRefType, keyType);
            builder.AppendLine();
            AppendHotfixLocalRef(builder, contract, localRefType, keyType);
            builder.AppendLine();
            AppendHotfixRemoteRef(builder, contract, remoteRefType, keyType);

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
            }

            builder.AppendLine("}");
        }

        private static void AppendHotfixActorsClass(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            string actorsType,
            string distributedRefType,
            string localRefType,
            string remoteRefType,
            string keyType)
        {
            builder.Append("public sealed class ").Append(actorsType).AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            builder.AppendLine("    private readonly global::System.IServiceProvider _services;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.RemoteActorOptions _options;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorDirectory _directory;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorDirectoryCache _directoryCache;");
            builder.AppendLine();
            builder.Append("    public ").Append(actorsType).AppendLine("(");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorRuntime runtime,");
            builder.AppendLine("        global::System.IServiceProvider services,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.RemoteActorOptions options,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorDirectory directory,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorDirectoryCache directoryCache)");
            builder.AppendLine("    {");
            builder.AppendLine("        _runtime = runtime;");
            builder.AppendLine("        _services = services;");
            builder.AppendLine("        _options = options;");
            builder.AppendLine("        _directory = directory;");
            builder.AppendLine("        _directoryCache = directoryCache;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public ").Append(distributedRefType).Append(" Get(").Append(keyType).AppendLine(" id)");
            builder.AppendLine("    {");
            builder.Append("        return new ").Append(distributedRefType).AppendLine("(_runtime, _services, _options, _directory, _directoryCache, id);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public ").Append(localRefType).Append(" Local(").Append(keyType).AppendLine(" id)");
            builder.AppendLine("    {");
            builder.Append("        return new ").Append(localRefType).AppendLine("(_runtime, id);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public ").Append(remoteRefType).Append(" Remote(global::Lakona.Game.Cluster.NodeId nodeId, ").Append(keyType).AppendLine(" id)");
            builder.AppendLine("    {");
            builder.Append("        return new ").Append(remoteRefType).AppendLine("(_services, _options, nodeId, id);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private static void AppendHotfixDistributedRef(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            string refType,
            string keyType)
        {
            builder.Append("public readonly struct ").Append(refType).AppendLine();
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
            AppendHotfixDistributedTellHelper(builder, contract);
            builder.AppendLine();
            AppendHotfixDistributedAskHelper(builder, contract);

            builder.AppendLine();
            AppendHotfixResolveNodeMethod(builder, LowerFirst(GetActorPrefix(contract.Actor.Name)), indentLevel: 1);
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
            builder.Append("public readonly struct ").Append(refType).AppendLine();
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
            AppendHotfixLocalTellHelper(builder, contract);
            builder.AppendLine();
            AppendHotfixLocalTryTellHelper(builder, contract);
            builder.AppendLine();
            AppendHotfixLocalAskHelper(builder, contract);

            builder.AppendLine("}");
        }

        private static void AppendHotfixLocalTellHelper(
            StringBuilder builder,
            HotfixActorApiInfo contract)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.AppendLine("    internal async global::System.Threading.Tasks.ValueTask __lakona_TellAsync<TRequest>(");
            builder.AppendLine("        string behaviorMethodName,");
            builder.AppendLine("        string remoteKind,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.Append("        await _runtime.TellAsync<").Append(actorType).AppendLine(">(");
            builder.AppendLine("            actorId,");
            builder.AppendLine("            (actor, ct) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeValueTaskAsync(");
            builder.Append("                typeof(").Append(actorType).AppendLine("),");
            builder.AppendLine("                behaviorMethodName,");
            builder.AppendLine("                actor,");
            AppendGenericParameterTypeArray(builder, indentLevel: 4);
            builder.AppendLine(",");
            AppendGenericArgumentArray(builder, indentLevel: 4);
            builder.AppendLine("),");
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
            builder.AppendLine("        string remoteKind,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.Append("        return _runtime.TryTell<").Append(actorType).AppendLine(">(");
            builder.AppendLine("            actorId,");
            builder.AppendLine("            (actor, ct) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeValueTaskAsync(");
            builder.Append("                typeof(").Append(actorType).AppendLine("),");
            builder.AppendLine("                behaviorMethodName,");
            builder.AppendLine("                actor,");
            AppendGenericParameterTypeArray(builder, indentLevel: 4);
            builder.AppendLine(",");
            AppendGenericArgumentArray(builder, indentLevel: 4);
            builder.AppendLine("),");
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
            builder.AppendLine("        string remoteKind,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.Append("        return await _runtime.AskAsync<").Append(actorType).AppendLine(", TResult>(");
            builder.AppendLine("            actorId,");
            builder.AppendLine("            (actor, ct) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeValueTaskAsync<TResult>(");
            builder.Append("                typeof(").Append(actorType).AppendLine("),");
            builder.AppendLine("                behaviorMethodName,");
            builder.AppendLine("                actor,");
            AppendGenericParameterTypeArray(builder, indentLevel: 4);
            builder.AppendLine(",");
            AppendGenericArgumentArray(builder, indentLevel: 4);
            builder.AppendLine("),");
            builder.AppendLine("            cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixDistributedTellHelper(
            StringBuilder builder,
            HotfixActorApiInfo contract)
        {
            var actorType = contract.Actor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var actorName = LowerFirst(GetActorPrefix(contract.Actor.Name));

            builder.AppendLine("    internal async global::System.Threading.Tasks.ValueTask __lakona_TellAsync<TRequest>(");
            builder.AppendLine("        string behaviorMethodName,");
            builder.AppendLine("        string remoteKind,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.AppendLine("        if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.AppendLine("        {");
            builder.Append("            await _runtime.TellAsync<").Append(actorType).AppendLine(">(");
            builder.AppendLine("                actorId,");
            builder.AppendLine("                (actor, ct) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeValueTaskAsync(");
            builder.Append("                    typeof(").Append(actorType).AppendLine("),");
            builder.AppendLine("                    behaviorMethodName,");
            builder.AppendLine("                    actor,");
            AppendGenericParameterTypeArray(builder, indentLevel: 5);
            builder.AppendLine(",");
            AppendGenericArgumentArray(builder, indentLevel: 5);
            builder.AppendLine("),");
            builder.AppendLine("                cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            return;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var node = await ResolveNodeAsync(actorId, remoteKind, cancellationToken).ConfigureAwait(false);");
            AppendHotfixRemoteInvocationSetup(builder, actorName, "remoteKind", "node", indentLevel: 2, includeActorId: false, methodNameIsExpression: true);
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = await _remote.TellAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.Append("            global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureAccepted(result, actorId, \"")
                .Append(EscapeStringLiteral(actorName))
                .AppendLine("\", remoteKind, node, correlationId);");
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
            var actorName = LowerFirst(GetActorPrefix(contract.Actor.Name));

            builder.AppendLine("    internal async global::System.Threading.Tasks.ValueTask<TResult> __lakona_AskAsync<TRequest, TResult>(");
            builder.AppendLine("        string behaviorMethodName,");
            builder.AppendLine("        string remoteKind,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixActorIdSetup(builder, indentLevel: 2);
            builder.AppendLine("        if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.AppendLine("        {");
            builder.Append("            return await _runtime.AskAsync<").Append(actorType).AppendLine(", TResult>(");
            builder.AppendLine("                actorId,");
            builder.AppendLine("                (actor, ct) => global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeValueTaskAsync<TResult>(");
            builder.Append("                    typeof(").Append(actorType).AppendLine("),");
            builder.AppendLine("                    behaviorMethodName,");
            builder.AppendLine("                    actor,");
            AppendGenericParameterTypeArray(builder, indentLevel: 5);
            builder.AppendLine(",");
            AppendGenericArgumentArray(builder, indentLevel: 5);
            builder.AppendLine("),");
            builder.AppendLine("                cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var node = await ResolveNodeAsync(actorId, remoteKind, cancellationToken).ConfigureAwait(false);");
            AppendHotfixRemoteInvocationSetup(builder, actorName, "remoteKind", "node", indentLevel: 2, includeActorId: false, methodNameIsExpression: true);
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = await _remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.Append("            global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(result, actorId, \"")
                .Append(EscapeStringLiteral(actorName))
                .AppendLine("\", remoteKind, node, correlationId);");
            builder.AppendLine("            return _serializer.Deserialize<TResult>(result.Payload);");
            builder.AppendLine("        }");
            builder.AppendLine("        catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.AppendLine("        {");
            builder.AppendLine("            _directoryCache.Remove(actorId);");
            builder.AppendLine("            throw;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixRemoteRef(
            StringBuilder builder,
            HotfixActorApiInfo contract,
            string refType,
            string keyType)
        {
            builder.Append("public readonly struct ").Append(refType).AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::System.IServiceProvider _services;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.RemoteActorOptions _options;");
            builder.AppendLine("    private readonly global::Lakona.Game.Cluster.NodeId _node;");
            builder.Append("    private readonly ").Append(keyType).AppendLine(" _id;");
            builder.AppendLine("    private global::Lakona.Game.Server.Actors.IRemoteActorInvoker _remote => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Actors.IRemoteActorInvoker>(_services);");
            builder.AppendLine("    private global::Lakona.Game.Server.Actors.IRemoteActorSerializer _serializer => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Actors.IRemoteActorSerializer>(_services);");
            builder.AppendLine();
            builder.Append("    public ").Append(refType).AppendLine("(");
            builder.AppendLine("        global::System.IServiceProvider services,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.RemoteActorOptions options,");
            builder.AppendLine("        global::Lakona.Game.Cluster.NodeId nodeId,");
            builder.Append("        ").Append(keyType).AppendLine(" id)");
            builder.AppendLine("    {");
            builder.AppendLine("        _services = services;");
            builder.AppendLine("        _options = options;");
            builder.AppendLine("        _node = nodeId;");
            builder.AppendLine("        _id = id;");
            builder.AppendLine("    }");

            builder.AppendLine();
            AppendHotfixRemoteTellHelper(builder, contract);
            builder.AppendLine();
            AppendHotfixRemoteAskHelper(builder, contract);

            builder.AppendLine("}");
        }

        private static void AppendHotfixRemoteTellHelper(
            StringBuilder builder,
            HotfixActorApiInfo contract)
        {
            var actorName = LowerFirst(GetActorPrefix(contract.Actor.Name));

            builder.AppendLine("    internal async global::System.Threading.Tasks.ValueTask __lakona_TellAsync<TRequest>(");
            builder.AppendLine("        string behaviorMethodName,");
            builder.AppendLine("        string remoteKind,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixRemoteInvocationSetup(builder, actorName, "remoteKind", "_node", indentLevel: 2, includeActorId: true, methodNameIsExpression: true);
            builder.AppendLine("        var result = await _remote.TellAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.Append("        global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureAccepted(result, actorId, \"")
                .Append(EscapeStringLiteral(actorName))
                .AppendLine("\", remoteKind, _node, correlationId);");
            builder.AppendLine("    }");
        }

        private static void AppendHotfixRemoteAskHelper(
            StringBuilder builder,
            HotfixActorApiInfo contract)
        {
            var actorName = LowerFirst(GetActorPrefix(contract.Actor.Name));

            builder.AppendLine("    internal async global::System.Threading.Tasks.ValueTask<TResult> __lakona_AskAsync<TRequest, TResult>(");
            builder.AppendLine("        string behaviorMethodName,");
            builder.AppendLine("        string remoteKind,");
            builder.AppendLine("        bool passCancellationToken,");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            AppendHotfixRemoteInvocationSetup(builder, actorName, "remoteKind", "_node", indentLevel: 2, includeActorId: true, methodNameIsExpression: true);
            builder.AppendLine("        var result = await _remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.Append("        global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(result, actorId, \"")
                .Append(EscapeStringLiteral(actorName))
                .AppendLine("\", remoteKind, _node, correlationId);");
            builder.AppendLine("        return _serializer.Deserialize<TResult>(result.Payload);");
            builder.AppendLine("    }");
        }

        private static void AppendActorRegistration(StringBuilder builder, HotfixActorApiInfo[] contracts)
        {
            builder.AppendLine("public sealed class GeneratedHotfixActorRegistration :");
            builder.AppendLine("    global::Lakona.Game.Server.Hotfix.IHotfixGeneratedServiceRegistration");
            builder.AppendLine("{");
            builder.AppendLine("    public void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            builder.AppendLine("    {");
            foreach (var contract in contracts)
            {
                var prefix = GetActorPrefix(contract.Actor.Name);
                var actorNamespace = contract.Actor.ContainingNamespace.IsGlobalNamespace
                    ? string.Empty
                    : contract.Actor.ContainingNamespace.ToDisplayString() + ".";
                builder.Append("        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<global::")
                    .Append(actorNamespace)
                    .Append(prefix)
                    .AppendLine("Actors>(services);");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private static string GenerateRpcServiceExtensionSource(HotfixRpcServiceInfo[] services)
        {
            var firstService = services[0];
            var namespaceName = firstService.GeneratedServerNamespace;
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.Append("namespace ").Append(namespaceName).AppendLine(";");
            builder.AppendLine();

            foreach (var service in services.OrderBy(static service => service.Contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            {
                AppendEndpointRpcServiceBinder(builder, service);
            }

            builder.AppendLine("internal sealed class GeneratedHotfixRequiredServiceContracts :");
            builder.AppendLine("    global::Lakona.Game.Server.Hotfix.Abstractions.IHotfixRequiredServiceContracts");
            builder.AppendLine("{");
            builder.AppendLine("    public global::System.Collections.Generic.IReadOnlyList<global::System.Type> ServiceContracts { get; } =");
            builder.AppendLine("    [");
            foreach (var service in services.OrderBy(static service => service.Contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            {
                builder.Append("        typeof(").Append(service.Contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).AppendLine("),");
            }

            builder.AppendLine("    ];");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendEndpointRpcServiceBinder(StringBuilder builder, HotfixRpcServiceInfo service)
        {
            var serviceName = GetEndpointServiceName(service);
            var binderTypeName = GetServiceTypeName(service.Contract.Name) + "EndpointBinder";

            builder.Append("[global::Lakona.Game.Server.Hosting.LakonaRpcServiceAttribute(\"")
                .Append(EscapeStringLiteral(serviceName))
                .AppendLine("\")]");
            builder.Append("internal sealed class ").Append(binderTypeName)
                .AppendLine(" : global::Lakona.Game.Server.Hosting.LakonaRpcServiceBinder");
            builder.AppendLine("{");
            builder.AppendLine("    public override void Bind(global::Lakona.Game.Server.Hosting.LakonaGameServerRpcContext context)");
            builder.AppendLine("    {");
            builder.AppendLine("        var registry = context.Builder.ServiceRegistry;");
            builder.AppendLine("        var services = context.Services;");
            AppendRpcServiceBinding(builder, service);
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendRpcServiceBinding(StringBuilder builder, HotfixRpcServiceInfo service)
        {
            var generatedNamespace = service.GeneratedProxyNamespace;
            var proxyType = GetGeneratedProxyTypeDisplay(service);
            var rpcServiceAttribute = service.Contract.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            var callbackType = GetNamedTypeArgument(rpcServiceAttribute, "NotificationContract");
            var binderName = generatedNamespace + "." + GetBinderTypeName(service.Contract.Name);

            builder.Append("        global::").Append(binderName).AppendLine(".BindFactory(");
            builder.AppendLine("            registry,");
            builder.AppendLine("            session => new " + proxyType + "(");
            builder.AppendLine("                global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Hotfix.IHotfixRuntimeAccessor>(services),");
            builder.AppendLine("                global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Actors.IActorRuntime>(services),");
            builder.AppendLine("                global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.ILakonaGameServer>(services),");
            if (callbackType != null)
            {
                var callbackProxyName = generatedNamespace + "." + GetNotificationProxyTypeName(callbackType.Name);
                builder.Append("                new global::").Append(callbackProxyName).AppendLine("(session),");
            }

            builder.AppendLine("                session.ContextId));");
            builder.AppendLine();
        }

        private static bool ValidateRpcService(SourceProductionContext context, HotfixRpcServiceInfo service)
        {
            if (!IsSupportedRpcServiceContract(service))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.UnsupportedServiceContract,
                    service.Contract.Locations.FirstOrDefault(),
                    service.Contract.ToDisplayString()));
                return false;
            }

            var rpcServiceAttribute = service.Contract.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            var callbackType = GetNamedTypeArgument(rpcServiceAttribute, "NotificationContract");
            if (callbackType != null && callbackType.TypeKind != TypeKind.Interface)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.UnsupportedNotificationContract,
                    service.Contract.Locations.FirstOrDefault(),
                    service.Contract.ToDisplayString()));
                return false;
            }

            foreach (var method in GetContractMethods(service.Contract))
            {
                var methodDisplay = method.ToDisplayString();
                var rpcMethod = method.GetAttributes()
                    .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcMethodAttributeName);
                if (rpcMethod == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.RpcMethodAttributeRequired,
                        method.Locations.FirstOrDefault() ?? service.Contract.Locations.FirstOrDefault(),
                        methodDisplay));
                    return false;
                }

                if (method.Parameters.Length != 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.RpcMethodRequiresSingleRequest,
                        method.Locations.FirstOrDefault() ?? service.Contract.Locations.FirstOrDefault(),
                        methodDisplay));
                    return false;
                }

                if (!IsSupportedRpcReturnType(method.ReturnType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.UnsupportedRpcMethodReturnType,
                        method.Locations.FirstOrDefault() ?? service.Contract.Locations.FirstOrDefault(),
                        methodDisplay));
                    return false;
                }
            }

            return true;
        }

        private static bool IsSupportedRpcService(HotfixRpcServiceInfo service)
        {
            if (!IsSupportedRpcServiceContract(service))
            {
                return false;
            }

            var rpcServiceAttribute = service.Contract.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            var callbackType = GetNamedTypeArgument(rpcServiceAttribute, "NotificationContract");
            if (callbackType != null && callbackType.TypeKind != TypeKind.Interface)
            {
                return false;
            }

            return GetContractMethods(service.Contract)
                .All(method => method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == RpcMethodAttributeName) &&
                    method.Parameters.Length == 1 &&
                    IsSupportedRpcReturnType(method.ReturnType));
        }

        private static bool IsSupportedRpcServiceContract(HotfixRpcServiceInfo service)
        {
            return service.Contract.TypeKind == TypeKind.Interface &&
                service.Contract.GetAttributes()
                    .Any(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
        }

        private static IEnumerable<IMethodSymbol> GetContractMethods(INamedTypeSymbol contractType)
        {
            return contractType.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(method => method.MethodKind == MethodKind.Ordinary);
        }

        private static IEnumerable<INamedTypeSymbol> DiscoverRpcServiceContracts(Compilation compilation, CancellationToken cancellationToken)
        {
            if (compilation.GetTypeByMetadataName("Lakona.Game.Server.Hosting.LakonaGameServerBuilder") is null)
            {
                yield break;
            }

            var seen = new HashSet<string>();
            foreach (var contract in EnumerateTypes(compilation.Assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsUserRpcService(contract) && seen.Add(contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                {
                    yield return contract;
                }
            }

            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var contract in EnumerateTypes(assembly.GlobalNamespace))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsUserRpcService(contract) && seen.Add(contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                    {
                        yield return contract;
                    }
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

        private static HotfixActorApiInfo CreateBehaviorActorContract(HotfixBehaviorInfo behavior, string hotfixAssemblyName)
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

            return new HotfixActorApiInfo(
                behavior.Behavior,
                behavior.Actor,
                keyType ?? behavior.Actor,
                diagnostics.Count == 0 ? methods.ToArray() : Array.Empty<HotfixActorMethodInfo>(),
                diagnostics.ToArray());
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
                !method.IsStatic ||
                !method.IsExtensionMethod ||
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

        private static string GetRuntimeTypeIdentity(ITypeSymbol type)
        {
            var assemblyName = type.ContainingAssembly?.Identity.Name ?? string.Empty;
            return GetRuntimeTypeFullName(type) + ", " + assemblyName;
        }

        private static string GetRuntimeTypeFullName(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol arrayType)
            {
                return GetRuntimeTypeFullName(arrayType.ElementType) + "[]";
            }

            if (type is INamedTypeSymbol namedType)
            {
                var containingTypes = new Stack<string>();
                for (INamedTypeSymbol? current = namedType; current != null; current = current.ContainingType)
                {
                    containingTypes.Push(current.MetadataName);
                }

                var typeName = string.Join("+", containingTypes);
                if (namedType.IsGenericType && namedType.TypeArguments.Length > 0)
                {
                    typeName += "[[" +
                        string.Join("],[", namedType.TypeArguments.Select(GetRuntimeAssemblyQualifiedTypeIdentity)) +
                        "]]";
                }

                var namespaceName = namedType.ContainingNamespace.IsGlobalNamespace
                    ? string.Empty
                    : namedType.ContainingNamespace.ToDisplayString() + ".";
                return namespaceName + typeName;
            }

            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);
        }

        private static string GetRuntimeAssemblyQualifiedTypeIdentity(ITypeSymbol type)
        {
            var assemblyDisplayName = type.ContainingAssembly?.Identity.GetDisplayName();
            return string.IsNullOrEmpty(assemblyDisplayName)
                ? GetRuntimeTypeFullName(type)
                : GetRuntimeTypeFullName(type) + ", " + assemblyDisplayName;
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

        private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
        {
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                foreach (var nested in EnumerateTypes(type))
                {
                    yield return nested;
                }
            }

            foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                foreach (var type in EnumerateTypes(childNamespace))
                {
                    yield return type;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamedTypeSymbol type)
        {
            yield return type;
            foreach (var nested in type.GetTypeMembers())
            {
                foreach (var item in EnumerateTypes(nested))
                {
                    yield return item;
                }
            }
        }

        private static bool IsUserRpcService(INamedTypeSymbol contract)
        {
            var assemblyName = contract.ContainingAssembly?.Name ?? string.Empty;
            return !assemblyName.StartsWith("Lakona.", System.StringComparison.Ordinal) &&
                HasRpcServiceAttribute(contract);
        }

        private static bool HasRpcServiceAttribute(INamedTypeSymbol contract)
        {
            return contract.GetAttributes().Any(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
        }

        private static bool IsSupportedRpcReturnType(ITypeSymbol returnType)
        {
            return returnType is INamedTypeSymbol namedReturn &&
                namedReturn.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
                namedReturn.Name == "ValueTask" &&
                (!namedReturn.IsGenericType || namedReturn.TypeArguments.Length == 1);
        }

        private static string GenerateRpcServiceSource(HotfixRpcServiceInfo service)
        {
            var namespaceName = service.GeneratedProxyNamespace;
            var contractDisplay = service.Contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var proxyName = GetServiceTypeName(service.Contract.Name) + "Proxy";
            var rpcServiceAttribute = service.Contract.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            var callbackType = GetNamedTypeArgument(rpcServiceAttribute, "NotificationContract");
            var callbackDisplay = callbackType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.Append("namespace ").Append(namespaceName).AppendLine(";");
            builder.AppendLine();

            builder.Append("internal sealed class ").Append(proxyName).Append(" : ").Append(contractDisplay).AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Hotfix.IHotfixRuntimeAccessor _hotfixRuntime;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _actors;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.ILakonaGameServer _gameServer;");
            builder.AppendLine("    private readonly string _connectionId;");
            if (callbackDisplay != null)
            {
                builder.Append("    private readonly ").Append(callbackDisplay).AppendLine(" _callback;");
            }

            builder.AppendLine();
            builder.Append("    public ").Append(proxyName).AppendLine("(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.IHotfixRuntimeAccessor hotfixRuntime,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorRuntime actors,");
            builder.AppendLine("        global::Lakona.Game.Server.ILakonaGameServer gameServer,");
            if (callbackDisplay != null)
            {
                builder.Append("        ").Append(callbackDisplay).AppendLine(" callback,");
            }

            builder.AppendLine("        string connectionId)");
            builder.AppendLine("    {");
            builder.AppendLine("        _hotfixRuntime = hotfixRuntime;");
            builder.AppendLine("        _actors = actors;");
            builder.AppendLine("        _gameServer = gameServer;");
            if (callbackDisplay != null)
            {
                builder.AppendLine("        _callback = callback;");
            }

            builder.AppendLine("        _connectionId = connectionId;");
            builder.AppendLine("    }");

            foreach (var method in service.Contract.GetMembers().OfType<IMethodSymbol>().Where(method => method.MethodKind == MethodKind.Ordinary))
            {
                AppendRpcProxyMethod(builder, contractDisplay, method, callbackDisplay);
            }

            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendRpcProxyMethod(StringBuilder builder, string contractDisplay, IMethodSymbol method, string? callbackDisplay)
        {
            var rpcMethod = method.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcMethodAttributeName);
            if (rpcMethod == null || method.Parameters.Length != 1)
            {
                return;
            }

            var requestDisplay = method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var returnDisplay = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var methodId = rpcMethod.ConstructorArguments.Length > 0
                ? rpcMethod.ConstructorArguments[0].Value?.ToString() ?? "0"
                : "0";
            var returnsResult = method.ReturnType is INamedTypeSymbol namedReturn &&
                namedReturn.IsGenericType &&
                namedReturn.Name == "ValueTask" &&
                namedReturn.TypeArguments.Length == 1;
            var resultDisplay = returnsResult && method.ReturnType is INamedTypeSymbol valueTask
                ? valueTask.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;
            var callType = callbackDisplay == null
                ? "global::Lakona.Game.Server.Hotfix.HotfixServiceCall<" + requestDisplay + ">"
                : "global::Lakona.Game.Server.Hotfix.HotfixServiceCall<" + requestDisplay + ", " + callbackDisplay + ">";

            builder.AppendLine();
            builder.Append("    public async ").Append(returnDisplay).Append(' ').Append(method.Name).Append('(')
                .Append(requestDisplay).Append(' ').Append(method.Parameters[0].Name).AppendLine(")");
            builder.AppendLine("    {");
            builder.AppendLine("        using var lease = _hotfixRuntime.AcquireCurrent();");
            builder.AppendLine("        var snapshot = lease.Snapshot;");
            builder.AppendLine("        var sessions = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions");
            builder.AppendLine("            .GetRequiredService<global::Lakona.Game.Server.Sessions.IGameSessionRegistry>(snapshot.Services);");
            builder.AppendLine("        var currentSession = await sessions");
            builder.AppendLine("            .GetCurrentSessionAsync(_connectionId, global::System.Threading.CancellationToken.None)");
            builder.AppendLine("            .ConfigureAwait(false);");
            builder.AppendLine("        var currentSessionItems = currentSession is { } sessionKey");
            builder.AppendLine("            ? await sessions.GetSessionItemsAsync(sessionKey, global::System.Threading.CancellationToken.None).ConfigureAwait(false)");
            builder.AppendLine("            : global::Lakona.Game.Server.Sessions.GameSessionItems.Empty;");
            builder.Append("        ");
            if (returnsResult)
            {
                builder.Append("return ");
            }

            builder.Append("await snapshot.Invoker.InvokeAsync<").Append(contractDisplay).Append(", ").Append(callType);
            if (returnsResult)
            {
                builder.Append(", ").Append(resultDisplay);
            }

            builder.AppendLine(">(");
            builder.Append("            ").Append(methodId).AppendLine(",");
            builder.Append("            new ").Append(callType).AppendLine("(");
            builder.Append("                ").Append(method.Parameters[0].Name).AppendLine(",");
            builder.AppendLine("                _connectionId,");
            if (callbackDisplay != null)
            {
                builder.AppendLine("                _callback,");
            }

            builder.AppendLine("                currentSession,");
            builder.AppendLine("                currentSessionItems,");
            builder.AppendLine("                snapshot.Services,");
            builder.AppendLine("                _actors,");
            builder.AppendLine("                _gameServer))");
            builder.AppendLine("            .ConfigureAwait(false);");
            builder.AppendLine("    }");
        }

        private static void GenerateState(SourceProductionContext context, HotfixStateInfo? state)
        {
            if (state == null)
            {
                return;
            }

            if (!state.Declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.StateMustBePartial,
                    state.Declaration.Identifier.GetLocation(),
                    state.Symbol.ToDisplayString()));
                return;
            }

            var nonPartialContainer = state.ContainingTypes.FirstOrDefault(type => !IsPartial(type.Declaration));
            if (nonPartialContainer != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.ContainingTypeMustBePartial,
                    nonPartialContainer.Declaration.Identifier.GetLocation(),
                    nonPartialContainer.Symbol.ToDisplayString(),
                    state.Symbol.ToDisplayString()));
                return;
            }

            var hintName = CreateHintName(state.Symbol);
            context.AddSource(hintName, SourceText.From(GenerateStateSource(state), Encoding.UTF8));
        }

        private static void GenerateStateCaller(SourceProductionContext context, HotfixStateInfo? state)
        {
            if (state == null || state.Symbol.IsGenericType)
            {
                return;
            }

            var hintName = CreateCallerHintName(state.Symbol);
            context.AddSource(hintName, SourceText.From(GenerateStateCallerSource(state), Encoding.UTF8));
        }

        private static string GenerateStateCallerSource(HotfixStateInfo state)
        {
            var namespaceName = state.Symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : state.Symbol.ContainingNamespace.ToDisplayString();
            var stateDisplay = state.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var callerClassName = state.Symbol.Name + "HotfixCaller";

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using Lakona.Game.Server.Hotfix.Dispatch;");
            builder.AppendLine();

            if (namespaceName != null)
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine(";");
                builder.AppendLine();
            }

            builder.Append("public static class ").Append(callerClassName).AppendLine();
            builder.AppendLine("{");
            builder.Append("    public static TResult Call<TResult>(this ").Append(stateDisplay).AppendLine(" self, string methodName)");
            builder.AppendLine("    {");
            builder.Append("        return HotfixDispatch.Invoke<").Append(stateDisplay).AppendLine(", TResult>(methodName, self);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public static TResult Call<TArg, TResult>(this ").Append(stateDisplay).AppendLine(" self, string methodName, TArg arg)");
            builder.AppendLine("    {");
            builder.Append("        return HotfixDispatch.Invoke<").Append(stateDisplay).AppendLine(", TArg, TResult>(methodName, self, arg);");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }

        private static bool IsPartial(TypeDeclarationSyntax declaration)
        {
            return declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
        }

        private static string GenerateStateSource(HotfixStateInfo state)
        {
            var namespaceName = state.Symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : state.Symbol.ContainingNamespace.ToDisplayString();

            var fields = state.Symbol.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(IsFriendAccessorField)
                .OrderBy(field => field.Locations.Length == 0 ? 0 : field.Locations[0].SourceSpan.Start)
                .ToArray();

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System.ComponentModel;");
            builder.AppendLine();

            if (namespaceName != null)
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine();
                builder.AppendLine("{");
            }

            AppendContainingTypes(builder, state, fields, namespaceName != null ? 1 : 0);

            if (namespaceName != null)
            {
                builder.AppendLine("}");
            }

            return builder.ToString();
        }

        private static bool IsFriendAccessorField(IFieldSymbol field)
        {
            return field.DeclaredAccessibility == Accessibility.Private &&
                !field.IsImplicitlyDeclared &&
                field.AssociatedSymbol == null &&
                !field.IsStatic &&
                !field.IsConst;
        }

        private static void AppendContainingTypes(StringBuilder builder, HotfixStateInfo state, IFieldSymbol[] fields, int indentLevel)
        {
            foreach (var containingType in state.ContainingTypes)
            {
                AppendTypeHeader(builder, containingType.Declaration, indentLevel);
                builder.Append(new string(' ', indentLevel * 4)).AppendLine("{");
                indentLevel++;
            }

            AppendStateType(builder, state.Declaration, fields, indentLevel);

            for (var index = state.ContainingTypes.Length - 1; index >= 0; index--)
            {
                indentLevel--;
                builder.Append(new string(' ', indentLevel * 4)).AppendLine("}");
            }
        }

        private static void AppendStateType(StringBuilder builder, TypeDeclarationSyntax declaration, IFieldSymbol[] fields, int indentLevel)
        {
            var indent = new string(' ', indentLevel * 4);
            var usedAccessorNames = new HashSet<string>();
            var normalizedNameCounts = fields
                .GroupBy(field => NormalizeFieldName(field.Name))
                .ToDictionary(group => group.Key, group => group.Count());

            AppendTypeHeader(builder, declaration, indentLevel);
            builder.Append(indent).AppendLine("{");

            foreach (var field in fields)
            {
                var accessorName = CreateUniqueAccessorName(field.Name, normalizedNameCounts, usedAccessorNames);

                builder.Append(indent).AppendLine("    [EditorBrowsable(EditorBrowsableState.Never)]");
                builder.Append(indent).Append("    public ")
                    .Append(field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append(' ')
                    .Append(accessorName)
                    .AppendLine("()");
                builder.Append(indent).AppendLine("    {");
                builder.Append(indent).Append("        return ").Append(EscapeIdentifier(field.Name)).AppendLine(";");
                builder.Append(indent).AppendLine("    }");
                builder.AppendLine();
            }

            builder.Append(indent).AppendLine("    [EditorBrowsable(EditorBrowsableState.Never)]");
            builder.Append(indent).AppendLine("    public static string __hotfix_dispatch_marker()");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        return typeof(global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch).FullName ?? string.Empty;");
            builder.Append(indent).AppendLine("    }");
            builder.AppendLine();

            builder.Append(indent).AppendLine("}");
        }

        private static void AppendTypeHeader(StringBuilder builder, TypeDeclarationSyntax declaration, int indentLevel)
        {
            builder.Append(new string(' ', indentLevel * 4))
                .Append(GetTypeModifiers(declaration))
                .Append(' ')
                .Append(declaration.Keyword.ValueText)
                .Append(' ')
                .Append(declaration.Identifier.ValueText)
                .Append(declaration.TypeParameterList != null ? declaration.TypeParameterList.ToString() : string.Empty)
                .AppendLine();

            foreach (var constraint in declaration.ConstraintClauses)
            {
                builder.Append(new string(' ', (indentLevel + 1) * 4))
                    .AppendLine(constraint.ToString());
            }
        }

        private static string GetTypeModifiers(TypeDeclarationSyntax declaration)
        {
            return string.Join(" ", declaration.Modifiers.Select(modifier => modifier.ValueText));
        }

        private static string CreateUniqueAccessorName(
            string fieldName,
            Dictionary<string, int> normalizedNameCounts,
            HashSet<string> usedAccessorNames)
        {
            var normalizedName = NormalizeFieldName(fieldName);
            if (fieldName.StartsWith("_", System.StringComparison.Ordinal) &&
                normalizedNameCounts.TryGetValue(normalizedName, out var count) &&
                count > 1)
            {
                normalizedName = fieldName;
            }

            var candidate = "__hotfix_" + SanitizeIdentifierPart(normalizedName);
            if (usedAccessorNames.Add(candidate))
            {
                return candidate;
            }

            candidate = "__hotfix_" + SanitizeIdentifierPart(fieldName.TrimStart('@'));
            if (usedAccessorNames.Add(candidate))
            {
                return candidate;
            }

            var suffix = 2;
            while (!usedAccessorNames.Add(candidate + "_" + suffix))
            {
                suffix++;
            }

            return candidate + "_" + suffix;
        }

        private static string NormalizeFieldName(string fieldName)
        {
            var normalizedName = fieldName.TrimStart('_');
            return normalizedName.Length == 0 ? fieldName : normalizedName;
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

        private static string EscapeIdentifier(string identifier)
        {
            return SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None
                ? identifier
                : "@" + identifier;
        }

        private static string CreateHintName(INamedTypeSymbol symbol)
        {
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty)
                .Replace('.', '_')
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace(',', '_')
                .Replace(' ', '_') + ".Hotfix.g.cs";
        }

        private static string CreateCallerHintName(INamedTypeSymbol symbol)
        {
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty)
                .Replace('.', '_')
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace(',', '_')
                .Replace(' ', '_') + ".HotfixCaller.g.cs";
        }

        private static string CreateRpcServiceHintName(INamedTypeSymbol symbol)
        {
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty)
                .Replace('.', '_')
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace(',', '_')
                .Replace(' ', '_') + ".HotfixRpcService.g.cs";
        }

        private static string CreateActorWrapperHintName(INamedTypeSymbol symbol)
        {
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty)
                .Replace('.', '_')
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace(',', '_')
                .Replace(' ', '_') + ".HotfixActorRefs.g.cs";
        }

        private static string GetGeneratedProxyTypeDisplay(HotfixRpcServiceInfo service)
        {
            var proxyName = GetServiceTypeName(service.Contract.Name) + "Proxy";
            if (string.IsNullOrEmpty(service.GeneratedProxyNamespace))
            {
                return proxyName;
            }

            return "global::" + service.GeneratedProxyNamespace + "." + proxyName;
        }

        private static INamedTypeSymbol? GetNamedTypeArgument(AttributeData? attribute, string name)
        {
            if (attribute == null)
            {
                return null;
            }

            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == name && namedArgument.Value.Value is INamedTypeSymbol namedType)
                {
                    return namedType;
                }
            }

            return null;
        }

        private static string? GetNamedStringArgument(AttributeData? attribute, string name)
        {
            if (attribute == null)
            {
                return null;
            }

            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == name && namedArgument.Value.Value is string value)
                {
                    return value;
                }
            }

            return null;
        }

        private static string GetEndpointServiceName(HotfixRpcServiceInfo service)
        {
            var rpcServiceAttribute = service.Contract.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            var apiName = GetNamedStringArgument(rpcServiceAttribute, "ApiName");
            var sourceName = string.IsNullOrWhiteSpace(apiName)
                ? GetEndpointServiceBaseName(service.Contract.Name)
                : apiName!;

            return ToKebabCase(sourceName);
        }

        private static string GetServiceTypeName(string interfaceName)
        {
            return interfaceName.Length > 1 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1])
                ? interfaceName.Substring(1)
                : interfaceName;
        }

        private static string GetEndpointServiceBaseName(string interfaceName)
        {
            var name = GetServiceTypeName(interfaceName);
            const string suffix = "Service";
            return name.EndsWith(suffix, System.StringComparison.Ordinal) && name.Length > suffix.Length
                ? name.Substring(0, name.Length - suffix.Length)
                : name;
        }

        private static string ToKebabCase(string value)
        {
            var builder = new StringBuilder();
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (IsAsciiLetterOrDigit(character))
                {
                    if (IsAsciiUpper(character) && builder.Length > 0)
                    {
                        var previous = value[index - 1];
                        var next = index + 1 < value.Length ? value[index + 1] : '\0';
                        if (IsAsciiLower(previous) ||
                            IsAsciiDigit(previous) ||
                            (IsAsciiUpper(previous) && IsAsciiLower(next)))
                        {
                            AppendDash(builder);
                        }
                    }

                    builder.Append(ToAsciiLower(character));
                    continue;
                }

                AppendDash(builder);
            }

            while (builder.Length > 0 && builder[builder.Length - 1] == '-')
            {
                builder.Length--;
            }

            if (builder.Length == 0)
            {
                return "service";
            }

            if (IsAsciiDigit(builder[0]))
            {
                builder.Insert(0, "service-");
            }

            return builder.ToString();
        }

        private static void AppendDash(StringBuilder builder)
        {
            if (builder.Length > 0 && builder[builder.Length - 1] != '-')
            {
                builder.Append('-');
            }
        }

        private static bool IsAsciiLetterOrDigit(char value)
        {
            return IsAsciiLower(value) || IsAsciiUpper(value) || IsAsciiDigit(value);
        }

        private static bool IsAsciiLower(char value)
        {
            return value >= 'a' && value <= 'z';
        }

        private static bool IsAsciiUpper(char value)
        {
            return value >= 'A' && value <= 'Z';
        }

        private static bool IsAsciiDigit(char value)
        {
            return value >= '0' && value <= '9';
        }

        private static char ToAsciiLower(char value)
        {
            return IsAsciiUpper(value) ? (char)(value + ('a' - 'A')) : value;
        }

        private static string GetBinderTypeName(string interfaceName)
        {
            return GetServiceTypeName(interfaceName) + "Binder";
        }

        private static string GetNotificationProxyTypeName(string notificationContractInterfaceName)
        {
            return GetServiceTypeName(notificationContractInterfaceName) + "Proxy";
        }

        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void AppendHotfixActorIdSetup(StringBuilder builder, int indentLevel)
        {
            builder.Append(Indent(indentLevel))
                .AppendLine("var actorId = global::Lakona.Game.Server.Actors.ActorId.From(_id.ToString());");
        }

        private static void AppendHotfixRemoteInvocationSetup(
            StringBuilder builder,
            string actorName,
            string methodName,
            string nodeExpression,
            int indentLevel,
            bool includeActorId,
            bool methodNameIsExpression = false)
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
            builder.Append(indent).Append("    [global::Lakona.Game.Server.Hotfix.HotfixActorApiMetadata.MethodKeyKey] = ");
            if (methodNameIsExpression)
            {
                builder.Append(methodName);
            }
            else
            {
                builder.Append('"').Append(EscapeStringLiteral(methodName)).Append('"');
            }

            builder.AppendLine();
            builder.Append(indent).AppendLine("};");
            builder.Append(indent)
                .Append("var invocation = new global::Lakona.Game.Server.Actors.RemoteActorInvocation(")
                .Append(nodeExpression)
                .Append(", actorId, \"")
                .Append(EscapeStringLiteral(actorName))
                .Append("\", ");
            if (methodNameIsExpression)
            {
                builder.Append(methodName);
            }
            else
            {
                builder.Append('"').Append(EscapeStringLiteral(methodName)).Append('"');
            }

            builder.AppendLine(", payload, deadline, correlationId, metadata);");
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

        private static void AppendGenericParameterTypeArray(StringBuilder builder, int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent).AppendLine("passCancellationToken");
            builder.Append(indent).AppendLine("    ? new global::System.Type[] { typeof(TRequest), typeof(global::System.Threading.CancellationToken) }");
            builder.Append(indent).Append("    : new global::System.Type[] { typeof(TRequest) }");
        }

        private static void AppendGenericArgumentArray(StringBuilder builder, int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent).AppendLine("passCancellationToken");
            builder.Append(indent).AppendLine("    ? new object[] { request, cancellationToken }");
            builder.Append(indent).Append("    : new object[] { request }");
        }

        private static string DisplayReturnType(HotfixActorMethodInfo method)
        {
            if (method.ResultType == null)
            {
                return "global::System.Threading.Tasks.ValueTask";
            }

            return "global::System.Threading.Tasks.ValueTask<" +
                method.ResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                ">";
        }

        private static string GetRemoteKind(HotfixActorMethodInfo method)
        {
            return method.MethodKey;
        }

        private static string GetActorPrefix(string actorName)
        {
            return actorName.EndsWith("Actor", System.StringComparison.Ordinal) && actorName.Length > "Actor".Length
                ? actorName.Substring(0, actorName.Length - "Actor".Length)
                : actorName;
        }

        private static string CreateActorApiMethodKeyConstantName(HotfixActorMethodInfo method)
        {
            var builder = new StringBuilder();
            builder.Append(SanitizeIdentifierPart(method.Name));
            builder.Append('_');
            builder.Append(SanitizeIdentifierPart(GetRuntimeTypeFullName(method.RequestType)));
            builder.Append("_MethodKey");
            return builder.ToString();
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

        private static string GetAccessibility(INamedTypeSymbol symbol)
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

        private static string LowerFirst(string value)
        {
            if (value.Length == 0)
            {
                return value;
            }

            return char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        private static string Indent(int indentLevel)
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

        private sealed class HotfixStateInfo
        {
            public HotfixStateInfo(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration)
            {
                Symbol = symbol;
                Declaration = declaration;
                ContainingTypes = CreateContainingTypes(symbol, declaration);
            }

            public INamedTypeSymbol Symbol { get; }

            public TypeDeclarationSyntax Declaration { get; }

            public ContainingTypeInfo[] ContainingTypes { get; }
        }

        private sealed class HotfixBehaviorInfo
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

        private sealed class ContainingTypeInfo
        {
            public ContainingTypeInfo(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration)
            {
                Symbol = symbol;
                Declaration = declaration;
            }

            public INamedTypeSymbol Symbol { get; }

            public TypeDeclarationSyntax Declaration { get; }
        }

        private sealed class HotfixRpcServiceInfo
        {
            public HotfixRpcServiceInfo(
                INamedTypeSymbol contract,
                string generatedProxyNamespace,
                string generatedServerNamespace)
            {
                Contract = contract;
                GeneratedProxyNamespace = generatedProxyNamespace;
                GeneratedServerNamespace = generatedServerNamespace;
            }

            public INamedTypeSymbol Contract { get; }

            public string GeneratedProxyNamespace { get; }

            public string GeneratedServerNamespace { get; }
        }

        private sealed class HotfixActorGenerationInput
        {
            public HotfixActorGenerationInput(
                string assemblyName,
                HotfixBehaviorInfo[] behaviors)
            {
                AssemblyName = assemblyName;
                Behaviors = behaviors;
            }

            public string AssemblyName { get; }

            public HotfixBehaviorInfo[] Behaviors { get; }
        }

        private readonly struct HotfixGeneratorOptions : System.IEquatable<HotfixGeneratorOptions>
        {
            public HotfixGeneratorOptions(bool generateStableRpcServices)
            {
                GenerateStableRpcServices = generateStableRpcServices;
            }

            public bool GenerateStableRpcServices { get; }

            public static HotfixGeneratorOptions From(AnalyzerConfigOptions options)
            {
                return new HotfixGeneratorOptions(
                    IsEnabled(options, StableRpcServicesKey, defaultValue: true));
            }

            private static bool IsEnabled(AnalyzerConfigOptions options, string key, bool defaultValue)
            {
                if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    return defaultValue;
                }

                var trimmed = value.Trim();
                if (string.Equals(trimmed, "true", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "1", System.StringComparison.Ordinal))
                {
                    return true;
                }

                if (string.Equals(trimmed, "false", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, "0", System.StringComparison.Ordinal))
                {
                    return false;
                }

                return defaultValue;
            }

            public bool Equals(HotfixGeneratorOptions other)
            {
                return GenerateStableRpcServices == other.GenerateStableRpcServices;
            }

            public override bool Equals(object? obj)
            {
                return obj is HotfixGeneratorOptions other && Equals(other);
            }

            public override int GetHashCode()
            {
                return GenerateStableRpcServices.GetHashCode();
            }
        }

        private sealed class HotfixActorApiInfo
        {
            public HotfixActorApiInfo(
                INamedTypeSymbol behavior,
                INamedTypeSymbol actor,
                ITypeSymbol keyType,
                HotfixActorMethodInfo[] methods,
                HotfixGeneratorDiagnosticInfo[] diagnostics)
            {
                Behavior = behavior;
                Actor = actor;
                KeyType = keyType;
                Methods = methods;
                Diagnostics = diagnostics;
            }

            public INamedTypeSymbol Behavior { get; }

            public INamedTypeSymbol Actor { get; }

            public ITypeSymbol KeyType { get; }

            public HotfixActorMethodInfo[] Methods { get; }

            public HotfixGeneratorDiagnosticInfo[] Diagnostics { get; }

            public bool IsSupported => Diagnostics.Length == 0 && Methods.Length > 0;
        }

        private sealed class HotfixGeneratorDiagnosticInfo
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

        private sealed class HotfixActorMethodInfo
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
