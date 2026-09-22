using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Lakona.Rpc.Analyzers;

public sealed partial class LakonaRpcSourceGenerator
{
    private static class RpcSymbolReader
    {
        public static List<RpcServiceModel> FindServices(Compilation compilation)
        {
            var notificationContracts = new Dictionary<string, NotificationContractModel>(StringComparer.Ordinal);
            var services = new List<RpcServiceModel>();

            foreach (var type in EnumerateCandidateTypes(compilation))
            {
                if (type.TypeKind != TypeKind.Interface)
                    continue;

                if (GetAttribute(type, "RpcNotificationContractAttribute") is not null)
                {
                    var notificationContract = TryCreateNotificationContract(type);
                    notificationContracts[notificationContract.FullName] = notificationContract;
                }

                var serviceAttribute = GetAttribute(type, "RpcServiceAttribute");
                if (serviceAttribute is null || !TryGetIntId(serviceAttribute, out var serviceId))
                    continue;

                var service = CreateService(type, serviceAttribute, serviceId);
                services.Add(service);
            }

            ValidateServiceIds(services);
            var orderedServices = services
                .OrderBy(static service => service.ServiceId)
                .ThenBy(static service => service.FullName, StringComparer.Ordinal)
                .ToList();
            var referencedNotificationContracts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var service in orderedServices)
            {
                if (service.NotificationContractInterfaceFullName is null)
                    continue;

                if (!notificationContracts.TryGetValue(service.NotificationContractInterfaceFullName, out var notificationContract))
                    throw new InvalidOperationException(
                        $"Notification contract interface '{service.NotificationContractInterfaceFullName}' declared by service '{service.FullName}' was not found or is missing a valid [RpcNotificationContract] contract.");

                if (referencedNotificationContracts.TryGetValue(notificationContract.FullName, out var existingServiceFullName))
                    throw new InvalidOperationException(
                        $"Notification contract interface '{notificationContract.FullName}' is referenced by multiple RPC services: '{existingServiceFullName}' and '{service.FullName}'. Each notification contract may be referenced by at most one RPC service.");

                referencedNotificationContracts.Add(notificationContract.FullName, service.FullName);
                service.NotificationMethods = notificationContract.Methods;
            }

            ValidateGeneratedApiNames(orderedServices);
            return orderedServices;
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateCandidateTypes(Compilation compilation)
        {
            var seenAssemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols.Prepend(compilation.Assembly))
            {
                if (!seenAssemblies.Add(assembly))
                    continue;
                if (!SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly) && IsFrameworkAssembly(assembly.Identity.Name))
                    continue;

                foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
                    yield return type;
            }
        }

        private static bool IsFrameworkAssembly(string assemblyName) =>
            assemblyName.StartsWith("System", StringComparison.Ordinal) ||
            assemblyName.StartsWith("Microsoft", StringComparison.Ordinal) ||
            string.Equals(assemblyName, "mscorlib", StringComparison.Ordinal) ||
            string.Equals(assemblyName, "netstandard", StringComparison.Ordinal);

        private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceOrTypeSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                if (member is INamespaceSymbol namespaceSymbol)
                {
                    foreach (var type in EnumerateTypes(namespaceSymbol))
                        yield return type;
                }
                else if (member is INamedTypeSymbol namedType)
                {
                    yield return namedType;
                    foreach (var nested in EnumerateTypes(namedType))
                        yield return nested;
                }
            }
        }

        private static RpcServiceModel CreateService(INamedTypeSymbol type, AttributeData attribute, int serviceId)
        {
            var methods = new List<RpcMethodModel>();
            foreach (var member in type.GetMembers().OfType<IMethodSymbol>().OrderBy(static method => method.Name, StringComparer.Ordinal))
            {
                var methodAttribute = GetAttribute(member, "RpcMethodAttribute");
                if (methodAttribute is null || !TryGetIntId(methodAttribute, out var methodId))
                    continue;

                if (!IsValueTask(member.ReturnType, out var resultType, out var isVoid))
                    throw new InvalidOperationException(
                        $"Unsupported return type '{member.ReturnType.ToDisplayString()}' on {type.Name}.{member.Name}. RPC methods must return ValueTask or ValueTask<T>.");

                methods.Add(new RpcMethodModel(
                    member.Name,
                    methodId,
                    CreateParameters(member.Parameters),
                    isVoid ? null : TypeName(resultType!),
                    isVoid));
            }

            ValidateMethodIds(methods, type.Name, "MethodId", "[RpcMethod]");
            if (methods.Count == 0)
                throw new InvalidOperationException($"RPC service '{type.Name}' must declare at least one [RpcMethod] contract.");

            TryGetTypeArgument(attribute, out var notificationContractName, out var notificationContractFullName);
            var fullName = TypeName(type);
            var apiGroup = GetNamedString(attribute, "ApiGroup");
            var apiName = GetNamedString(attribute, "ApiName");
            if (apiGroup is not null)
                ValidateExplicitApiIdentifier(type.Name, "ApiGroup", apiGroup);
            if (apiName is not null)
                ValidateExplicitApiIdentifier(type.Name, "ApiName", apiName);

            return new RpcServiceModel(
                type.Name,
                fullName,
                type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                serviceId,
                methods,
                notificationContractName,
                notificationContractFullName,
                apiGroup ?? Naming.GetFacadeGroupName(fullName),
                apiName ?? Naming.GetFacadeServicePropertyName(type.Name));
        }

        private static NotificationContractModel TryCreateNotificationContract(INamedTypeSymbol type)
        {
            var methods = new List<RpcNotificationMethodModel>();
            foreach (var member in type.GetMembers().OfType<IMethodSymbol>().OrderBy(static method => method.Name, StringComparer.Ordinal))
            {
                var notificationAttribute = GetAttribute(member, "RpcNotificationAttribute");
                if (notificationAttribute is null || !TryGetIntId(notificationAttribute, out var methodId))
                    continue;

                var returnsValueTask = false;
                if (!member.ReturnsVoid)
                {
                    if (!IsValueTask(member.ReturnType, out var resultType, out var isVoid) || !isVoid || resultType is not null)
                        throw new InvalidOperationException($"RPC notification method '{type.Name}.{member.Name}' must return void or ValueTask.");

                    returnsValueTask = true;
                }

                methods.Add(new RpcNotificationMethodModel(member.Name, methodId, CreateNotificationParameters(member.Parameters), returnsValueTask));
            }

            ValidateMethodIds(methods, type.Name, "NotificationId", "[RpcNotification]");
            if (methods.Count == 0)
                throw new InvalidOperationException($"RPC notification contract interface '{type.Name}' must declare at least one valid [RpcNotification] method.");

            return new NotificationContractModel(type.Name, TypeName(type), methods);
        }

        private static List<RpcParameterModel> CreateParameters(ImmutableArray<IParameterSymbol> parameters)
        {
            if (parameters.Length != 1)
                throw new InvalidOperationException("RPC methods and notifications must declare exactly one DTO payload parameter.");

            return parameters
                .Select(static parameter => new RpcParameterModel(
                    TypeName(parameter.Type),
                    parameter.Name,
                    isCancellationToken: IsCancellationToken(parameter.Type)))
                .ToList();
        }

        private static List<RpcParameterModel> CreateNotificationParameters(ImmutableArray<IParameterSymbol> parameters)
        {
            if (parameters.Length is < 1 or > 2)
                throw new InvalidOperationException("RPC notifications must declare exactly one DTO payload parameter and may include a trailing CancellationToken.");

            if (parameters.Length == 2 && !IsCancellationToken(parameters[1].Type))
                throw new InvalidOperationException("RPC notifications may only use CancellationToken as their optional second parameter.");

            return parameters
                .Select(static parameter => new RpcParameterModel(
                    TypeName(parameter.Type),
                    parameter.Name,
                    isCancellationToken: IsCancellationToken(parameter.Type)))
                .ToList();
        }

        private static bool IsCancellationToken(ITypeSymbol type) =>
            string.Equals(type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), "System.Threading.CancellationToken", StringComparison.Ordinal);

        private static bool IsValueTask(ITypeSymbol returnType, out ITypeSymbol? resultType, out bool isVoid)
        {
            resultType = null;
            isVoid = false;
            if (returnType is not INamedTypeSymbol named ||
                !string.Equals(named.Name, "ValueTask", StringComparison.Ordinal) ||
                !string.Equals(named.ContainingNamespace?.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal))
            {
                return false;
            }

            if (named.TypeArguments.Length == 0)
            {
                isVoid = true;
                return true;
            }

            if (named.TypeArguments.Length != 1)
                return false;

            resultType = named.TypeArguments[0];
            return true;
        }

        private static AttributeData? GetAttribute(ISymbol symbol, string attributeName)
        {
            var shortName = attributeName.EndsWith("Attribute", StringComparison.Ordinal)
                ? attributeName.Substring(0, attributeName.Length - "Attribute".Length)
                : attributeName;

            foreach (var attribute in symbol.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass is null)
                    continue;

                if (string.Equals(attributeClass.Name, attributeName, StringComparison.Ordinal) ||
                    string.Equals(attributeClass.Name, shortName, StringComparison.Ordinal))
                    return attribute;
            }

            return null;
        }

        private static bool TryGetIntId(AttributeData attribute, out int id)
        {
            foreach (var argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(static pair => pair.Value)))
            {
                if (argument.Value is int intValue)
                {
                    id = intValue;
                    return true;
                }
            }

            id = default;
            return false;
        }

        private static bool TryGetTypeArgument(AttributeData attribute, out string? name, out string? fullName)
        {
            foreach (var argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(static pair => pair.Value)))
            {
                if (argument.Value is INamedTypeSymbol type)
                {
                    name = type.Name;
                    fullName = TypeName(type);
                    return true;
                }
            }

            name = null;
            fullName = null;
            return false;
        }

        private static string? GetNamedString(AttributeData attribute, string name)
        {
            foreach (var pair in attribute.NamedArguments)
            {
                if (string.Equals(pair.Key, name, StringComparison.Ordinal) && pair.Value.Value is string value)
                    return value.Trim();
            }

            return null;
        }

        private static void ValidateExplicitApiIdentifier(string serviceName, string propertyName, string value)
        {
            if (!Naming.IsValidIdentifier(value))
                throw new InvalidOperationException(
                    $"RPC service '{serviceName}' uses invalid {propertyName} '{value}'. {propertyName} must be a valid C# identifier.");
        }

        private static string TypeName(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static void ValidateServiceIds(IReadOnlyList<RpcServiceModel> services)
        {
            var seen = new Dictionary<int, string>();
            foreach (var service in services)
            {
                if (service.ServiceId <= 0)
                    throw new InvalidOperationException($"Invalid ServiceId {service.ServiceId} found on '{service.InterfaceName}'. Each [RpcService] id must be greater than 0.");

                if (seen.TryGetValue(service.ServiceId, out var existingName))
                    throw new InvalidOperationException($"Duplicate ServiceId {service.ServiceId} found on '{existingName}' and '{service.InterfaceName}'. Each [RpcService] must have a unique id.");

                seen.Add(service.ServiceId, service.InterfaceName);
            }
        }

        private static void ValidateGeneratedApiNames(IReadOnlyList<RpcServiceModel> services)
        {
            var duplicates = services
                .GroupBy(static service => service.ApiGroupName + "." + service.ApiName, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .ToArray();

            foreach (var group in duplicates)
            {
                var serviceNames = string.Join(", ", group
                    .Select(static service => service.FullName)
                    .OrderBy(static name => name, StringComparer.Ordinal));
                throw new InvalidOperationException($"Duplicate generated API service name '{group.Key}' for services: {serviceNames}.");
            }
        }

        private static void ValidateMethodIds<T>(IReadOnlyList<T> methods, string interfaceName, string idName, string attributeName)
            where T : IRpcMethodContract
        {
            var seen = new Dictionary<int, string>();
            foreach (var method in methods)
            {
                if (method.MethodId <= 0)
                    throw new InvalidOperationException($"Invalid {idName} {method.MethodId} found on '{method.Name}' in {interfaceName}. Each {attributeName} id must be greater than 0.");

                if (seen.TryGetValue(method.MethodId, out var existingName))
                    throw new InvalidOperationException($"Duplicate {idName} {method.MethodId} found on '{existingName}' and '{method.Name}' in {interfaceName}.");

                seen.Add(method.MethodId, method.Name);
            }
        }
    }
}
