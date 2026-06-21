using System.Reflection;
using System.Runtime.CompilerServices;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Hotfix.Scanning;

public static class HotfixBehaviorScanner
{
    public static HotfixBehaviorScanResult Scan(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return Scan(assemblies, includedTypes: null, requiredServiceContracts: null);
    }

    public static HotfixBehaviorScanResult Scan(
        Assembly assembly,
        IReadOnlyList<Type>? candidateTypes = null,
        IReadOnlyList<Type>? requiredServiceContracts = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return Scan([assembly], candidateTypes, requiredServiceContracts);
    }

    private static HotfixBehaviorScanResult Scan(
        IReadOnlyList<Assembly> assemblies,
        IReadOnlyList<Type>? includedTypes,
        IReadOnlyList<Type>? requiredServiceContracts)
    {
        var methods = new List<HotfixMethodBinding>();
        var services = new List<HotfixServiceMethodBinding>();
        var diagnostics = new List<string>();
        var keys = new HashSet<HotfixMethodKey>();
        var serviceKeys = new HashSet<string>(StringComparer.Ordinal);
        var serviceImplementations = new Dictionary<Type, HashSet<Type>>();
        var included = includedTypes is null ? null : new HashSet<Type>(includedTypes);

        foreach (var assembly in assemblies)
        {
            Type[] assemblyTypes;
            try
            {
                assemblyTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                diagnostics.Add($"Could not load all types from hotfix assembly '{assembly.FullName}': {exception.Message}");
                foreach (var loaderException in exception.LoaderExceptions.Where(static item => item is not null))
                {
                    diagnostics.Add(loaderException!.Message);
                }

                continue;
            }

            foreach (var type in assemblyTypes)
            {
                if (included is not null && !included.Contains(type))
                {
                    continue;
                }

                var behavior = type.GetCustomAttribute<HotfixBehaviorOfAttribute>();
                if (behavior is not null)
                {
                    if (!type.IsAbstract || !type.IsSealed)
                    {
                        diagnostics.Add($"Hotfix behavior '{type.FullName}' must be a static class.");
                        continue;
                    }

                    ScanBehaviorType(type, behavior.ActorType, methods, diagnostics, keys);
                }

                if (!TryGetHotfixServiceContract(type, diagnostics, out var serviceBinding))
                {
                    continue;
                }

                if (serviceBinding is not null)
                {
                    var serviceContract = serviceBinding.ContractType;
                    if (!serviceImplementations.TryGetValue(serviceContract, out var implementations))
                    {
                        implementations = [];
                        serviceImplementations.Add(serviceContract, implementations);
                    }

                    implementations.Add(type);
                    ScanServiceType(type, serviceBinding, services, diagnostics, serviceKeys);
                }
            }
        }

        foreach (var contract in requiredServiceContracts ?? Array.Empty<Type>())
        {
            serviceImplementations.TryGetValue(contract, out var implementations);
            var count = implementations?.Count ?? 0;
            if (count != 1)
            {
                diagnostics.Add($"Hotfix service contract '{contract.FullName}' requires exactly one [HotfixService] or [HotfixLifecycle] implementation; found {count}.");
            }
        }

        return new HotfixBehaviorScanResult(methods, services, diagnostics);
    }

    private static bool TryGetHotfixServiceContract(
        Type type,
        List<string> diagnostics,
        out HotfixServiceBindingDescriptor? binding)
    {
        var service = type.GetCustomAttribute<HotfixServiceAttribute>();
        var lifecycle = type.GetCustomAttribute<HotfixLifecycleAttribute>();

        if (service is not null && lifecycle is not null)
        {
            diagnostics.Add($"Hotfix type '{type.FullName}' must not use both [HotfixService] and [HotfixLifecycle].");
            binding = null;
            return false;
        }

        binding = service is not null
            ? new HotfixServiceBindingDescriptor(service.ContractType, HotfixServiceBindingKind.Service)
            : lifecycle is not null
                ? new HotfixServiceBindingDescriptor(lifecycle.ContractType, HotfixServiceBindingKind.Lifecycle)
                : null;
        return true;
    }

