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

        return Scan(assemblies, includedTypes: null);
    }

    public static HotfixBehaviorScanResult Scan(Assembly assembly, IReadOnlyList<Type> includedTypes)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(includedTypes);

        return Scan([assembly], includedTypes);
    }

    private static HotfixBehaviorScanResult Scan(IReadOnlyList<Assembly> assemblies, IReadOnlyList<Type>? includedTypes)
    {
        var methods = new List<HotfixMethodBinding>();
        var services = new List<HotfixServiceMethodBinding>();
        var diagnostics = new List<string>();
        var keys = new HashSet<HotfixMethodKey>();
        var serviceKeys = new HashSet<string>(StringComparer.Ordinal);
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

                var service = type.GetCustomAttribute<HotfixServiceAttribute>();
                if (service is not null)
                {
                    ScanServiceType(type, service.ContractType, services, diagnostics, serviceKeys);
                }
            }
        }

        return new HotfixBehaviorScanResult(methods, services, diagnostics);
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
        Type contractType,
        List<HotfixServiceMethodBinding> services,
        List<string> diagnostics,
        HashSet<string> serviceKeys)
    {
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
            var contractMethod = ResolveContractMethod(contractType, method, parameterTypes, diagnostics);
            if (contractMethod is null)
            {
                continue;
            }

            var rpcMethod = contractMethod.GetCustomAttribute<RpcMethodAttribute>();
            if (rpcMethod is null)
            {
                diagnostics.Add($"Hotfix service method '{serviceType.FullName}.{method.Name}' maps to contract method '{contractType.FullName}.{contractMethod.Name}' without [RpcMethod].");
                continue;
            }

            var key = HotfixDispatch.CreateServiceKey(contractType, rpcMethod.MethodId, returnType, parameterTypes);
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
}
