using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lakona.Game.Server.Generators
{
    [Generator]
    public sealed class TypedActorGenerator : IIncrementalGenerator
    {
        private const string ActorIgnoreAttributeName = "Lakona.Game.Server.Actors.ActorIgnoreAttribute";
        private const string ActorDestroyAttributeName = "Lakona.Game.Server.Actors.ActorDestroyAttribute";
        private const string ActorLocalOnlyAttributeName = "Lakona.Game.Server.Actors.ActorLocalOnlyAttribute";
        private const string ActorMethodAttributeName = "Lakona.Game.Server.Actors.ActorMethodAttribute";
        private const string ActorNameAttributeName = "Lakona.Game.Server.Actors.ActorNameAttribute";
        private const string ActorSpawnAttributeName = "Lakona.Game.Server.Actors.ActorSpawnAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var actors = context.SyntaxProvider
                .CreateSyntaxProvider(
                    IsActorCandidate,
                    GetActor)
                .Where(IsNotNull);

            context.RegisterSourceOutput(actors, GenerateActor);
        }

        private static bool IsActorCandidate(SyntaxNode node, CancellationToken cancellationToken)
        {
            return node is ClassDeclarationSyntax;
        }

        private static ActorInfo? GetActor(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            var declaration = (ClassDeclarationSyntax)context.Node;
            if (context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol symbol)
            {
                return null;
            }

            var keyType = GetActorKeyType(symbol);
            if (keyType == null)
            {
                return null;
            }

            var candidateMethods = symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(IsPublicInstanceOrdinaryMethod)
                .Where(static method => !HasAttribute(method, ActorIgnoreAttributeName))
                .ToArray();
            var spawnHook = candidateMethods
                .Where(static method => HasAttribute(method, ActorSpawnAttributeName))
                .Where(static method => IsEligibleLifecycleMethod(method, allowRequest: true))
                .Select(static method => LifecycleMethodInfo.Create(method, true))
                .FirstOrDefault();
            var destroyHook = candidateMethods
                .Where(static method => HasAttribute(method, ActorDestroyAttributeName))
                .Where(static method => IsEligibleLifecycleMethod(method, allowRequest: false))
                .Select(static method => LifecycleMethodInfo.Create(method, false))
                .FirstOrDefault();
            var businessMethods = candidateMethods
                .Where(static method => !HasAttribute(method, ActorSpawnAttributeName))
                .Where(static method => !HasAttribute(method, ActorDestroyAttributeName))
                .ToArray();
            var methods = businessMethods
                .Where(IsEligibleMethod)
                .Select(method => MethodInfo.Create(method))
                .ToArray();
            var unsupportedMethods = businessMethods
                .Where(static method => !IsEligibleMethod(method))
                .Select(static method => new UnsupportedMethodInfo(
                    method.Name,
                    method.Locations.Length == 0 ? Location.None : method.Locations[0]))
                .Concat(candidateMethods
                    .Where(static method => HasAttribute(method, ActorSpawnAttributeName))
                    .Where(static method => !IsEligibleLifecycleMethod(method, allowRequest: true))
                    .Select(static method => new UnsupportedMethodInfo(
                        method.Name,
                        method.Locations.Length == 0 ? Location.None : method.Locations[0])))
                .Concat(candidateMethods
                    .Where(static method => HasAttribute(method, ActorDestroyAttributeName))
                    .Where(static method => !IsEligibleLifecycleMethod(method, allowRequest: false))
                    .Select(static method => new UnsupportedMethodInfo(
                        method.Name,
                        method.Locations.Length == 0 ? Location.None : method.Locations[0])))
                .ToArray();
            var actorName = GetAttributeString(symbol, ActorNameAttributeName) ?? LowerFirst(GetActorPrefix(symbol.Name));
            var isLocalOnly = HasAttribute(symbol, ActorLocalOnlyAttributeName);

            return new ActorInfo(symbol, keyType, actorName, isLocalOnly, methods, spawnHook, destroyHook, unsupportedMethods);
        }

        private static bool IsNotNull(ActorInfo? actor)
        {
            return actor != null;
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

        private static bool IsEligibleMethod(IMethodSymbol method)
        {
            if (!IsValueTask(method.ReturnType, out _))
            {
                return false;
            }

            if (method.Parameters.Length == 1)
            {
                return true;
            }

            return method.Parameters.Length == 2 &&
                IsCancellationToken(method.Parameters[1].Type);
        }

        private static bool IsEligibleLifecycleMethod(IMethodSymbol method, bool allowRequest)
        {
            if (!IsValueTask(method.ReturnType, out var resultType) || resultType != null)
            {
                return false;
            }

            if (method.Parameters.Length == 0)
            {
                return true;
            }

            if (method.Parameters.Length == 1)
            {
                return IsCancellationToken(method.Parameters[0].Type) || allowRequest;
            }

            return allowRequest &&
                method.Parameters.Length == 2 &&
                IsCancellationToken(method.Parameters[1].Type);
        }

        private static bool IsPublicInstanceOrdinaryMethod(IMethodSymbol method)
        {
            return method.DeclaredAccessibility == Accessibility.Public &&
                !method.IsStatic &&
                method.MethodKind == MethodKind.Ordinary;
        }

        private static bool HasAttribute(ISymbol symbol, string attributeName)
        {
            return symbol.GetAttributes().Any(attribute =>
                attribute.AttributeClass != null &&
                attribute.AttributeClass.ToDisplayString() == attributeName);
        }

        private static string? GetAttributeString(ISymbol symbol, string attributeName)
        {
            var attribute = symbol.GetAttributes().FirstOrDefault(candidate =>
                candidate.AttributeClass != null &&
                candidate.AttributeClass.ToDisplayString() == attributeName);
            return attribute?.ConstructorArguments.Length == 1
                ? attribute.ConstructorArguments[0].Value as string
                : null;
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

        private static void GenerateActor(SourceProductionContext context, ActorInfo? actor)
        {
            if (actor == null)
            {
                return;
            }

            foreach (var unsupportedMethod in actor.UnsupportedMethods)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    TypedActorGeneratorDiagnostics.UnsupportedMethodSignature,
                    unsupportedMethod.Location,
                    unsupportedMethod.Name));
            }

            var hintName = CreateHintName(actor.Symbol);
            context.AddSource(hintName, SourceText.From(GenerateActorSource(actor), Encoding.UTF8));
        }

        private static string GenerateActorSource(ActorInfo actor)
        {
            var namespaceName = actor.Symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : actor.Symbol.ContainingNamespace.ToDisplayString();
            var prefix = GetActorPrefix(actor.Symbol.Name);
            var keyType = DisplayType(actor.KeyType, actor.Symbol.ContainingNamespace);
            var actorsType = prefix + "Actors";
            var distributedRefType = prefix + "Ref";
            var localRefType = prefix + "LocalRef";
            var remoteRefType = prefix + "RemoteRef";

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine();

            if (namespaceName != null)
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine();
                builder.AppendLine("{");
            }

            var indentLevel = namespaceName != null ? 1 : 0;
            AppendActorsClass(builder, actor, actorsType, distributedRefType, localRefType, remoteRefType, keyType, indentLevel);
            if (!actor.IsLocalOnly)
            {
                builder.AppendLine();
                AppendDistributedRef(builder, actor, distributedRefType, keyType, actor.ActorName, indentLevel);
            }

            builder.AppendLine();
            AppendLocalRef(builder, actor, localRefType, keyType, actor.ActorName, indentLevel);
            if (!actor.IsLocalOnly)
            {
                builder.AppendLine();
                AppendRemoteRef(builder, actor, remoteRefType, keyType, actor.ActorName, indentLevel);
                builder.AppendLine();
                AppendClusterHandler(builder, actor, indentLevel);
            }

            builder.AppendLine();
            AppendServiceCollectionExtensions(builder, actor, actorsType, indentLevel);

            if (namespaceName != null)
            {
                builder.AppendLine("}");
            }

            return builder.ToString();
        }

        private static void AppendActorsClass(
            StringBuilder builder,
            ActorInfo actor,
            string actorsType,
            string distributedRefType,
            string localRefType,
            string remoteRefType,
            string keyType,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent).Append("public sealed class ").Append(actorsType).AppendLine();
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            if (!actor.IsLocalOnly)
            {
                builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IRemoteActorInvoker _remote;");
                builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IRemoteActorSerializer _serializer;");
                builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.RemoteActorOptions _options;");
                builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorDirectory _directory;");
                builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorDirectoryCache _directoryCache;");
                builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.LocalActorNodeIdentity _localNode;");
            }

            builder.AppendLine();
            builder.Append(indent).Append("    public ").Append(actorsType).AppendLine("(");
            builder.Append(indent).Append("        global::Lakona.Game.Server.Actors.IActorRuntime runtime");
            if (actor.IsLocalOnly)
            {
                builder.AppendLine(")");
            }
            else
            {
                builder.AppendLine(",");
                builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IRemoteActorInvoker remote,");
                builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IRemoteActorSerializer serializer,");
                builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.RemoteActorOptions options,");
                builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IActorDirectory directory,");
                builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IActorDirectoryCache directoryCache,");
                builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.LocalActorNodeIdentity localNode)");
            }

            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        _runtime = runtime;");
            if (!actor.IsLocalOnly)
            {
                builder.Append(indent).AppendLine("        _remote = remote;");
                builder.Append(indent).AppendLine("        _serializer = serializer;");
                builder.Append(indent).AppendLine("        _options = options;");
                builder.Append(indent).AppendLine("        _directory = directory;");
                builder.Append(indent).AppendLine("        _directoryCache = directoryCache;");
                builder.Append(indent).AppendLine("        _localNode = localNode;");
            }

            builder.Append(indent).AppendLine("    }");
            if (!actor.IsLocalOnly)
            {
                builder.AppendLine();
                builder.Append(indent).Append("    public ").Append(distributedRefType).Append(" Get(").Append(keyType).AppendLine(" id)");
                builder.Append(indent).AppendLine("    {");
                builder.Append(indent).Append("        return new ").Append(distributedRefType).AppendLine("(_runtime, _remote, _serializer, _options, _directory, _directoryCache, id);");
                builder.Append(indent).AppendLine("    }");
            }

            builder.AppendLine();
            builder.Append(indent).Append("    public ").Append(localRefType).Append(" Local(").Append(keyType).AppendLine(" id)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).Append("        return new ").Append(localRefType).AppendLine("(_runtime, id);");
            builder.Append(indent).AppendLine("    }");

            if (!actor.IsLocalOnly)
            {
                builder.AppendLine();
                builder.Append(indent).Append("    public ").Append(remoteRefType).Append(" Remote(global::Lakona.Game.Cluster.NodeId nodeId, ").Append(keyType).AppendLine(" id)");
                builder.Append(indent).AppendLine("    {");
                builder.Append(indent).Append("        return new ").Append(remoteRefType).AppendLine("(_remote, _serializer, _options, nodeId, id);");
                builder.Append(indent).AppendLine("    }");
            }

            builder.AppendLine();
            AppendSpawnMethod(builder, actor, keyType, actor.ActorName, indentLevel + 1);
            builder.AppendLine();
            AppendDestroyMethod(builder, actor, keyType, actor.ActorName, indentLevel + 1);

            builder.Append(indent).AppendLine("}");
        }

        private static void AppendSpawnMethod(
            StringBuilder builder,
            ActorInfo actor,
            string keyType,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var requestParameter = actor.SpawnHook != null && actor.SpawnHook.RequestType != null
                ? DisplayType(actor.SpawnHook.RequestType, actor.Symbol.ContainingNamespace) + " request, "
                : string.Empty;

            builder.Append(indent)
                .Append("public async global::System.Threading.Tasks.ValueTask SpawnAsync(")
                .Append(keyType)
                .Append(" id, ")
                .Append(requestParameter)
                .AppendLine("global::System.Threading.CancellationToken cancellationToken = default)");
            builder.Append(indent).AppendLine("{");
            AppendCollectionActorIdSetup(builder, actor, routePrefix, indentLevel + 1);
            builder.Append(indent).AppendLine("    if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        throw new global::Lakona.Game.Server.Actors.ActorAlreadyExistsException(");
            builder.Append(indent).AppendLine("            actorId,");
            builder.Append(indent).Append("            \"").Append(actor.ActorName).AppendLine("\",");
            builder.Append(indent).AppendLine("            \"spawn\",");
            builder.Append(indent).AppendLine("            \"Actor already exists locally.\");");
            builder.Append(indent).AppendLine("    }");
            builder.AppendLine();
            if (!actor.IsLocalOnly)
            {
                builder.Append(indent).AppendLine("    var registerStatus = await _directory.RegisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);");
                builder.Append(indent).AppendLine("    if (registerStatus == global::Lakona.Game.Server.Actors.ActorDirectoryRegisterStatus.Conflict)");
                builder.Append(indent).AppendLine("    {");
                builder.Append(indent).AppendLine("        throw new global::Lakona.Game.Server.Actors.ActorAlreadyExistsException(");
                builder.Append(indent).AppendLine("            actorId,");
                builder.Append(indent).Append("            \"").Append(actor.ActorName).AppendLine("\",");
                builder.Append(indent).AppendLine("            \"spawn\",");
                builder.Append(indent).AppendLine("            \"Actor is already registered on another node.\",");
                builder.Append(indent).AppendLine("            _localNode.NodeId);");
                builder.Append(indent).AppendLine("    }");
                builder.AppendLine();
            }

            builder.Append(indent).AppendLine("    try");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent)
                .Append("        await _runtime.GetOrCreateAsync<")
                .Append(actorType)
                .AppendLine(">(actorId, cancellationToken).ConfigureAwait(false);");
            if (actor.SpawnHook != null)
            {
                AppendLifecycleHookCall(builder, actor, actor.SpawnHook, indentLevel + 2);
            }

            if (!actor.IsLocalOnly)
            {
                builder.Append(indent).AppendLine("        _directoryCache.Set(actorId, _localNode.NodeId);");
            }

            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("    catch");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        await _runtime.StopAsync(actorId).ConfigureAwait(false);");
            if (!actor.IsLocalOnly)
            {
                builder.Append(indent).AppendLine("        await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);");
                builder.Append(indent).AppendLine("        _directoryCache.Remove(actorId);");
            }

            builder.Append(indent).AppendLine("        throw;");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendDestroyMethod(
            StringBuilder builder,
            ActorInfo actor,
            string keyType,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);

            builder.Append(indent)
                .Append("public async global::System.Threading.Tasks.ValueTask DestroyAsync(")
                .Append(keyType)
                .Append(" id");
            AppendLifecycleRequestParameter(builder, actor, actor.DestroyHook, includeRequest: false);
            builder.AppendLine(")");
            builder.Append(indent).AppendLine("{");
            AppendCollectionActorIdSetup(builder, actor, routePrefix, indentLevel + 1);
            builder.Append(indent).AppendLine("    if (_runtime.GetState(actorId) == global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        throw new global::Lakona.Game.Server.Actors.ActorNotFoundException(");
            builder.Append(indent).AppendLine("            actorId,");
            builder.Append(indent).Append("            \"").Append(actor.ActorName).AppendLine("\",");
            builder.Append(indent).AppendLine("            \"destroy\",");
            builder.Append(indent).AppendLine("            \"Actor was not found locally.\");");
            builder.Append(indent).AppendLine("    }");
            if (!actor.IsLocalOnly)
            {
                builder.AppendLine();
                builder.Append(indent).AppendLine("    var unregisterStatus = await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);");
                builder.Append(indent).AppendLine("    if (unregisterStatus == global::Lakona.Game.Server.Actors.ActorDirectoryUnregisterStatus.OwnershipMismatch)");
                builder.Append(indent).AppendLine("    {");
                builder.Append(indent).AppendLine("        throw new global::Lakona.Game.Server.Actors.ActorOwnershipMismatchException(");
                builder.Append(indent).AppendLine("            actorId,");
                builder.Append(indent).Append("            \"").Append(actor.ActorName).AppendLine("\",");
                builder.Append(indent).AppendLine("            \"destroy\",");
                builder.Append(indent).AppendLine("            \"Actor directory ownership belongs to another node.\",");
                builder.Append(indent).AppendLine("            _localNode.NodeId);");
                builder.Append(indent).AppendLine("    }");
                builder.AppendLine();
                builder.Append(indent).AppendLine("    _directoryCache.Remove(actorId);");
            }

            builder.AppendLine();
            builder.Append(indent).AppendLine("    try");
            builder.Append(indent).AppendLine("    {");
            if (actor.DestroyHook != null)
            {
                AppendLifecycleHookCall(builder, actor, actor.DestroyHook, indentLevel + 2);
            }

            builder.Append(indent).AppendLine("        await _runtime.StopAsync(actorId).ConfigureAwait(false);");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("    catch");
            builder.Append(indent).AppendLine("    {");
            if (!actor.IsLocalOnly)
            {
                builder.Append(indent).AppendLine("        await _directory.RegisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);");
                builder.Append(indent).AppendLine("        _directoryCache.Set(actorId, _localNode.NodeId);");
            }

            builder.Append(indent).AppendLine("        throw;");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendLifecycleRequestParameter(
            StringBuilder builder,
            ActorInfo actor,
            LifecycleMethodInfo? hook,
            bool includeRequest = true)
        {
            if (includeRequest && hook?.RequestType != null)
            {
                builder.Append(", ")
                    .Append(DisplayType(hook.RequestType, actor.Symbol.ContainingNamespace))
                    .Append(" request");
            }

            builder.Append(", global::System.Threading.CancellationToken cancellationToken = default");
        }

        private static void AppendDistributedRef(
            StringBuilder builder,
            ActorInfo actor,
            string distributedRefType,
            string keyType,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent).Append("public readonly struct ").Append(distributedRefType).AppendLine();
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IRemoteActorInvoker _remote;");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IRemoteActorSerializer _serializer;");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.RemoteActorOptions _options;");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorDirectory _directory;");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorDirectoryCache _directoryCache;");
            builder.Append(indent).Append("    private readonly ").Append(keyType).AppendLine(" _id;");
            builder.AppendLine();
            builder.Append(indent).Append("    public ").Append(distributedRefType).Append("(").AppendLine();
            builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IActorRuntime runtime,");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IRemoteActorInvoker remote,");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IRemoteActorSerializer serializer,");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.RemoteActorOptions options,");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IActorDirectory directory,");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IActorDirectoryCache directoryCache,");
            builder.Append(indent).Append("        ").Append(keyType).AppendLine(" id)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        _runtime = runtime;");
            builder.Append(indent).AppendLine("        _remote = remote;");
            builder.Append(indent).AppendLine("        _serializer = serializer;");
            builder.Append(indent).AppendLine("        _options = options;");
            builder.Append(indent).AppendLine("        _directory = directory;");
            builder.Append(indent).AppendLine("        _directoryCache = directoryCache;");
            builder.Append(indent).AppendLine("        _id = id;");
            builder.Append(indent).AppendLine("    }");

            foreach (var method in actor.Methods)
            {
                builder.AppendLine();
                AppendDistributedMethod(builder, actor, method, routePrefix, indentLevel + 1);
            }

            builder.AppendLine();
            AppendResolveNodeMethod(builder, routePrefix, indentLevel + 1);
            builder.AppendLine();
            AppendIsLocationFailureMethod(builder, indentLevel + 1);

            builder.Append(indent).AppendLine("}");
        }

        private static void AppendLifecycleHookCall(
            StringBuilder builder,
            ActorInfo actor,
            LifecycleMethodInfo hook,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            builder.Append(indent)
                .Append("await _runtime.TellAsync<")
                .Append(actorType)
                .Append(">(actorId, (actor, ct) => actor.")
                .Append(hook.Name)
                .Append('(');

            var appendedArgument = false;
            if (hook.RequestType != null)
            {
                builder.Append("request");
                appendedArgument = true;
            }

            if (hook.HasCancellationToken)
            {
                if (appendedArgument)
                {
                    builder.Append(", ");
                }

                builder.Append("ct");
            }

            builder.AppendLine("), cancellationToken).ConfigureAwait(false);");
        }

        private static void AppendDistributedMethod(
            StringBuilder builder,
            ActorInfo actor,
            MethodInfo method,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var returnType = DisplayReturnType(actor, method);
            var requestType = DisplayType(method.RequestType, actor.Symbol.ContainingNamespace);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var actorName = actor.ActorName;
            var methodName = method.ActorMethodName;

            builder.Append(indent)
                .Append("public async ")
                .Append(returnType)
                .Append(' ')
                .Append(method.Name)
                .Append('(')
                .Append(requestType)
                .Append(" request, global::System.Threading.CancellationToken cancellationToken = default)")
                .AppendLine();
            builder.Append(indent).AppendLine("{");
            AppendActorIdSetup(builder, actor, routePrefix, indentLevel + 1);
            builder.Append(indent).AppendLine("    if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.Append(indent).AppendLine("    {");

            if (method.ResultType == null)
            {
                builder.Append(indent)
                    .Append("        await _runtime.TellAsync<")
                    .Append(actorType)
                    .Append(">(actorId, (actor, ct) => actor.")
                    .Append(method.Name)
                    .Append("(request");
                if (method.HasCancellationToken)
                {
                    builder.Append(", ct");
                }

                builder.AppendLine("), cancellationToken).ConfigureAwait(false);");
                builder.Append(indent).AppendLine("        return;");
            }
            else
            {
                builder.Append(indent)
                    .Append("        return await _runtime.AskAsync<")
                    .Append(actorType)
                    .Append(", ")
                    .Append(DisplayType(method.ResultType, actor.Symbol.ContainingNamespace))
                    .Append(">(actorId, (actor, ct) => actor.")
                    .Append(method.Name)
                    .Append("(request");
                if (method.HasCancellationToken)
                {
                    builder.Append(", ct");
                }

                builder.AppendLine("), cancellationToken).ConfigureAwait(false);");
            }

            builder.Append(indent).AppendLine("    }");
            builder.AppendLine();
            builder.Append(indent)
                .Append("    var node = await ResolveNodeAsync(actorId, \"")
                .Append(methodName)
                .AppendLine("\", cancellationToken).ConfigureAwait(false);");
            AppendRemoteInvocationSetup(builder, actor, routePrefix, actorName, methodName, "node", indentLevel + 1, includeActorId: false);
            builder.Append(indent).AppendLine("    try");
            builder.Append(indent).AppendLine("    {");

            if (method.ResultType == null)
            {
                builder.Append(indent).AppendLine("        var result = await _remote.TellAsync(invocation, cancellationToken).ConfigureAwait(false);");
                builder.Append(indent)
                    .Append("        global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureAccepted(result, actorId, \"")
                    .Append(actorName)
                    .Append("\", \"")
                    .Append(methodName)
                    .AppendLine("\", node, correlationId);");
            }
            else
            {
                builder.Append(indent).AppendLine("        var result = await _remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
                builder.Append(indent)
                    .Append("        global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(result, actorId, \"")
                    .Append(actorName)
                    .Append("\", \"")
                    .Append(methodName)
                    .AppendLine("\", node, correlationId);");
                builder.Append(indent)
                    .Append("        return _serializer.Deserialize<")
                    .Append(DisplayType(method.ResultType, actor.Symbol.ContainingNamespace))
                    .AppendLine(">(result.Payload);");
            }

            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("    catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        _directoryCache.Remove(actorId);");
            builder.Append(indent).AppendLine("        throw;");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendResolveNodeMethod(
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
            builder.Append(indent).Append("                \"").Append(actorName).AppendLine("\",");
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

        private static void AppendIsLocationFailureMethod(
            StringBuilder builder,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent).AppendLine("private static bool IsLocationFailure(global::Lakona.Game.Server.Actors.ActorCallException exception)");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    return exception.Status == global::Lakona.Game.Server.Actors.ActorCallStatus.ActorNotFound");
            builder.Append(indent).AppendLine("        || exception.Status == global::Lakona.Game.Server.Actors.ActorCallStatus.NodeUnavailable;");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendLocalRef(
            StringBuilder builder,
            ActorInfo actor,
            string localRefType,
            string keyType,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent).Append("public readonly struct ").Append(localRefType).AppendLine();
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            builder.Append(indent).Append("    private readonly ").Append(keyType).AppendLine(" _id;");
            builder.AppendLine();
            builder.Append(indent).Append("    public ").Append(localRefType).Append("(global::Lakona.Game.Server.Actors.IActorRuntime runtime, ").Append(keyType).AppendLine(" id)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        _runtime = runtime;");
            builder.Append(indent).AppendLine("        _id = id;");
            builder.Append(indent).AppendLine("    }");

            foreach (var method in actor.Methods)
            {
                builder.AppendLine();
                AppendLocalMethod(builder, actor, method, routePrefix, indentLevel + 1);
            }

            builder.Append(indent).AppendLine("}");
        }

        private static void AppendLocalMethod(
            StringBuilder builder,
            ActorInfo actor,
            MethodInfo method,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var returnType = DisplayReturnType(actor, method);
            var requestType = DisplayType(method.RequestType, actor.Symbol.ContainingNamespace);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Append(indent)
                .Append("public ")
                .Append(returnType)
                .Append(' ')
                .Append(method.Name)
                .Append('(')
                .Append(requestType)
                .Append(" request, global::System.Threading.CancellationToken cancellationToken = default)")
                .AppendLine();
            builder.Append(indent).AppendLine("{");
            AppendActorIdSetup(builder, actor, routePrefix, indentLevel + 1);

            if (method.ResultType == null)
            {
                builder.Append(indent)
                    .Append("    return _runtime.TellAsync<")
                    .Append(actorType)
                    .Append(">(actorId, (actor, ct) => actor.")
                    .Append(method.Name)
                    .Append("(request");
                if (method.HasCancellationToken)
                {
                    builder.Append(", ct");
                }

                builder.AppendLine("), cancellationToken);");
            }
            else
            {
                builder.Append(indent)
                    .Append("    return _runtime.AskAsync<")
                    .Append(actorType)
                    .Append(", ")
                    .Append(DisplayType(method.ResultType, actor.Symbol.ContainingNamespace))
                    .Append(">(actorId, (actor, ct) => actor.")
                    .Append(method.Name)
                    .Append("(request");
                if (method.HasCancellationToken)
                {
                    builder.Append(", ct");
                }

                builder.AppendLine("), cancellationToken);");
            }

            builder.Append(indent).AppendLine("}");
        }

        private static void AppendRemoteRef(
            StringBuilder builder,
            ActorInfo actor,
            string remoteRefType,
            string keyType,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent).Append("public readonly struct ").Append(remoteRefType).AppendLine();
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IRemoteActorInvoker _remote;");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IRemoteActorSerializer _serializer;");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.RemoteActorOptions _options;");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Cluster.NodeId _node;");
            builder.Append(indent).Append("    private readonly ").Append(keyType).AppendLine(" _id;");
            builder.AppendLine();
            builder.Append(indent).Append("    public ").Append(remoteRefType).Append("(").AppendLine();
            builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IRemoteActorInvoker remote,");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IRemoteActorSerializer serializer,");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.RemoteActorOptions options,");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Cluster.NodeId nodeId,");
            builder.Append(indent).Append("        ").Append(keyType).AppendLine(" id)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        _remote = remote;");
            builder.Append(indent).AppendLine("        _serializer = serializer;");
            builder.Append(indent).AppendLine("        _options = options;");
            builder.Append(indent).AppendLine("        _node = nodeId;");
            builder.Append(indent).AppendLine("        _id = id;");
            builder.Append(indent).AppendLine("    }");

            foreach (var method in actor.Methods)
            {
                builder.AppendLine();
                AppendRemoteMethod(builder, actor, method, routePrefix, indentLevel + 1);
            }

            builder.Append(indent).AppendLine("}");
        }

        private static void AppendRemoteMethod(
            StringBuilder builder,
            ActorInfo actor,
            MethodInfo method,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var returnType = DisplayReturnType(actor, method);
            var requestType = DisplayType(method.RequestType, actor.Symbol.ContainingNamespace);
            var actorName = actor.ActorName;
            var methodName = method.ActorMethodName;

            builder.Append(indent)
                .Append("public async ")
                .Append(returnType)
                .Append(' ')
                .Append(method.Name)
                .Append('(')
                .Append(requestType)
                .Append(" request, global::System.Threading.CancellationToken cancellationToken = default)")
                .AppendLine();
            builder.Append(indent).AppendLine("{");
            AppendRemoteInvocationSetup(builder, actor, routePrefix, actorName, methodName, "_node", indentLevel + 1);

            if (method.ResultType == null)
            {
                builder.Append(indent).AppendLine("    var result = await _remote.TellAsync(invocation, cancellationToken).ConfigureAwait(false);");
                builder.Append(indent)
                    .Append("    global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureAccepted(result, actorId, \"")
                    .Append(actorName)
                    .Append("\", \"")
                    .Append(methodName)
                    .AppendLine("\", _node, correlationId);");
            }
            else
            {
                builder.Append(indent).AppendLine("    var result = await _remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
                builder.Append(indent)
                    .Append("    global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(result, actorId, \"")
                    .Append(actorName)
                    .Append("\", \"")
                    .Append(methodName)
                    .AppendLine("\", _node, correlationId);");
                builder.Append(indent)
                    .Append("    return _serializer.Deserialize<")
                    .Append(DisplayType(method.ResultType, actor.Symbol.ContainingNamespace))
                    .AppendLine(">(result.Payload);");
            }

            builder.Append(indent).AppendLine("}");
        }

        private static void AppendServiceCollectionExtensions(
            StringBuilder builder,
            ActorInfo actor,
            string actorsType,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var extensionType = actor.Symbol.Name + "ServiceCollectionExtensions";
            var methodName = "Add" + actorsType;

            builder.Append(indent).Append("public static class ").Append(extensionType).AppendLine();
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).Append("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection ").Append(methodName).AppendLine("(");
            builder.Append(indent).AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).Append("        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<").Append(actorsType).AppendLine(">(services);");
            if (!actor.IsLocalOnly)
            {
                builder.Append(indent).AppendLine("        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(");
                builder.Append(indent).AppendLine("            services,");
                builder.Append(indent).AppendLine("            global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<");
                builder.Append(indent).AppendLine("                global::Lakona.Game.Cluster.IClusterMessageHandler,");
                builder.Append(indent).Append("                ").Append(actor.Symbol.Name).AppendLine("ClusterHandler>());");
            }

            builder.Append(indent).AppendLine("        return services;");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendClusterHandler(
            StringBuilder builder,
            ActorInfo actor,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var handlerType = actor.Symbol.Name + "ClusterHandler";

            builder.Append(indent).Append("public sealed class ").Append(handlerType).AppendLine(" : global::Lakona.Game.Cluster.IClusterMessageHandler");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _runtime;");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Server.Actors.IRemoteActorSerializer _serializer;");
            builder.Append(indent).AppendLine("    private readonly global::Lakona.Game.Cluster.IClusterRouter _router;");
            builder.AppendLine();
            builder.Append(indent).Append("    public ").Append(handlerType).AppendLine("(");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IActorRuntime runtime,");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IRemoteActorSerializer serializer,");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Cluster.IClusterRouter router)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        _runtime = runtime;");
            builder.Append(indent).AppendLine("        _serializer = serializer;");
            builder.Append(indent).AppendLine("        _router = router;");
            builder.Append(indent).AppendLine("    }");
            builder.AppendLine();
            builder.Append(indent).AppendLine("    public async global::System.Threading.Tasks.ValueTask<global::Lakona.Game.Cluster.ClusterSendStatus> HandleAsync(");
            builder.Append(indent).AppendLine("        global::Lakona.Game.Cluster.ClusterMessage message,");
            builder.Append(indent).AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        if (!global::Lakona.Game.Cluster.ClusterActorEnvelope.TryFromClusterMessage(message, out var envelope) || envelope is null)");
            builder.Append(indent).AppendLine("        {");
            builder.Append(indent).AppendLine("            return global::Lakona.Game.Cluster.ClusterSendStatus.RouteNotFound;");
            builder.Append(indent).AppendLine("        }");
            builder.AppendLine();
            builder.Append(indent)
                .Append("        if (!envelope.ActorId.StartsWith(\"")
                .Append(actor.ActorName)
                .Append("/\", global::System.StringComparison.Ordinal))")
                .AppendLine();
            builder.Append(indent).AppendLine("        {");
            builder.Append(indent).AppendLine("            return global::Lakona.Game.Cluster.ClusterSendStatus.RouteNotFound;");
            builder.Append(indent).AppendLine("        }");
            builder.AppendLine();
            builder.Append(indent).AppendLine("        var actorId = global::Lakona.Game.Server.Actors.ActorId.From(envelope.ActorId);");
            builder.Append(indent).AppendLine("        switch (envelope.Kind)");
            builder.Append(indent).AppendLine("        {");

            foreach (var method in actor.Methods)
            {
                AppendClusterHandlerCase(builder, actor, method, indentLevel + 3);
            }

            builder.Append(indent).AppendLine("            default:");
            builder.Append(indent).AppendLine("                return global::Lakona.Game.Cluster.ClusterSendStatus.RouteNotFound;");
            builder.Append(indent).AppendLine("        }");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendClusterHandlerCase(
            StringBuilder builder,
            ActorInfo actor,
            MethodInfo method,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var requestType = DisplayType(method.RequestType, actor.Symbol.ContainingNamespace);

            builder.Append(indent).Append("case \"").Append(method.ActorMethodName).AppendLine("\":");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).Append("    var request = _serializer.Deserialize<").Append(requestType).AppendLine(">(envelope.Payload);");

            if (method.ResultType == null)
            {
                builder.Append(indent)
                    .Append("    await _runtime.TellAsync<")
                    .Append(actorType)
                    .Append(">(actorId, (actor, ct) => actor.")
                    .Append(method.Name)
                    .Append("(request");
                if (method.HasCancellationToken)
                {
                    builder.Append(", ct");
                }

                builder.AppendLine("), cancellationToken).ConfigureAwait(false);");
            }
            else
            {
                builder.Append(indent)
                    .Append("    var reply = await _runtime.AskAsync<")
                    .Append(actorType)
                    .Append(", ")
                    .Append(DisplayType(method.ResultType, actor.Symbol.ContainingNamespace))
                    .Append(">(actorId, (actor, ct) => actor.")
                    .Append(method.Name)
                    .Append("(request");
                if (method.HasCancellationToken)
                {
                    builder.Append(", ct");
                }

                builder.AppendLine("), cancellationToken).ConfigureAwait(false);");
                builder.Append(indent).AppendLine("    if (envelope.ReplyCorrelationId is not null)");
                builder.Append(indent).AppendLine("    {");
                builder.Append(indent).AppendLine("        await global::Lakona.Game.Server.Actors.RemoteActorGateway.SendReplyAsync(");
                builder.Append(indent).AppendLine("            _router,");
                builder.Append(indent).AppendLine("            envelope.SourceNode,");
                builder.Append(indent).AppendLine("            envelope.ReplyCorrelationId,");
                builder.Append(indent).AppendLine("            _serializer.Serialize(reply),");
                builder.Append(indent).AppendLine("            cancellationToken).ConfigureAwait(false);");
                builder.Append(indent).AppendLine("    }");
            }

            builder.Append(indent).AppendLine("    return global::Lakona.Game.Cluster.ClusterSendStatus.Accepted;");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendRemoteInvocationSetup(
            StringBuilder builder,
            ActorInfo actor,
            string routePrefix,
            string actorName,
            string methodName,
            string nodeExpression,
            int indentLevel,
            bool includeActorId = true)
        {
            var indent = Indent(indentLevel);
            if (includeActorId)
            {
                AppendActorIdSetup(builder, actor, routePrefix, indentLevel);
            }

            builder.Append(indent).AppendLine("var payload = _serializer.Serialize(request);");
            builder.Append(indent).AppendLine("var correlationId = global::System.Guid.NewGuid().ToString(\"N\");");
            builder.Append(indent).AppendLine("var deadline = global::System.DateTimeOffset.UtcNow.Add(_options.DefaultTimeout);");
            builder.Append(indent)
                .Append("var invocation = new global::Lakona.Game.Server.Actors.RemoteActorInvocation(")
                .Append(nodeExpression)
                .Append(", actorId, \"")
                .Append(actorName)
                .Append("\", \"")
                .Append(methodName)
                .AppendLine("\", payload, deadline, correlationId);");
        }

        private static void AppendActorIdSetup(
            StringBuilder builder,
            ActorInfo actor,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent)
                .Append("var actorId = global::Lakona.Game.Server.Actors.ActorId.From(\"")
                .Append(routePrefix)
                .Append("/\" + ")
                .Append(CreateKeyValueExpression(actor.KeyType))
                .AppendLine(");");
        }

        private static void AppendCollectionActorIdSetup(
            StringBuilder builder,
            ActorInfo actor,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            builder.Append(indent)
                .Append("var actorId = global::Lakona.Game.Server.Actors.ActorId.From(\"")
                .Append(routePrefix)
                .Append("/\" + ")
                .Append(CreateKeyValueExpression(actor.KeyType, "id"))
                .AppendLine(");");
        }

        private static string DisplayReturnType(ActorInfo actor, MethodInfo method)
        {
            if (method.ResultType == null)
            {
                return "global::System.Threading.Tasks.ValueTask";
            }

            return "global::System.Threading.Tasks.ValueTask<" + DisplayType(method.ResultType, actor.Symbol.ContainingNamespace) + ">";
        }

        private static string DisplayType(ITypeSymbol type, INamespaceSymbol actorNamespace)
        {
            if (SymbolEqualityComparer.Default.Equals(type.ContainingNamespace, actorNamespace))
            {
                return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            }

            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static string CreateKeyValueExpression(ITypeSymbol keyType)
        {
            return CreateKeyValueExpression(keyType, "_id");
        }

        private static string CreateKeyValueExpression(ITypeSymbol keyType, string idExpression)
        {
            if (keyType.SpecialType == SpecialType.System_String)
            {
                return idExpression;
            }

            if (HasAccessibleValueProperty(keyType))
            {
                return idExpression + ".Value";
            }

            return idExpression + ".ToString()";
        }

        private static bool HasAccessibleValueProperty(ITypeSymbol keyType)
        {
            return keyType.GetMembers("Value")
                .OfType<IPropertySymbol>()
                .Any(static property =>
                    !property.IsStatic &&
                    property.GetMethod != null &&
                    IsAccessiblePropertyGetter(property.GetMethod.DeclaredAccessibility));
        }

        private static bool IsAccessiblePropertyGetter(Accessibility accessibility)
        {
            return accessibility == Accessibility.Public ||
                accessibility == Accessibility.Internal ||
                accessibility == Accessibility.ProtectedOrInternal;
        }

        private static string GetRemoteMethodName(string methodName)
        {
            var normalized = methodName.EndsWith("Async", System.StringComparison.Ordinal) && methodName.Length > "Async".Length
                ? methodName.Substring(0, methodName.Length - "Async".Length)
                : methodName;

            return LowerFirst(normalized);
        }

        private static string GetActorPrefix(string actorName)
        {
            return actorName.EndsWith("Actor", System.StringComparison.Ordinal) && actorName.Length > "Actor".Length
                ? actorName.Substring(0, actorName.Length - "Actor".Length)
                : actorName;
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

        private static string CreateHintName(INamedTypeSymbol symbol)
        {
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty)
                .Replace('.', '_')
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace(',', '_')
                .Replace(' ', '_') + ".Actors.g.cs";
        }

        private sealed class ActorInfo
        {
            public ActorInfo(
                INamedTypeSymbol symbol,
                ITypeSymbol keyType,
                string actorName,
                bool isLocalOnly,
                MethodInfo[] methods,
                LifecycleMethodInfo? spawnHook,
                LifecycleMethodInfo? destroyHook,
                UnsupportedMethodInfo[] unsupportedMethods)
            {
                Symbol = symbol;
                KeyType = keyType;
                ActorName = actorName;
                IsLocalOnly = isLocalOnly;
                Methods = methods;
                SpawnHook = spawnHook;
                DestroyHook = destroyHook;
                UnsupportedMethods = unsupportedMethods;
            }

            public INamedTypeSymbol Symbol { get; }

            public ITypeSymbol KeyType { get; }

            public string ActorName { get; }

            public bool IsLocalOnly { get; }

            public MethodInfo[] Methods { get; }

            public LifecycleMethodInfo? SpawnHook { get; }

            public LifecycleMethodInfo? DestroyHook { get; }

            public UnsupportedMethodInfo[] UnsupportedMethods { get; }
        }

        private sealed class LifecycleMethodInfo
        {
            private LifecycleMethodInfo(
                string name,
                ITypeSymbol? requestType,
                bool hasCancellationToken)
            {
                Name = name;
                RequestType = requestType;
                HasCancellationToken = hasCancellationToken;
            }

            public string Name { get; }

            public ITypeSymbol? RequestType { get; }

            public bool HasCancellationToken { get; }

            public static LifecycleMethodInfo Create(IMethodSymbol method, bool allowRequest)
            {
                ITypeSymbol? requestType = null;
                var hasCancellationToken = false;
                if (method.Parameters.Length == 1)
                {
                    if (IsCancellationToken(method.Parameters[0].Type))
                    {
                        hasCancellationToken = true;
                    }
                    else if (allowRequest)
                    {
                        requestType = method.Parameters[0].Type;
                    }
                }
                else if (method.Parameters.Length == 2)
                {
                    requestType = method.Parameters[0].Type;
                    hasCancellationToken = true;
                }

                return new LifecycleMethodInfo(method.Name, requestType, hasCancellationToken);
            }
        }

        private sealed class MethodInfo
        {
            private MethodInfo(
                string name,
                string actorMethodName,
                ITypeSymbol requestType,
                ITypeSymbol? resultType,
                bool hasCancellationToken)
            {
                Name = name;
                ActorMethodName = actorMethodName;
                RequestType = requestType;
                ResultType = resultType;
                HasCancellationToken = hasCancellationToken;
            }

            public string Name { get; }

            public string ActorMethodName { get; }

            public ITypeSymbol RequestType { get; }

            public ITypeSymbol? ResultType { get; }

            public bool HasCancellationToken { get; }

            public static MethodInfo Create(IMethodSymbol method)
            {
                IsValueTask(method.ReturnType, out var resultType);
                return new MethodInfo(
                    method.Name,
                    GetAttributeString(method, ActorMethodAttributeName) ?? GetRemoteMethodName(method.Name),
                    method.Parameters[0].Type,
                    resultType,
                    method.Parameters.Length == 2);
            }
        }

        private sealed class UnsupportedMethodInfo
        {
            public UnsupportedMethodInfo(string name, Location location)
            {
                Name = name;
                Location = location;
            }

            public string Name { get; }

            public Location Location { get; }
        }
    }
}
