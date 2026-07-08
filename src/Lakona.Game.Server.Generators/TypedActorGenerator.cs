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
        private const string ActorLocalOnlyAttributeName = "Lakona.Game.Server.Actors.ActorLocalOnlyAttribute";
        private const string ActorMethodAttributeName = "Lakona.Game.Server.Actors.ActorMethodAttribute";
        private const string ActorNameAttributeName = "Lakona.Game.Server.Actors.ActorNameAttribute";

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
            var methods = candidateMethods
                .Where(IsEligibleMethod)
                .Select(method => MethodInfo.Create(method))
                .ToArray();
            var unsupportedMethods = candidateMethods
                .Where(static method => !IsEligibleMethod(method))
                .Select(static method => new UnsupportedMethodInfo(
                    method.Name,
                    method.Locations.Length == 0 ? Location.None : method.Locations[0]))
                .ToArray();
            var actorName = GetAttributeString(symbol, ActorNameAttributeName) ?? LowerFirst(GetActorPrefix(symbol.Name));
            var isLocalOnly = HasAttribute(symbol, ActorLocalOnlyAttributeName);

            return new ActorInfo(symbol, keyType, actorName, isLocalOnly, methods, unsupportedMethods);
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
            var routeRefType = prefix + "RouteRef";
            var localRefType = prefix + "LocalRef";
            var callDelegateType = actor.Symbol.Name + "Call";
            var callNoCancellationDelegateType = actor.Symbol.Name + "CallNoCancellation";
            var postDelegateType = actor.Symbol.Name + "Post";
            var postNoCancellationDelegateType = actor.Symbol.Name + "PostNoCancellation";

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine();

            if (namespaceName != null)
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine();
                builder.AppendLine("{");
            }

            var indentLevel = namespaceName != null ? 1 : 0;
            AppendActorsClass(builder, actor, actorsType, routeRefType, localRefType, keyType, indentLevel);
            builder.AppendLine();
            AppendActorDelegates(
                builder,
                actor,
                callDelegateType,
                callNoCancellationDelegateType,
                postDelegateType,
                postNoCancellationDelegateType,
                indentLevel);

            builder.AppendLine();
            AppendLocalRef(
                builder,
                actor,
                localRefType,
                keyType,
                actor.ActorName,
                callDelegateType,
                callNoCancellationDelegateType,
                postDelegateType,
                postNoCancellationDelegateType,
                indentLevel);
            if (!actor.IsLocalOnly)
            {
                builder.AppendLine();
                AppendDistributedRef(
                    builder,
                    actor,
                    routeRefType,
                    keyType,
                    actor.ActorName,
                    callDelegateType,
                    callNoCancellationDelegateType,
                    postDelegateType,
                    postNoCancellationDelegateType,
                    indentLevel);
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
            string routeRefType,
            string localRefType,
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
                builder.Append(indent).AppendLine("        global::Lakona.Game.Server.Actors.IActorDirectoryCache directoryCache)");
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
            }

            builder.Append(indent).AppendLine("    }");
            if (!actor.IsLocalOnly)
            {
                builder.AppendLine();
                builder.Append(indent).Append("    public ").Append(routeRefType).Append(" Route(").Append(keyType).AppendLine(" id)");
                builder.Append(indent).AppendLine("    {");
                builder.Append(indent).Append("        return new ").Append(routeRefType).AppendLine("(_runtime, _remote, _serializer, _options, _directory, _directoryCache, id);");
                builder.Append(indent).AppendLine("    }");
            }

            builder.AppendLine();
            builder.Append(indent).Append("    public ").Append(localRefType).Append(" Local(").Append(keyType).AppendLine(" id)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).Append("        return new ").Append(localRefType).AppendLine("(_runtime, id);");
            builder.Append(indent).AppendLine("    }");

            builder.Append(indent).AppendLine("}");
        }

        private static void AppendActorDelegates(
            StringBuilder builder,
            ActorInfo actor,
            string callDelegateType,
            string callNoCancellationDelegateType,
            string postDelegateType,
            string postNoCancellationDelegateType,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Append(indent).Append("public delegate global::System.Threading.Tasks.ValueTask<TResult> ").Append(callDelegateType).AppendLine("<in TRequest, TResult>(");
            builder.Append(indent).Append("    ").Append(actorType).AppendLine(" self,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken);");
            builder.AppendLine();
            builder.Append(indent).Append("public delegate global::System.Threading.Tasks.ValueTask<TResult> ").Append(callNoCancellationDelegateType).AppendLine("<in TRequest, TResult>(");
            builder.Append(indent).Append("    ").Append(actorType).AppendLine(" self,");
            builder.Append(indent).AppendLine("    TRequest request);");
            builder.AppendLine();
            builder.Append(indent).Append("public delegate global::System.Threading.Tasks.ValueTask ").Append(postDelegateType).AppendLine("<in TRequest>(");
            builder.Append(indent).Append("    ").Append(actorType).AppendLine(" self,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken);");
            builder.AppendLine();
            builder.Append(indent).Append("public delegate global::System.Threading.Tasks.ValueTask ").Append(postNoCancellationDelegateType).AppendLine("<in TRequest>(");
            builder.Append(indent).Append("    ").Append(actorType).AppendLine(" self,");
            builder.Append(indent).AppendLine("    TRequest request);");
        }

        private static void AppendActorCallApi(
            StringBuilder builder,
            ActorInfo actor,
            string callDelegateType,
            string callNoCancellationDelegateType,
            string postDelegateType,
            string postNoCancellationDelegateType,
            int indentLevel,
            bool includePost,
            bool resolveMethodName)
        {
            var indent = Indent(indentLevel);

            builder.Append(indent).AppendLine("public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(");
            builder.Append(indent).Append("    ").Append(callDelegateType).AppendLine("<TRequest, TResult> method,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
            builder.Append(indent).AppendLine("{");
            if (resolveMethodName)
            {
                builder.Append(indent).AppendLine("    var methodName = __lakona_ResolveActorMethod(method, typeof(TRequest), typeof(TResult));");
                builder.Append(indent).AppendLine("    return __lakona_CallAsync<TRequest, TResult>((actor, argument, ct) => method(actor, argument, ct), methodName, request, cancellationToken);");
            }
            else
            {
                builder.Append(indent).AppendLine("    return __lakona_CallAsync<TRequest, TResult>((actor, argument, ct) => method(actor, argument, ct), request, cancellationToken);");
            }

            builder.Append(indent).AppendLine("}");
            builder.AppendLine();
            builder.Append(indent).AppendLine("public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(");
            builder.Append(indent).Append("    ").Append(callNoCancellationDelegateType).AppendLine("<TRequest, TResult> method,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
            builder.Append(indent).AppendLine("{");
            if (resolveMethodName)
            {
                builder.Append(indent).AppendLine("    var methodName = __lakona_ResolveActorMethod(method, typeof(TRequest), typeof(TResult));");
                builder.Append(indent).AppendLine("    return __lakona_CallAsync<TRequest, TResult>((actor, argument, _) => method(actor, argument), methodName, request, cancellationToken);");
            }
            else
            {
                builder.Append(indent).AppendLine("    return __lakona_CallAsync<TRequest, TResult>((actor, argument, _) => method(actor, argument), request, cancellationToken);");
            }

            builder.Append(indent).AppendLine("}");
            builder.AppendLine();
            builder.Append(indent).AppendLine("public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(");
            builder.Append(indent).Append("    ").Append(postDelegateType).AppendLine("<TRequest> method,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
            builder.Append(indent).AppendLine("{");
            if (resolveMethodName)
            {
                builder.Append(indent).AppendLine("    var methodName = __lakona_ResolveActorMethod(method, typeof(TRequest), resultType: null);");
                builder.Append(indent).AppendLine("    return __lakona_CallAsync<TRequest>((actor, argument, ct) => method(actor, argument, ct), methodName, request, cancellationToken);");
            }
            else
            {
                builder.Append(indent).AppendLine("    return __lakona_CallAsync<TRequest>((actor, argument, ct) => method(actor, argument, ct), request, cancellationToken);");
            }

            builder.Append(indent).AppendLine("}");
            builder.AppendLine();
            builder.Append(indent).AppendLine("public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(");
            builder.Append(indent).Append("    ").Append(postNoCancellationDelegateType).AppendLine("<TRequest> method,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
            builder.Append(indent).AppendLine("{");
            if (resolveMethodName)
            {
                builder.Append(indent).AppendLine("    var methodName = __lakona_ResolveActorMethod(method, typeof(TRequest), resultType: null);");
                builder.Append(indent).AppendLine("    return __lakona_CallAsync<TRequest>((actor, argument, _) => method(actor, argument), methodName, request, cancellationToken);");
            }
            else
            {
                builder.Append(indent).AppendLine("    return __lakona_CallAsync<TRequest>((actor, argument, _) => method(actor, argument), request, cancellationToken);");
            }

            builder.Append(indent).AppendLine("}");

            if (!includePost)
            {
                return;
            }

            builder.AppendLine();
            builder.Append(indent).AppendLine("public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(");
            builder.Append(indent).Append("    ").Append(postDelegateType).AppendLine("<TRequest> method,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
            builder.Append(indent).AppendLine("{");
            if (resolveMethodName)
            {
                builder.Append(indent).AppendLine("    var methodName = __lakona_ResolveActorMethod(method, typeof(TRequest), resultType: null);");
                builder.Append(indent).AppendLine("    return __lakona_PostAsync<TRequest>((actor, argument, ct) => method(actor, argument, ct), methodName, request, cancellationToken);");
            }
            else
            {
                builder.Append(indent).AppendLine("    return __lakona_PostAsync<TRequest>((actor, argument, ct) => method(actor, argument, ct), request, cancellationToken);");
            }

            builder.Append(indent).AppendLine("}");
            builder.AppendLine();
            builder.Append(indent).AppendLine("public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(");
            builder.Append(indent).Append("    ").Append(postNoCancellationDelegateType).AppendLine("<TRequest> method,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
            builder.Append(indent).AppendLine("{");
            if (resolveMethodName)
            {
                builder.Append(indent).AppendLine("    var methodName = __lakona_ResolveActorMethod(method, typeof(TRequest), resultType: null);");
                builder.Append(indent).AppendLine("    return __lakona_PostAsync<TRequest>((actor, argument, _) => method(actor, argument), methodName, request, cancellationToken);");
            }
            else
            {
                builder.Append(indent).AppendLine("    return __lakona_PostAsync<TRequest>((actor, argument, _) => method(actor, argument), request, cancellationToken);");
            }

            builder.Append(indent).AppendLine("}");
        }

        private static void AppendResolveActorMethodName(
            StringBuilder builder,
            ActorInfo actor,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorTypeName = EscapeStringLiteral(GetRuntimeTypeFullName(actor.Symbol));

            builder.Append(indent).AppendLine("private static string __lakona_ResolveActorMethod(");
            builder.Append(indent).AppendLine("    global::System.Delegate method,");
            builder.Append(indent).AppendLine("    global::System.Type requestType,");
            builder.Append(indent).AppendLine("    global::System.Type? resultType)");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    var methodInfo = method.Method;");
            builder.Append(indent).AppendLine("    var declaringTypeName = methodInfo.DeclaringType?.FullName;");
            builder.Append(indent).AppendLine("    var methodName = methodInfo.Name;");
            builder.AppendLine();

            foreach (var method in actor.Methods)
            {
                var requestType = method.RequestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                builder.Append(indent).Append("    if (declaringTypeName == \"").Append(actorTypeName).AppendLine("\"");
                builder.Append(indent).Append("        && methodName == \"").Append(EscapeStringLiteral(method.Name)).AppendLine("\"");
                builder.Append(indent).Append("        && requestType == typeof(").Append(requestType).AppendLine(")");
                if (method.ResultType == null)
                {
                    builder.Append(indent).AppendLine("        && resultType == null)");
                }
                else
                {
                    builder.Append(indent).Append("        && resultType == typeof(").Append(DisplayType(method.ResultType, actor.Symbol.ContainingNamespace)).AppendLine("))");
                }

                builder.Append(indent).AppendLine("    {");
                builder.Append(indent).Append("        return \"").Append(EscapeStringLiteral(method.ActorMethodName)).AppendLine("\";");
                builder.Append(indent).AppendLine("    }");
                builder.AppendLine();
            }

            builder.Append(indent).Append("    throw new global::System.ArgumentException(\"The supplied actor method is not a generated actor method for ")
                .Append(EscapeStringLiteral(actor.Symbol.Name))
                .AppendLine(".\", nameof(method));");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendDistributedRef(
            StringBuilder builder,
            ActorInfo actor,
            string distributedRefType,
            string keyType,
            string routePrefix,
            string callDelegateType,
            string callNoCancellationDelegateType,
            string postDelegateType,
            string postNoCancellationDelegateType,
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

            builder.AppendLine();
            AppendActorCallApi(
                builder,
                actor,
                callDelegateType,
                callNoCancellationDelegateType,
                postDelegateType,
                postNoCancellationDelegateType,
                indentLevel + 1,
                includePost: true,
                resolveMethodName: true);
            builder.AppendLine();
            AppendResolveActorMethodName(builder, actor, indentLevel + 1);
            builder.AppendLine();
            AppendDistributedCallNoResultHelper(builder, actor, routePrefix, indentLevel + 1);
            builder.AppendLine();
            AppendDistributedPostHelper(builder, actor, routePrefix, indentLevel + 1);
            builder.AppendLine();
            AppendDistributedTellHelper(builder, actor, routePrefix, indentLevel + 1);
            builder.AppendLine();
            AppendDistributedAskHelper(builder, actor, routePrefix, indentLevel + 1);

            builder.AppendLine();
            AppendResolveNodeMethod(builder, routePrefix, indentLevel + 1);
            builder.AppendLine();
            AppendIsLocationFailureMethod(builder, indentLevel + 1);

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

        private static void AppendLocalCallNoResultHelper(
            StringBuilder builder,
            ActorInfo actor,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Append(indent).AppendLine("private global::System.Threading.Tasks.ValueTask __lakona_CallAsync<TRequest>(");
            builder.Append(indent).Append("    global::System.Func<").Append(actorType).AppendLine(", TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask> method,");
            builder.Append(indent).AppendLine("    string methodName,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken)");
            builder.Append(indent).AppendLine("{");
            AppendActorIdSetup(builder, actor, routePrefix, indentLevel + 1);
            builder.Append(indent).Append("    return _runtime.TellAsync<").Append(actorType).AppendLine(">(actorId, (actor, ct) => method(actor, request, ct), cancellationToken);");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendLocalPostHelper(
            StringBuilder builder,
            ActorInfo actor,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Append(indent).AppendLine("private global::System.Threading.Tasks.ValueTask __lakona_PostAsync<TRequest>(");
            builder.Append(indent).Append("    global::System.Func<").Append(actorType).AppendLine(", TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask> method,");
            builder.Append(indent).AppendLine("    string methodName,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken)");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    cancellationToken.ThrowIfCancellationRequested();");
            AppendActorIdSetup(builder, actor, routePrefix, indentLevel + 1);
            builder.Append(indent).Append("    var result = _runtime.TryTell<").Append(actorType).AppendLine(">(actorId, (actor, ct) => method(actor, request, ct), cancellationToken);");
            builder.Append(indent).AppendLine("    if (result == global::Lakona.Game.Server.Actors.ActorTellResult.Accepted)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        return default;");
            builder.Append(indent).AppendLine("    }");
            builder.AppendLine();
            builder.Append(indent).AppendLine("    var status = result == global::Lakona.Game.Server.Actors.ActorTellResult.MailboxFull");
            builder.Append(indent).AppendLine("        ? global::Lakona.Game.Server.Actors.ActorCallStatus.Backpressure");
            builder.Append(indent).AppendLine("        : result == global::Lakona.Game.Server.Actors.ActorTellResult.ActorNotFound");
            builder.Append(indent).AppendLine("            ? global::Lakona.Game.Server.Actors.ActorCallStatus.ActorNotFound");
            builder.Append(indent).AppendLine("            : global::Lakona.Game.Server.Actors.ActorCallStatus.Failed;");
            builder.Append(indent).Append("    throw new global::Lakona.Game.Server.Actors.ActorCallException(status, actorId, \"")
                .Append(actor.ActorName)
                .AppendLine("\", methodName, \"Local actor post was rejected with result \" + result + \".\");");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendLocalAskHelper(
            StringBuilder builder,
            ActorInfo actor,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Append(indent).AppendLine("private global::System.Threading.Tasks.ValueTask<TResult> __lakona_CallAsync<TRequest, TResult>(");
            builder.Append(indent).Append("    global::System.Func<").Append(actorType).AppendLine(", TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<TResult>> method,");
            builder.Append(indent).AppendLine("    string methodName,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken)");
            builder.Append(indent).AppendLine("{");
            AppendActorIdSetup(builder, actor, routePrefix, indentLevel + 1);
            builder.Append(indent).Append("    return _runtime.AskAsync<").Append(actorType).AppendLine(", TResult>(actorId, (actor, ct) => method(actor, request, ct), cancellationToken);");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendDistributedCallNoResultHelper(
            StringBuilder builder,
            ActorInfo actor,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Append(indent).AppendLine("private async global::System.Threading.Tasks.ValueTask __lakona_CallAsync<TRequest>(");
            builder.Append(indent).Append("    global::System.Func<").Append(actorType).AppendLine(", TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask> method,");
            builder.Append(indent).AppendLine("    string methodName,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken)");
            builder.Append(indent).AppendLine("{");
            AppendActorIdSetup(builder, actor, routePrefix, indentLevel + 1);
            builder.Append(indent).AppendLine("    if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).Append("        await _runtime.TellAsync<").Append(actorType).AppendLine(">(actorId, (actor, ct) => method(actor, request, ct), cancellationToken).ConfigureAwait(false);");
            builder.Append(indent).AppendLine("        return;");
            builder.Append(indent).AppendLine("    }");
            builder.AppendLine();
            builder.Append(indent).AppendLine("    var node = await ResolveNodeAsync(actorId, methodName, cancellationToken).ConfigureAwait(false);");
            AppendRemoteInvocationSetup(builder, actor, routePrefix, actor.ActorName, "methodName", "node", indentLevel + 1, includeActorId: false, methodNameIsExpression: true);
            builder.Append(indent).AppendLine("    try");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        var result = await _remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.Append(indent).Append("        global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(result, actorId, \"")
                .Append(actor.ActorName)
                .AppendLine("\", methodName, node, correlationId);");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("    catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        _directoryCache.Remove(actorId);");
            builder.Append(indent).AppendLine("        throw;");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendDistributedPostHelper(
            StringBuilder builder,
            ActorInfo actor,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Append(indent).AppendLine("private global::System.Threading.Tasks.ValueTask __lakona_PostAsync<TRequest>(");
            builder.Append(indent).Append("    global::System.Func<").Append(actorType).AppendLine(", TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask> method,");
            builder.Append(indent).AppendLine("    string methodName,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken)");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).AppendLine("    return __lakona_TellAsync(method, methodName, request, cancellationToken);");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendDistributedTellHelper(
            StringBuilder builder,
            ActorInfo actor,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Append(indent).AppendLine("private async global::System.Threading.Tasks.ValueTask __lakona_TellAsync<TRequest>(");
            builder.Append(indent).Append("    global::System.Func<").Append(actorType).AppendLine(", TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask> method,");
            builder.Append(indent).AppendLine("    string methodName,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken)");
            builder.Append(indent).AppendLine("{");
            AppendActorIdSetup(builder, actor, routePrefix, indentLevel + 1);
            builder.Append(indent).AppendLine("    if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).Append("        var localResult = _runtime.TryTell<").Append(actorType).AppendLine(">(actorId, (actor, ct) => method(actor, request, ct), cancellationToken);");
            builder.Append(indent).AppendLine("        if (localResult == global::Lakona.Game.Server.Actors.ActorTellResult.Accepted)");
            builder.Append(indent).AppendLine("        {");
            builder.Append(indent).AppendLine("            return;");
            builder.Append(indent).AppendLine("        }");
            builder.AppendLine();
            builder.Append(indent).AppendLine("        var localStatus = localResult == global::Lakona.Game.Server.Actors.ActorTellResult.MailboxFull");
            builder.Append(indent).AppendLine("            ? global::Lakona.Game.Server.Actors.ActorCallStatus.Backpressure");
            builder.Append(indent).AppendLine("            : localResult == global::Lakona.Game.Server.Actors.ActorTellResult.ActorNotFound");
            builder.Append(indent).AppendLine("                ? global::Lakona.Game.Server.Actors.ActorCallStatus.ActorNotFound");
            builder.Append(indent).AppendLine("                : global::Lakona.Game.Server.Actors.ActorCallStatus.Failed;");
            builder.Append(indent).Append("        throw new global::Lakona.Game.Server.Actors.ActorCallException(localStatus, actorId, \"")
                .Append(actor.ActorName)
                .AppendLine("\", methodName, \"Routed local actor post was rejected with result \" + localResult + \".\");");
            builder.Append(indent).AppendLine("    }");
            builder.AppendLine();
            builder.Append(indent).AppendLine("    var node = await ResolveNodeAsync(actorId, methodName, cancellationToken).ConfigureAwait(false);");
            AppendRemoteInvocationSetup(builder, actor, routePrefix, actor.ActorName, "methodName", "node", indentLevel + 1, includeActorId: false, methodNameIsExpression: true);
            builder.Append(indent).AppendLine("    try");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        var result = await _remote.TellAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.Append(indent).Append("        global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureAccepted(result, actorId, \"")
                .Append(actor.ActorName)
                .AppendLine("\", methodName, node, correlationId);");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("    catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        _directoryCache.Remove(actorId);");
            builder.Append(indent).AppendLine("        throw;");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendDistributedAskHelper(
            StringBuilder builder,
            ActorInfo actor,
            string routePrefix,
            int indentLevel)
        {
            var indent = Indent(indentLevel);
            var actorType = actor.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Append(indent).AppendLine("private async global::System.Threading.Tasks.ValueTask<TResult> __lakona_CallAsync<TRequest, TResult>(");
            builder.Append(indent).Append("    global::System.Func<").Append(actorType).AppendLine(", TRequest, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<TResult>> method,");
            builder.Append(indent).AppendLine("    string methodName,");
            builder.Append(indent).AppendLine("    TRequest request,");
            builder.Append(indent).AppendLine("    global::System.Threading.CancellationToken cancellationToken)");
            builder.Append(indent).AppendLine("{");
            AppendActorIdSetup(builder, actor, routePrefix, indentLevel + 1);
            builder.Append(indent).AppendLine("    if (_runtime.GetState(actorId) != global::Lakona.Game.Server.Actors.ActorState.Dead)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).Append("        return await _runtime.AskAsync<").Append(actorType).AppendLine(", TResult>(actorId, (actor, ct) => method(actor, request, ct), cancellationToken).ConfigureAwait(false);");
            builder.Append(indent).AppendLine("    }");
            builder.AppendLine();
            builder.Append(indent).AppendLine("    var node = await ResolveNodeAsync(actorId, methodName, cancellationToken).ConfigureAwait(false);");
            AppendRemoteInvocationSetup(builder, actor, routePrefix, actor.ActorName, "methodName", "node", indentLevel + 1, includeActorId: false, methodNameIsExpression: true);
            builder.Append(indent).AppendLine("    try");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        var result = await _remote.AskAsync(invocation, cancellationToken).ConfigureAwait(false);");
            builder.Append(indent).Append("        global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied(result, actorId, \"")
                .Append(actor.ActorName)
                .AppendLine("\", methodName, node, correlationId);");
            builder.Append(indent).AppendLine("        return _serializer.Deserialize<TResult>(result.Payload);");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("    catch (global::Lakona.Game.Server.Actors.ActorCallException exception) when (IsLocationFailure(exception))");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        _directoryCache.Remove(actorId);");
            builder.Append(indent).AppendLine("        throw;");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");
        }

        private static void AppendLocalRef(
            StringBuilder builder,
            ActorInfo actor,
            string localRefType,
            string keyType,
            string routePrefix,
            string callDelegateType,
            string callNoCancellationDelegateType,
            string postDelegateType,
            string postNoCancellationDelegateType,
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

            builder.AppendLine();
            AppendActorCallApi(
                builder,
                actor,
                callDelegateType,
                callNoCancellationDelegateType,
                postDelegateType,
                postNoCancellationDelegateType,
                indentLevel + 1,
                includePost: true,
                resolveMethodName: true);
            builder.AppendLine();
            AppendResolveActorMethodName(builder, actor, indentLevel + 1);
            builder.AppendLine();
            AppendLocalCallNoResultHelper(builder, actor, routePrefix, indentLevel + 1);
            builder.AppendLine();
            AppendLocalPostHelper(builder, actor, routePrefix, indentLevel + 1);
            builder.AppendLine();
            AppendLocalAskHelper(builder, actor, routePrefix, indentLevel + 1);

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
                builder.Append(indent).AppendLine("    if (envelope.ReplyCorrelationId is not null)");
                builder.Append(indent).AppendLine("    {");
                builder.Append(indent).AppendLine("        await global::Lakona.Game.Server.Actors.RemoteActorGateway.SendReplyAsync(");
                builder.Append(indent).AppendLine("            _router,");
                builder.Append(indent).AppendLine("            envelope.SourceNode,");
                builder.Append(indent).AppendLine("            envelope.ReplyCorrelationId,");
                builder.Append(indent).AppendLine("            global::System.ReadOnlyMemory<byte>.Empty,");
                builder.Append(indent).AppendLine("            cancellationToken).ConfigureAwait(false);");
                builder.Append(indent).AppendLine("    }");
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
            bool includeActorId = true,
            bool methodNameIsExpression = false)
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
                .Append("\", ");
            if (methodNameIsExpression)
            {
                builder.Append(methodName);
            }
            else
            {
                builder.Append('"').Append(methodName).Append('"');
            }

            builder.AppendLine(", payload, deadline, correlationId);");
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

        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
                UnsupportedMethodInfo[] unsupportedMethods)
            {
                Symbol = symbol;
                KeyType = keyType;
                ActorName = actorName;
                IsLocalOnly = isLocalOnly;
                Methods = methods;
                UnsupportedMethods = unsupportedMethods;
            }

            public INamedTypeSymbol Symbol { get; }

            public ITypeSymbol KeyType { get; }

            public string ActorName { get; }

            public bool IsLocalOnly { get; }

            public MethodInfo[] Methods { get; }

            public UnsupportedMethodInfo[] UnsupportedMethods { get; }
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
