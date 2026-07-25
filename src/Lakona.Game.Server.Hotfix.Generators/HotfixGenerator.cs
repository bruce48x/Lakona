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
    public sealed partial class HotfixGenerator : IIncrementalGenerator
    {
        private const string HotfixStateAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixStateAttribute";
        private const string HotfixBehaviorOfAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixBehaviorOfAttribute";
        private const string HotfixTimerAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixTimerAttribute";
        private const string HotfixComponentAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixComponentAttribute";
        private const string ActorStartAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.ActorStartAttribute";
        private const string ActorStopAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.ActorStopAttribute";
        private const string RpcServiceAttributeName = "Lakona.Rpc.Core.RpcServiceAttribute";
        private const string RpcMethodAttributeName = "Lakona.Rpc.Core.RpcMethodAttribute";
        private const string RpcNotificationAttributeName = "Lakona.Rpc.Core.RpcNotificationAttribute";
        private const string ActorHostBuilderName = "Lakona.Game.Server.Hotfix.Abstractions.ActorHostBuilder";
        private const string DefaultGeneratedServerNamespace = "Server.App.Generated";
        private const string StableRpcServicesKey = "build_property.LakonaHotfixGenerateStableRpcServices";
        private const string HotfixProjectKey = "build_property.LakonaHotfixProject";

        private static readonly DiagnosticDescriptor UnsupportedHotfixBehaviorWrapperTarget = new DiagnosticDescriptor(
            "LKNHOTFIX021",
            "Hotfix behavior cannot receive generated actor ref wrappers",
            "Hotfix behavior '{0}' cannot receive generated actor entries because generation requires a non-file-local, non-generic, top-level sealed partial class",
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

            var httpServices = context.CompilationProvider.Combine(options)
                .Select(static (input, cancellationToken) =>
                {
                    var (compilation, generatorOptions) = input;
                    return generatorOptions.IsHotfixProject
                        ? DiscoverHttpServices(compilation, cancellationToken)
                        : [];
                });

            context.RegisterSourceOutput(httpServices, ValidateHttpServices);

            var clientNotifications = context.CompilationProvider
                .Select(static (compilation, cancellationToken) =>
                {
                    if (compilation.GetTypeByMetadataName("Lakona.Game.Server.Sessions.ClientNotificationTarget`1") is null ||
                        compilation.GetTypeByMetadataName("Lakona.Game.Server.Sessions.GeneratedClientNotificationExtensions") is not null)
                    {
                        return [];
                    }

                    return DiscoverRpcServiceContracts(compilation, cancellationToken)
                        .Select(static contract => new HotfixRpcServiceInfo(
                            contract,
                            DefaultGeneratedServerNamespace,
                            DefaultGeneratedServerNamespace))
                        .Where(static service => GetNotificationContract(service.Contract) is not null)
                        .ToArray();
                });

            context.RegisterSourceOutput(clientNotifications, GenerateClientNotificationExtensions);

            var actorContracts = context.CompilationProvider.Combine(options)
                .Select(static (input, cancellationToken) =>
                {
                    var (compilation, _) = input;
                    return new HotfixActorGenerationInput(
                        compilation.Assembly.Identity.Name,
                        DiscoverHotfixBehaviors(compilation, cancellationToken).ToArray(),
                        DiscoverStartupRegistrations(compilation, cancellationToken).ToArray());
                });

            context.RegisterSourceOutput(actorContracts, GenerateActorContracts);

            var timerModules = context.CompilationProvider
                .Select(static (compilation, cancellationToken) =>
                    DiscoverHotfixTimers(compilation, cancellationToken).ToArray());
            context.RegisterSourceOutput(timerModules, GenerateTimerEntries);

            var components = context.CompilationProvider
                .Select(static (compilation, cancellationToken) =>
                    DiscoverHotfixComponents(compilation, cancellationToken).ToArray());
            context.RegisterSourceOutput(components, GenerateComponentRegistration);
        }

        private static IEnumerable<INamedTypeSymbol> DiscoverHotfixComponents(
            Compilation compilation,
            CancellationToken cancellationToken)
        {
            foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (HasAttribute(type, HotfixComponentAttributeName))
                {
                    yield return type;
                }
            }
        }

        private static void GenerateComponentRegistration(
            SourceProductionContext context,
            INamedTypeSymbol[] components)
        {
            if (components.Length == 0)
            {
                return;
            }

            var supported = new List<INamedTypeSymbol>();
            foreach (var component in components)
            {
                var declaration = component.DeclaringSyntaxReferences
                    .Select(static reference => reference.GetSyntax())
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault();
                if (component.TypeKind != TypeKind.Class ||
                    component.IsStatic ||
                    component.IsAbstract ||
                    !component.IsSealed ||
                    component.TypeParameters.Length != 0 ||
                    component.ContainingType is not null ||
                    declaration is null ||
                    HasFileModifier(declaration))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.HotfixComponentModuleShape,
                        component.Locations.FirstOrDefault(static location => location.IsInSource),
                        component.ToDisplayString()));
                    continue;
                }

                supported.Add(component);
            }

            if (supported.Count == 0)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("namespace Lakona.Game.Server.Hotfix.Generated;");
            builder.AppendLine();
            builder.AppendLine("internal sealed class GeneratedHotfixComponentRegistration :");
            builder.AppendLine("    global::Lakona.Game.Server.Hotfix.IHotfixGeneratedServiceRegistration");
            builder.AppendLine("{");
            builder.AppendLine("    public void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            builder.AppendLine("    {");
            foreach (var component in supported.OrderBy(
                static item => item.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                System.StringComparer.Ordinal))
            {
                builder.Append("        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<")
                    .Append(component.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine(">(services);");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
            context.AddSource(
                "GeneratedHotfixComponents.g.cs",
                SourceText.From(builder.ToString(), Encoding.UTF8));
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

        private static void GenerateClientNotificationExtensions(
            SourceProductionContext context,
            HotfixRpcServiceInfo[] services)
        {
            if (services.Length == 0)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("namespace Lakona.Game.Server.Sessions;");
            builder.AppendLine();
            builder.AppendLine("public static class GeneratedClientNotificationExtensions");
            builder.AppendLine("{");

            foreach (var service in services.OrderBy(static item => item.Contract.ToDisplayString(), System.StringComparer.Ordinal))
            {
                var serviceAttribute = service.Contract.GetAttributes()
                    .FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
                var callback = GetNotificationContract(service.Contract);
                if (serviceAttribute?.ConstructorArguments.Length != 1 ||
                    serviceAttribute.ConstructorArguments[0].Value is not int serviceId ||
                    callback is null)
                {
                    continue;
                }

                var callbackDisplay = callback.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                foreach (var method in callback.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(static method => method.MethodKind == MethodKind.Ordinary)
                    .OrderBy(static method => method.Name, System.StringComparer.Ordinal))
                {
                    var notificationAttribute = method.GetAttributes()
                        .FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == RpcNotificationAttributeName);
                    if (notificationAttribute?.ConstructorArguments.Length != 1 ||
                        notificationAttribute.ConstructorArguments[0].Value is not int methodId ||
                        method.Parameters.Length is < 1 or > 2)
                    {
                        continue;
                    }

                    var payload = method.Parameters[0];
                    builder.AppendLine();
                    builder.Append("    public static global::Lakona.Game.Server.Sessions.ClientNotificationStatus ")
                        .Append(method.Name)
                        .Append("(this global::Lakona.Game.Server.Sessions.ClientNotificationTarget<")
                        .Append(callbackDisplay)
                        .Append("> target, ")
                        .Append(payload.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .Append(' ')
                        .Append(payload.Name)
                        .AppendLine(")");
                    builder.AppendLine("    {");
                    builder.Append("        return target.EnqueueGenerated(")
                        .Append(serviceId)
                        .Append(", ")
                        .Append(methodId)
                        .Append(", \"")
                        .Append(method.Name)
                        .Append("\", ")
                        .Append(payload.Name)
                        .AppendLine(");");
                    builder.AppendLine("    }");
                }
            }

            builder.AppendLine("}");
            context.AddSource(
                "GeneratedClientNotificationExtensions.g.cs",
                SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private static INamedTypeSymbol? GetNotificationContract(INamedTypeSymbol serviceContract)
        {
            var serviceAttribute = serviceContract.GetAttributes()
                .FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            return GetNamedTypeArgument(serviceAttribute, "NotificationContract");
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

        private static bool HasFileModifier(TypeDeclarationSyntax declaration)
        {
            return declaration.Modifiers.Any(static modifier =>
                string.Equals(modifier.ValueText, "file", System.StringComparison.Ordinal));
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

            AppendActorAccess(builder, contracts);
            builder.AppendLine();
            AppendActorSelectorExtensions(builder, contracts);
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

        private static string GenerateRpcServiceExtensionSource(HotfixRpcServiceInfo[] services)
        {
            var firstService = services[0];
            var namespaceName = firstService.GeneratedServerNamespace;
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
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
            var rpcServiceAttribute = service.Contract.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            var callbackType = GetNamedTypeArgument(rpcServiceAttribute, "NotificationContract");
            if (callbackType != null)
            {
                var callbackDisplay = callbackType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var callbackProxyName = service.GeneratedProxyNamespace + "." + GetNotificationProxyTypeName(callbackType.Name);
                builder.AppendLine();
                builder.AppendLine("    public override bool TryCreateCallback(");
                builder.AppendLine("        global::System.Type callbackContractType,");
                builder.AppendLine("        global::Lakona.Rpc.Server.RpcNotificationChannel notifications,");
                builder.AppendLine("        out object? callback)");
                builder.AppendLine("    {");
                builder.Append("        if (callbackContractType == typeof(").Append(callbackDisplay).AppendLine("))");
                builder.AppendLine("        {");
                builder.Append("            callback = new global::").Append(callbackProxyName).AppendLine("(notifications);");
                builder.AppendLine("            return true;");
                builder.AppendLine("        }");
                builder.AppendLine("        callback = null;");
                builder.AppendLine("        return false;");
                builder.AppendLine("    }");
            }
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
            builder.AppendLine(callbackType != null
                ? "            (connection, callback) => new " + proxyType + "("
                : "            connection => new " + proxyType + "(");
            builder.AppendLine("                global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Hotfix.IHotfixRuntimeAccessor>(services),");
            builder.AppendLine("                global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Actors.IActorRuntime>(services),");
            builder.AppendLine("                global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.ILakonaGameServer>(services),");
            if (callbackType != null)
            {
                builder.AppendLine("                callback,");
            }

            builder.AppendLine("                connection.ConnectionId));");
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

                foreach (var duplicate in methods.GroupBy(static method => method.Name).Where(static group => group.Count() > 1))
                {
                    foreach (var method in duplicate)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            HotfixGeneratorDiagnostics.HotfixTimerMethodShape,
                            method.Location,
                            method.Name));
                    }
                }

                if (methods.Count == 0 || methods.GroupBy(static method => method.Name).Any(static group => group.Count() > 1))
                {
                    continue;
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
                 method.Name == "DisposeAsync" && method.ReturnType.ToDisplayString() == "System.Threading.Tasks.ValueTask");
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

        private static bool HasAttribute(ISymbol symbol, string metadataName)
        {
            return symbol.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == metadataName);
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
            var serviceTypeName = GetServiceTypeName(service.Contract.Name);
            var proxyName = serviceTypeName + "Proxy";
            var callTypeName = serviceTypeName + "Call";
            var callTypeDisplay = "global::" + namespaceName + "." + callTypeName;
            var rpcServiceAttribute = service.Contract.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            var callbackType = GetNamedTypeArgument(rpcServiceAttribute, "NotificationContract");
            var callbackDisplay = callbackType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
            builder.Append("namespace ").Append(namespaceName).AppendLine(";");
            builder.AppendLine();

            AppendRpcServiceCall(builder, callTypeName, callbackDisplay);

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
                AppendRpcProxyMethod(builder, contractDisplay, method, callTypeDisplay, callbackDisplay);
            }

            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendRpcServiceCall(StringBuilder builder, string callTypeName, string? callbackDisplay)
        {
            var innerCallType = callbackDisplay == null
                ? "global::Lakona.Game.Server.Hotfix.HotfixServiceCall<TRequest>"
                : "global::Lakona.Game.Server.Hotfix.HotfixServiceCall<TRequest, " + callbackDisplay + ">";

            builder.Append("public readonly struct ").Append(callTypeName)
                .AppendLine("<TRequest> : global::Lakona.Game.Server.Hotfix.IHotfixServiceCall<TRequest>");
            builder.AppendLine("{");
            builder.Append("    private readonly ").Append(innerCallType).AppendLine(" _inner;");
            builder.AppendLine();
            builder.Append("    public ").Append(callTypeName).AppendLine("(");
            builder.AppendLine("        TRequest request,");
            builder.AppendLine("        string connectionId,");
            if (callbackDisplay != null)
            {
                builder.Append("        ").Append(callbackDisplay).AppendLine(" callback,");
            }

            builder.AppendLine("        global::Lakona.Game.Server.Sessions.GameSessionKey? currentSession,");
            builder.AppendLine("        global::Lakona.Game.Server.Sessions.GameSessionItems currentSessionItems,");
            builder.AppendLine("        global::System.IServiceProvider services,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorRuntime actors,");
            builder.AppendLine("        global::Lakona.Game.Server.ILakonaGameServer gameServer)");
            builder.AppendLine("    {");
            builder.Append("        _inner = new ").Append(innerCallType).AppendLine("(");
            builder.AppendLine("            request,");
            builder.AppendLine("            connectionId,");
            if (callbackDisplay != null)
            {
                builder.AppendLine("            callback,");
            }

            builder.AppendLine("            currentSession,");
            builder.AppendLine("            currentSessionItems,");
            builder.AppendLine("            services,");
            builder.AppendLine("            actors,");
            builder.AppendLine("            gameServer);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public TRequest Request => _inner.Request;");
            builder.AppendLine();
            builder.AppendLine("    public string ConnectionId => _inner.ConnectionId;");
            if (callbackDisplay != null)
            {
                builder.AppendLine();
                builder.Append("    public ").Append(callbackDisplay).AppendLine(" Callback => _inner.Callback;");
            }

            builder.AppendLine();
            builder.AppendLine("    public global::Lakona.Game.Server.Sessions.GameSessionKey? CurrentSession => _inner.CurrentSession;");
            builder.AppendLine();
            builder.AppendLine("    public global::Lakona.Game.Server.Sessions.GameSessionItems CurrentSessionItems => _inner.CurrentSessionItems;");
            builder.AppendLine();
            builder.AppendLine("    public global::System.IServiceProvider Services => _inner.Services;");
            builder.AppendLine();
            builder.AppendLine("    public global::Lakona.Game.Server.Actors.IActorRuntime Actors => _inner.Actors;");
            builder.AppendLine();
            builder.AppendLine("    public global::Lakona.Game.Server.ILakonaGameServer GameServer => _inner.GameServer;");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendRpcProxyMethod(
            StringBuilder builder,
            string contractDisplay,
            IMethodSymbol method,
            string callTypeDisplay,
            string? callbackDisplay)
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
            var callType = callTypeDisplay + "<" + requestDisplay + ">";

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

        private static string CreateTimerHintName(INamedTypeSymbol symbol)
        {
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty)
                .Replace('.', '_')
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace(',', '_')
                .Replace(' ', '_') + ".HotfixTimer.g.cs";
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

        private static ulong GetRemoteMethodId(HotfixActorMethodInfo method)
        {
            return CreateMethodId(method.MethodKey);
        }

        private static string GetActorPrefix(string actorName)
        {
            return actorName.EndsWith("Actor", System.StringComparison.Ordinal) && actorName.Length > "Actor".Length
                ? actorName.Substring(0, actorName.Length - "Actor".Length)
                : actorName;
        }

        private static string ResolveActorName(INamedTypeSymbol actor)
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

        private static string CreateActorApiMethodKeyConstantName(HotfixActorMethodInfo method)
        {
            var builder = new StringBuilder();
            builder.Append(SanitizeIdentifierPart(method.Name));
            builder.Append('_');
            builder.Append(SanitizeIdentifierPart(GetRuntimeTypeFullName(method.RequestType)));
            builder.Append("_MethodKey");
            return builder.ToString();
        }

        private static string CreateActorApiMethodIdConstantName(HotfixActorMethodInfo method)
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

        private sealed class HotfixActorGenerationInput
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

        private sealed class StartupRegistrationInfo
        {
            public StartupRegistrationInfo(INamedTypeSymbol actor, ITypeSymbol keyType)
            {
                Actor = actor;
                KeyType = keyType;
            }

            public INamedTypeSymbol Actor { get; }

            public ITypeSymbol KeyType { get; }
        }

        private readonly struct HotfixGeneratorOptions : System.IEquatable<HotfixGeneratorOptions>
        {
            public HotfixGeneratorOptions(
                bool generateStableRpcServices,
                bool isHotfixProject)
            {
                GenerateStableRpcServices = generateStableRpcServices;
                IsHotfixProject = isHotfixProject;
            }

            public bool GenerateStableRpcServices { get; }

            public bool IsHotfixProject { get; }

            public static HotfixGeneratorOptions From(AnalyzerConfigOptions options)
            {
                return new HotfixGeneratorOptions(
                    IsEnabled(options, StableRpcServicesKey, defaultValue: true),
                    IsEnabled(options, HotfixProjectKey, defaultValue: false));
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
                return GenerateStableRpcServices == other.GenerateStableRpcServices
                    && IsHotfixProject == other.IsHotfixProject;
            }

            public override bool Equals(object? obj)
            {
                return obj is HotfixGeneratorOptions other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (GenerateStableRpcServices, IsHotfixProject).GetHashCode();
            }
        }

        private sealed class HotfixActorApiInfo
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