    private static void ScanBehaviorType(
        Type behaviorType,
        Type stateType,
        List<HotfixMethodBinding> methods,
        List<string> diagnostics,
        HashSet<HotfixMethodKey> keys)
    {
        foreach (var method in behaviorType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!method.IsDefined(typeof(ExtensionAttribute), inherit: false))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType != stateType)
            {
                diagnostics.Add($"Hotfix method '{behaviorType.FullName}.{method.Name}' must start with 'this {stateType.FullName} self'.");
                continue;
            }

            if (method.ContainsGenericParameters)
            {
                diagnostics.Add($"Hotfix method '{behaviorType.FullName}.{method.Name}' must not be generic.");
                continue;
            }

            if (method.ReturnType.ContainsGenericParameters
                || parameters.Any(static parameter => parameter.ParameterType.ContainsGenericParameters))
            {
                diagnostics.Add($"Hotfix method '{behaviorType.FullName}.{method.Name}' must not use open generic return or parameter types.");
                continue;
            }

            if (parameters.Any(static parameter => parameter.IsOut || parameter.ParameterType.IsByRef || parameter.ParameterType.IsPointer))
            {
                diagnostics.Add($"Hotfix method '{behaviorType.FullName}.{method.Name}' must not use by-ref, out, or pointer parameter types.");
                continue;
            }

            var argumentTypes = parameters.Skip(1).Select(static parameter => parameter.ParameterType).ToArray();
            var key = new HotfixMethodKey(
                stateType.FullName ?? stateType.Name,
                method.Name,
                method.ReturnType.FullName ?? method.ReturnType.Name,
                argumentTypes.Select(static type => type.FullName ?? type.Name).ToArray());

            if (!keys.Add(key))
            {
                diagnostics.Add($"Duplicate hotfix method key '{key}'.");
                continue;
            }

