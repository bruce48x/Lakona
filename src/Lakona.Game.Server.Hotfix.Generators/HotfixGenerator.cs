using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lakona.Game.Server.Hotfix.Generators
{
    [Generator]
    public sealed class HotfixGenerator : IIncrementalGenerator
    {
        private const string HotfixStateAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixStateAttribute";
        private const string HotfixRpcServiceAttributeName = "Lakona.Game.Server.Hotfix.Abstractions.HotfixRpcServiceAttribute";
        private const string RpcServiceAttributeName = "Lakona.Rpc.Core.RpcServiceAttribute";
        private const string RpcMethodAttributeName = "Lakona.Rpc.Core.RpcMethodAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var states = context.SyntaxProvider
                .CreateSyntaxProvider(
                    IsStateCandidate,
                    GetState)
                .Where(IsNotNull);

            context.RegisterSourceOutput(states, GenerateState);
            context.RegisterSourceOutput(states, GenerateStateCaller);

            var services = context.SyntaxProvider
                .CreateSyntaxProvider(
                    IsStateCandidate,
                    GetRpcService)
                .Where(IsRpcServiceNotNull);

            context.RegisterSourceOutput(services, GenerateRpcService);
            context.RegisterSourceOutput(services.Collect(), GenerateRpcServiceExtension);
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

        private static bool IsRpcServiceNotNull(HotfixRpcServiceInfo? service)
        {
            return service != null;
        }

        private static HotfixRpcServiceInfo? GetRpcService(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            var declaration = (TypeDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken);
            if (symbol == null)
            {
                return null;
            }

            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != HotfixRpcServiceAttributeName)
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length == 0 ||
                    attribute.ConstructorArguments[0].Value is not INamedTypeSymbol contract)
                {
                    return null;
                }

                var endpointName = "default";
                var bindingSetName = "default";
                foreach (var named in attribute.NamedArguments)
                {
                    if (named.Key == "EndpointName" && named.Value.Value is string endpoint)
                    {
                        endpointName = endpoint;
                    }
                    else if (named.Key == "BindingSetName" && named.Value.Value is string bindingSet)
                    {
                        bindingSetName = bindingSet;
                    }
                }

                return new HotfixRpcServiceInfo(symbol, declaration, contract, endpointName, bindingSetName);
            }

            return null;
        }

        private static void GenerateRpcService(SourceProductionContext context, HotfixRpcServiceInfo? service)
        {
            if (service == null)
            {
                return;
            }

            if (!IsPartial(service.Declaration))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.ServiceMarkerMustBePartial,
                    service.Declaration.Identifier.GetLocation(),
                    service.Symbol.ToDisplayString()));
                return;
            }

            if (!ValidateRpcService(context, service))
            {
                return;
            }

            context.AddSource(
                CreateRpcServiceHintName(service.Symbol),
                SourceText.From(GenerateRpcServiceSource(service), Encoding.UTF8));
        }

        private static void GenerateRpcServiceExtension(SourceProductionContext context, System.Collections.Immutable.ImmutableArray<HotfixRpcServiceInfo?> services)
        {
            var concrete = services.OfType<HotfixRpcServiceInfo>()
                .Where(IsSupportedRpcService)
                .ToArray();
            if (concrete.Length == 0)
            {
                return;
            }

            foreach (var duplicate in concrete
                .GroupBy(service => service.BindingSetName + "\u001f" + service.ContractType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Where(group => group.Count() > 1)
                .SelectMany(group => group.Skip(1)))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.DuplicateServiceMarker,
                    duplicate.Declaration.Identifier.GetLocation(),
                    duplicate.Symbol.ToDisplayString(),
                    duplicate.ContractType.ToDisplayString(),
                    duplicate.BindingSetName));
            }

            foreach (var bindingSet in concrete.GroupBy(service => service.BindingSetName))
            {
                if (bindingSet.Select(service => service.EndpointName).Distinct().Count() <= 1)
                {
                    continue;
                }

                var first = bindingSet.First();
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.BindingSetEndpointMismatch,
                    first.Declaration.Identifier.GetLocation(),
                    bindingSet.Key));
            }

            context.AddSource("GeneratedHotfixServices.g.cs", SourceText.From(GenerateRpcServiceExtensionSource(concrete), Encoding.UTF8));
        }

        private static string GenerateRpcServiceExtensionSource(HotfixRpcServiceInfo[] services)
        {
            var firstService = services[0];
            var namespaceName = firstService.Symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : firstService.Symbol.ContainingNamespace.ToDisplayString();
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            if (namespaceName != null)
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine(";");
                builder.AppendLine();
            }

            builder.AppendLine("internal static class GeneratedHotfixServicesExtensions");
            builder.AppendLine("{");
            builder.AppendLine("    public static global::Lakona.Game.Server.Hosting.LakonaGameServerBuilder UseGeneratedHotfixServices(");
            builder.AppendLine("        this global::Lakona.Game.Server.Hosting.LakonaGameServerBuilder builder)");
            builder.AppendLine("    {");
            builder.AppendLine("        return builder.BindServices(BindGeneratedHotfixServices);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public static void BindGeneratedHotfixServices(");
            builder.AppendLine("        global::Lakona.Rpc.Server.RpcServiceRegistry registry,");
            builder.AppendLine("        global::System.IServiceProvider services)");
            builder.AppendLine("    {");
            builder.AppendLine("        BindGeneratedHotfixServices(registry, services, \"default\");");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public static void BindGeneratedHotfixServices(");
            builder.AppendLine("        global::Lakona.Rpc.Server.RpcServiceRegistry registry,");
            builder.AppendLine("        global::System.IServiceProvider services,");
            builder.AppendLine("        string bindingSetName)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (bindingSetName is null)");
            builder.AppendLine("        {");
            builder.AppendLine("            throw new global::System.ArgumentNullException(nameof(bindingSetName));");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        switch (bindingSetName)");
            builder.AppendLine("        {");
            foreach (var bindingSet in services.GroupBy(service => service.BindingSetName).OrderBy(group => group.Key))
            {
                builder.Append("            case \"").Append(EscapeStringLiteral(bindingSet.Key)).AppendLine("\":");
                AppendRpcServiceBindingSet(builder, bindingSet);
                builder.AppendLine("                break;");
            }

            builder.AppendLine("            default:");
            builder.AppendLine("                throw new global::System.InvalidOperationException($\"Unknown generated hotfix service binding set '{bindingSetName}'.\");");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendRpcServiceBindingSet(StringBuilder builder, IEnumerable<HotfixRpcServiceInfo> services)
        {
            builder.AppendLine("                {");
            foreach (var service in services)
            {
                AppendRpcServiceBinding(builder, service);
            }

            builder.AppendLine("                }");
        }

        private static void AppendRpcServiceBinding(StringBuilder builder, HotfixRpcServiceInfo service)
        {
            var generatedNamespace = CreateGeneratedNamespace(service.Symbol);
            var proxyType = GetGeneratedProxyTypeDisplay(service);
            var rpcServiceAttribute = service.ContractType.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            var callbackType = GetNamedTypeArgument(rpcServiceAttribute, "NotificationContract");
            var binderName = generatedNamespace + "." + GetBinderTypeName(service.ContractType.Name);
            var endpointName = EscapeStringLiteral(service.EndpointName);

            builder.Append("                    global::").Append(binderName).AppendLine(".BindFactory(");
            builder.AppendLine("                        registry,");
            builder.AppendLine("                        session => new " + proxyType + "(");
            builder.AppendLine("                            global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Hotfix.Abstractions.IHotfixServiceInvoker>(services),");
            builder.AppendLine("                            services,");
            builder.AppendLine("                            global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.Actors.IActorRuntime>(services),");
            builder.AppendLine("                            global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Lakona.Game.Server.ILakonaGameServer>(services),");
            if (callbackType != null)
            {
                var callbackProxyName = generatedNamespace + "." + GetNotificationProxyTypeName(callbackType.Name);
                builder.Append("                            new global::").Append(callbackProxyName).AppendLine("(session),");
            }

            builder.AppendLine("                            session.ContextId,");
            builder.Append("                            new global::Lakona.Game.Abstractions.GameEndpointName(\"").Append(endpointName).AppendLine("\")));");
            builder.AppendLine();
        }

        private static bool ValidateRpcService(SourceProductionContext context, HotfixRpcServiceInfo service)
        {
            if (!IsSupportedRpcServiceContract(service))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.UnsupportedServiceContract,
                    service.Declaration.Identifier.GetLocation(),
                    service.Symbol.ToDisplayString()));
                return false;
            }

            var rpcServiceAttribute = service.ContractType.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            var callbackType = GetNamedTypeArgument(rpcServiceAttribute, "NotificationContract");
            if (callbackType != null && callbackType.TypeKind != TypeKind.Interface)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.UnsupportedNotificationContract,
                    service.Declaration.Identifier.GetLocation(),
                    service.Symbol.ToDisplayString()));
                return false;
            }

            foreach (var method in GetContractMethods(service.ContractType))
            {
                var methodDisplay = method.ToDisplayString();
                var rpcMethod = method.GetAttributes()
                    .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcMethodAttributeName);
                if (rpcMethod == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.RpcMethodAttributeRequired,
                        method.Locations.FirstOrDefault() ?? service.Declaration.Identifier.GetLocation(),
                        methodDisplay));
                    return false;
                }

                if (method.Parameters.Length != 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.RpcMethodRequiresSingleRequest,
                        method.Locations.FirstOrDefault() ?? service.Declaration.Identifier.GetLocation(),
                        methodDisplay));
                    return false;
                }

                if (!IsSupportedRpcReturnType(method.ReturnType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        HotfixGeneratorDiagnostics.UnsupportedRpcMethodReturnType,
                        method.Locations.FirstOrDefault() ?? service.Declaration.Identifier.GetLocation(),
                        methodDisplay));
                    return false;
                }
            }

            return true;
        }

        private static bool IsSupportedRpcService(HotfixRpcServiceInfo service)
        {
            if (!IsPartial(service.Declaration) || !IsSupportedRpcServiceContract(service))
            {
                return false;
            }

            var rpcServiceAttribute = service.ContractType.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            var callbackType = GetNamedTypeArgument(rpcServiceAttribute, "NotificationContract");
            if (callbackType != null && callbackType.TypeKind != TypeKind.Interface)
            {
                return false;
            }

            return GetContractMethods(service.ContractType)
                .All(method => method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == RpcMethodAttributeName) &&
                    method.Parameters.Length == 1 &&
                    IsSupportedRpcReturnType(method.ReturnType));
        }

        private static bool IsSupportedRpcServiceContract(HotfixRpcServiceInfo service)
        {
            return service.ContractType.TypeKind == TypeKind.Interface &&
                service.ContractType.GetAttributes()
                    .Any(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
        }

        private static IEnumerable<IMethodSymbol> GetContractMethods(INamedTypeSymbol contractType)
        {
            return contractType.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(method => method.MethodKind == MethodKind.Ordinary);
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
            var namespaceName = service.Symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : service.Symbol.ContainingNamespace.ToDisplayString();
            var contractDisplay = service.ContractType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var proxyName = service.Symbol.Name + "Proxy";
            var rpcServiceAttribute = service.ContractType.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == RpcServiceAttributeName);
            var callbackType = GetNamedTypeArgument(rpcServiceAttribute, "NotificationContract");
            var callbackDisplay = callbackType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            if (namespaceName != null)
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine(";");
                builder.AppendLine();
            }

            builder.Append("internal sealed class ").Append(proxyName).Append(" : ").Append(contractDisplay).AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Hotfix.Abstractions.IHotfixServiceInvoker _hotfix;");
            builder.AppendLine("    private readonly global::System.IServiceProvider _services;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.Actors.IActorRuntime _actors;");
            builder.AppendLine("    private readonly global::Lakona.Game.Server.ILakonaGameServer _gameServer;");
            builder.AppendLine("    private readonly string _connectionId;");
            builder.AppendLine("    private readonly global::Lakona.Game.Abstractions.GameEndpointName _endpointName;");
            if (callbackDisplay != null)
            {
                builder.Append("    private readonly ").Append(callbackDisplay).AppendLine(" _callback;");
            }

            builder.AppendLine();
            builder.Append("    public ").Append(proxyName).AppendLine("(");
            builder.AppendLine("        global::Lakona.Game.Server.Hotfix.Abstractions.IHotfixServiceInvoker hotfix,");
            builder.AppendLine("        global::System.IServiceProvider services,");
            builder.AppendLine("        global::Lakona.Game.Server.Actors.IActorRuntime actors,");
            builder.AppendLine("        global::Lakona.Game.Server.ILakonaGameServer gameServer,");
            if (callbackDisplay != null)
            {
                builder.Append("        ").Append(callbackDisplay).AppendLine(" callback,");
            }

            builder.AppendLine("        string connectionId,");
            builder.AppendLine("        global::Lakona.Game.Abstractions.GameEndpointName endpointName)");
            builder.AppendLine("    {");
            builder.AppendLine("        _hotfix = hotfix;");
            builder.AppendLine("        _services = services;");
            builder.AppendLine("        _actors = actors;");
            builder.AppendLine("        _gameServer = gameServer;");
            if (callbackDisplay != null)
            {
                builder.AppendLine("        _callback = callback;");
            }

            builder.AppendLine("        _connectionId = connectionId;");
            builder.AppendLine("        _endpointName = endpointName;");
            builder.AppendLine("    }");

            foreach (var method in service.ContractType.GetMembers().OfType<IMethodSymbol>().Where(method => method.MethodKind == MethodKind.Ordinary))
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
            builder.Append("    public ").Append(returnDisplay).Append(' ').Append(method.Name).Append('(')
                .Append(requestDisplay).Append(' ').Append(method.Parameters[0].Name).AppendLine(")");
            builder.AppendLine("    {");
            builder.Append("        return _hotfix.InvokeAsync<").Append(contractDisplay).Append(", ").Append(callType);
            if (returnsResult)
            {
                builder.Append(", ").Append(resultDisplay);
            }

            builder.AppendLine(">(");
            builder.Append("            ").Append(methodId).AppendLine(",");
            builder.Append("            new ").Append(callType).AppendLine("(");
            builder.Append("                ").Append(method.Parameters[0].Name).AppendLine(",");
            builder.AppendLine("                _connectionId,");
            builder.AppendLine("                _endpointName,");
            if (callbackDisplay != null)
            {
                builder.AppendLine("                _callback,");
            }

            builder.AppendLine("                _services,");
            builder.AppendLine("                _actors,");
            builder.AppendLine("                _gameServer));");
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

        private static string GetGeneratedProxyTypeDisplay(HotfixRpcServiceInfo service)
        {
            var proxyName = service.Symbol.Name + "Proxy";
            if (service.Symbol.ContainingNamespace.IsGlobalNamespace)
            {
                return proxyName;
            }

            return "global::" + service.Symbol.ContainingNamespace.ToDisplayString() + "." + proxyName;
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

        private static string CreateGeneratedNamespace(INamedTypeSymbol symbol)
        {
            var containingNamespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : symbol.ContainingNamespace.ToDisplayString();
            const string servicesSuffix = ".Services";
            if (containingNamespace.EndsWith(servicesSuffix, System.StringComparison.Ordinal))
            {
                return containingNamespace.Substring(0, containingNamespace.Length - servicesSuffix.Length) + ".Generated";
            }

            return string.IsNullOrEmpty(containingNamespace)
                ? "Generated"
                : containingNamespace + ".Generated";
        }

        private static string GetServiceTypeName(string interfaceName)
        {
            return interfaceName.Length > 1 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1])
                ? interfaceName.Substring(1)
                : interfaceName;
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
                INamedTypeSymbol symbol,
                TypeDeclarationSyntax declaration,
                INamedTypeSymbol contractType,
                string endpointName,
                string bindingSetName)
            {
                Symbol = symbol;
                Declaration = declaration;
                ContractType = contractType;
                EndpointName = endpointName;
                BindingSetName = bindingSetName;
            }

            public INamedTypeSymbol Symbol { get; }

            public TypeDeclarationSyntax Declaration { get; }

            public INamedTypeSymbol ContractType { get; }

            public string EndpointName { get; }

            public string BindingSetName { get; }
        }
    }
}