            methods.Add(new HotfixMethodBinding(key, method, stateType, method.ReturnType, argumentTypes));
        }
    }

    private static void ScanServiceType(
        Type serviceType,
        HotfixServiceBindingDescriptor binding,
        List<HotfixServiceMethodBinding> services,
        List<string> diagnostics,
        HashSet<string> serviceKeys)
    {
        var contractType = binding.ContractType;
        if (serviceType.IsAbstract || serviceType.IsInterface)
        {
            diagnostics.Add($"Hotfix service '{serviceType.FullName}' must be a concrete class.");
            return;
        }

        foreach (var method in serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (method.ContainsGenericParameters)
            {
                diagnostics.Add($"Hotfix service method '{serviceType.FullName}.{method.Name}' must not be generic.");
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                diagnostics.Add($"Hotfix service method '{serviceType.FullName}.{method.Name}' must have exactly one argument.");
                continue;
            }

            if (method.ReturnType != typeof(ValueTask) && !IsValueTaskResult(method.ReturnType))
            {
                diagnostics.Add($"Hotfix service method '{serviceType.FullName}.{method.Name}' must return ValueTask or ValueTask<TResult>.");
                continue;
            }

            if (!method.IsStatic && serviceType.GetConstructor(Type.EmptyTypes) is null)
            {
                diagnostics.Add($"Hotfix service '{serviceType.FullName}' must have a public parameterless constructor for instance dispatch.");
                continue;
            }

            var returnType = method.ReturnType == typeof(ValueTask)
                ? typeof(ValueTask)
                : method.ReturnType.GetGenericArguments()[0];
            var parameterTypes = parameters.Select(static parameter => parameter.ParameterType).ToArray();
            var contractParameterTypes = new Type[parameterTypes.Length];
            var invalidParameter = false;
            for (var index = 0; index < parameterTypes.Length; index++)
            {
                if (!TryGetContractParameterType(binding.Kind, serviceType, method, parameterTypes[index], diagnostics, out var contractParameterType))
                {
                    invalidParameter = true;
                    break;
                }

                contractParameterTypes[index] = contractParameterType;
            }

            if (invalidParameter)
            {
                continue;
            }

            var contractMethod = ResolveContractMethod(contractType, method, contractParameterTypes, diagnostics);
            if (contractMethod is null)
            {
                continue;
            }

            if (!TryGetRpcMethodId(contractMethod, out var methodId))
            {
                diagnostics.Add($"Hotfix service method '{serviceType.FullName}.{method.Name}' maps to contract method '{contractType.FullName}.{contractMethod.Name}' without [RpcMethod].");
                continue;
            }

            var key = HotfixDispatch.CreateServiceKey(contractType, methodId, returnType, parameterTypes);
            if (!serviceKeys.Add(key))
            {
                diagnostics.Add($"Duplicate hotfix service method key '{key}'.");
                continue;
            }

            services.Add(new HotfixServiceMethodBinding(key, method, serviceType, contractType, returnType, parameterTypes));
        }
    }

    private static bool IsValueTaskResult(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);
    }

    private static bool TryGetRpcMethodId(MethodInfo method, out int methodId)
    {
        var rpcMethod = method.GetCustomAttribute<RpcMethodAttribute>();
        if (rpcMethod is not null)
        {
            methodId = rpcMethod.MethodId;
            return true;
        }

        foreach (var attribute in method.CustomAttributes)
        {
            if (!string.Equals(
                    attribute.AttributeType.FullName,
                    typeof(RpcMethodAttribute).FullName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Count == 1 &&
                attribute.ConstructorArguments[0].Value is int id)
            {
                methodId = id;
                return true;
            }
        }

        methodId = 0;
        return false;
    }

    private static bool TryGetContractParameterType(
        HotfixServiceBindingKind bindingKind,
        Type serviceType,
        MethodInfo method,
        Type parameterType,
        List<string> diagnostics,
        out Type contractParameterType)
    {
        contractParameterType = parameterType;
        if (!parameterType.IsGenericType)
        {
            return true;
        }

        var genericDefinition = parameterType.GetGenericTypeDefinition();
        if (genericDefinition.Namespace != "Lakona.Game.Server.Hotfix")
        {
            return true;
        }

        var name = genericDefinition.Name;
        var isServiceCall = name is "HotfixServiceCall`1" or "HotfixServiceCall`2";
        var isLifecycleCall = name is "HotfixLifecycleCall`1";
        if (!isServiceCall && !isLifecycleCall)
        {
            return true;
        }

        if (bindingKind == HotfixServiceBindingKind.Lifecycle && !isLifecycleCall)
        {
            diagnostics.Add($"Hotfix lifecycle method '{serviceType.FullName}.{method.Name}' must use HotfixLifecycleCall<TRequest>.");
            return false;
        }

        if (bindingKind == HotfixServiceBindingKind.Service && !isServiceCall)
        {
            diagnostics.Add($"Hotfix service method '{serviceType.FullName}.{method.Name}' must use HotfixServiceCall<TRequest> or HotfixServiceCall<TRequest, TCallback>.");
            return false;
        }

        contractParameterType = parameterType.GetGenericArguments()[0];
        return true;
    }

    private static MethodInfo? ResolveContractMethod(
        Type contractType,
        MethodInfo serviceMethod,
        IReadOnlyList<Type> parameterTypes,
        List<string> diagnostics)
    {
        var matches = contractType.GetMethods()
            .Where(method => string.Equals(method.Name, serviceMethod.Name, StringComparison.Ordinal))
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == parameterTypes.Count &&
                       parameters.Zip(parameterTypes).All(pair => pair.First.ParameterType == pair.Second);
            })
            .ToArray();

        if (matches.Length == 1)
        {
            return matches[0];
        }

        diagnostics.Add(matches.Length == 0
            ? $"Hotfix service method '{serviceMethod.DeclaringType?.FullName}.{serviceMethod.Name}' does not match a method on contract '{contractType.FullName}'."
            : $"Hotfix service method '{serviceMethod.DeclaringType?.FullName}.{serviceMethod.Name}' matches more than one method on contract '{contractType.FullName}'.");
        return null;
    }

    private sealed record HotfixServiceBindingDescriptor(
        Type ContractType,
        HotfixServiceBindingKind Kind);

    private enum HotfixServiceBindingKind
    {
        Service,
        Lifecycle
    }
}
