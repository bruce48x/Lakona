using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Rpc.Core;
using Microsoft.Extensions.DependencyInjection;

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
        var actorMethods = new List<HotfixActorMethodDescriptor>();
        var actorStartups = new List<ActorStartupDeclaration>();
        var actorPlacements = new List<ActorPlacementDeclaration>();
        var actorLifecycles = new Dictionary<Type, ActorLifecycleMethods>();
        var startupServices = new List<ServiceDescriptor>();
        var diagnostics = new List<string>();
        var keys = new HashSet<HotfixMethodKey>();
        var actorMethodKeys = new HashSet<string>(StringComparer.Ordinal);
        var serviceKeys = new HashSet<string>(StringComparer.Ordinal);
        var startupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var startupActors = new HashSet<Type>();
        var invalidStartupActors = new HashSet<Type>();
        var placementActors = new HashSet<Type>();
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

                var behaviorActorType = GetHotfixBehaviorActorType(type);
                if (behaviorActorType is not null)
                {
                    if (!type.IsAbstract || !type.IsSealed)
                    {
                        diagnostics.Add($"Hotfix behavior '{type.FullName}' must be a static class.");
                        continue;
                    }

                    ScanBehaviorType(
                        type,
                        behaviorActorType,
                        methods,
                        actorMethods,
                        actorLifecycles,
                        diagnostics,
                        keys,
                        actorMethodKeys);
                }

                var isStartupType = IsHotfixStartupType(type);
                if (!isStartupType && HasHotfixStartupMethodAttribute(type))
                {
                    var actorsAttribute = GetAttributeName(typeof(HotfixConfigureActorsAttribute));
                    var servicesAttribute = GetAttributeName(typeof(HotfixConfigureServicesAttribute));
                    var startupAttribute = GetAttributeName(typeof(HotfixStartupAttribute));
                    diagnostics.Add($"Hotfix startup method attributes [{actorsAttribute}] and [{servicesAttribute}] on '{type.FullName}' require [{startupAttribute}] on the containing type.");
                }

                if (isStartupType)
                {
                    ScanHotfixStartupType(
                        type,
                        actorStartups,
                        actorPlacements,
                        startupServices,
                        diagnostics,
                        startupNames,
                        startupActors,
                        invalidStartupActors,
                        placementActors);
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

        return new HotfixBehaviorScanResult(
            methods,
            services,
            actorMethods,
            actorStartups,
            actorPlacements,
            actorLifecycles
                .OrderBy(static item => item.Key.FullName, StringComparer.Ordinal)
                .Select(static item => item.Value.ToDescriptor(item.Key))
                .ToArray(),
            startupServices,
            diagnostics);
    }

    private static bool IsHotfixStartupType(Type type)
    {
        return HasAttribute(type, typeof(HotfixStartupAttribute));
    }

    private static bool HasHotfixStartupMethodAttribute(Type type)
    {
        return type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Any(method =>
                HasAttribute(method, typeof(HotfixConfigureActorsAttribute)) ||
                HasAttribute(method, typeof(HotfixConfigureServicesAttribute)));
    }

    private static void ScanHotfixStartupType(
        Type startupType,
        List<ActorStartupDeclaration> actorStartups,
        List<ActorPlacementDeclaration> actorPlacements,
        List<ServiceDescriptor> startupServices,
        List<string> diagnostics,
        HashSet<string> startupNames,
        HashSet<Type> startupActors,
        HashSet<Type> invalidStartupActors,
        HashSet<Type> placementActors)
    {
        if (!startupType.IsVisible || !startupType.IsAbstract || !startupType.IsSealed)
        {
            diagnostics.Add($"Hotfix startup '{startupType.FullName}' must be a public static class.");
            return;
        }

        var actorsMethod = ResolveHotfixStartupMethod(
            startupType,
            typeof(HotfixConfigureActorsAttribute),
            typeof(ActorHostBuilder),
            diagnostics);
        if (actorsMethod is not null)
        {
            var builder = new ActorHostBuilder();
            if (TryInvokeHotfixStartupMethod(startupType, actorsMethod, builder, diagnostics))
            {
                foreach (var startup in builder.Startups)
                {
                    if (!startup.IsLegacy)
                    {
                        var actorType = startup.ActorType!;
                        if (!startupActors.Add(actorType))
                        {
                            invalidStartupActors.Add(actorType);
                            actorStartups.RemoveAll(item => item.ActorType == actorType);
                            diagnostics.Add($"Duplicate actor startup for '{actorType.FullName}'.");
                            continue;
                        }

                        if (!invalidStartupActors.Contains(actorType))
                        {
                            actorStartups.Add(startup);
                        }

                        continue;
                    }

                    if (!startupNames.Add(startup.Name!))
                    {
                        diagnostics.Add($"Duplicate actor startup name '{startup.Name}'.");
                        continue;
                    }

                    actorStartups.Add(startup);
                }

                foreach (var placement in builder.Placements)
                {
                    if (!placementActors.Add(placement.ActorType))
                    {
                        diagnostics.Add($"Duplicate actor placement for '{placement.ActorType.FullName}'.");
                        continue;
                    }

                    actorPlacements.Add(placement);
                }
            }
        }

        var servicesMethod = ResolveHotfixStartupMethod(
            startupType,
            typeof(HotfixConfigureServicesAttribute),
            typeof(IServiceCollection),
            diagnostics);
        if (servicesMethod is not null)
        {
            var services = new ServiceCollection();
            if (TryInvokeHotfixStartupMethod(startupType, servicesMethod, services, diagnostics))
            {
                startupServices.AddRange(services);
            }
        }
    }

    private static MethodInfo? ResolveHotfixStartupMethod(
        Type startupType,
        Type attributeType,
        Type parameterType,
        List<string> diagnostics)
    {
        var methods = startupType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => HasAttribute(method, attributeType))
            .ToArray();

        if (methods.Length == 0)
        {
            return null;
        }

        if (methods.Length != 1)
        {
            diagnostics.Add($"Hotfix startup '{startupType.FullName}' must declare at most one [{GetAttributeName(attributeType)}] public static void method with one {parameterType.Name} parameter.");
            return null;
        }

        var method = methods[0];
        var parameters = method.GetParameters();
        if (!method.IsPublic ||
            !method.IsStatic ||
            method.IsGenericMethod ||
            method.ReturnType != typeof(void) ||
            parameters.Length != 1 ||
            parameters[0].ParameterType != parameterType)
        {
            diagnostics.Add($"Hotfix startup '{startupType.FullName}' method marked [{GetAttributeName(attributeType)}] must be public static void with one {parameterType.Name} parameter.");
            return null;
        }

        return method;
    }

    private static bool HasAttribute(MemberInfo member, Type attributeType)
    {
        return member.CustomAttributes.Any(attribute => string.Equals(
            attribute.AttributeType.FullName,
            attributeType.FullName,
            StringComparison.Ordinal));
    }

    private static string GetAttributeName(Type attributeType)
    {
        return attributeType.Name.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeType.Name[..^"Attribute".Length]
            : attributeType.Name;
    }

    private static bool TryInvokeHotfixStartupMethod(
        Type startupType,
        MethodInfo method,
        object argument,
        List<string> diagnostics)
    {
        try
        {
            method.Invoke(null, [argument]);
            return true;
        }
        catch (TargetInvocationException ex)
        {
            diagnostics.Add($"Hotfix startup '{startupType.FullName}' {method.Name} failed: {ex.InnerException?.Message ?? ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Hotfix startup '{startupType.FullName}' {method.Name} failed: {ex.Message}");
            return false;
        }
    }

    private static Type? GetHotfixBehaviorActorType(Type type)
    {
        var loadContext = AssemblyLoadContext.GetLoadContext(type.Assembly);
        if (loadContext is not null)
        {
            loadContext.Resolving += ResolveLoadedAssembly;
        }

        try
        {
            foreach (var attribute in type.CustomAttributes)
            {
                if (!string.Equals(
                        attribute.AttributeType.FullName,
                        typeof(HotfixBehaviorOfAttribute).FullName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Count == 1 &&
                    attribute.ConstructorArguments[0].Value is Type actorType)
                {
                    return actorType;
                }
            }

            return null;
        }
        finally
        {
            if (loadContext is not null)
            {
                loadContext.Resolving -= ResolveLoadedAssembly;
            }
        }
    }

    private static Assembly? ResolveLoadedAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => AssemblyName.ReferenceMatchesDefinition(assemblyName, assembly.GetName()));
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
        List<HotfixActorMethodDescriptor> actorMethods,
        Dictionary<Type, ActorLifecycleMethods> actorLifecycles,
        List<string> diagnostics,
        HashSet<HotfixMethodKey> keys,
        HashSet<string> actorMethodKeys)
    {
        var isActorBehavior = IsLakonaActorType(stateType);
        var loadContext = AssemblyLoadContext.GetLoadContext(behaviorType.Assembly);
        if (loadContext is not null)
        {
            loadContext.Resolving += ResolveLoadedAssembly;
        }

        try
        {
            if (isActorBehavior)
            {
                ScanActorLifecycleMethods(behaviorType, stateType, actorLifecycles, diagnostics);
            }
            else
            {
                RejectActorLifecycleMethodsOnNonActorBehavior(behaviorType, stateType, diagnostics);
            }

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

                if (isActorBehavior)
                {
                    ScanActorApiMethod(behaviorType, stateType, method, actorMethods, diagnostics, actorMethodKeys);
                }
            }
        }
        finally
        {
            if (loadContext is not null)
            {
                loadContext.Resolving -= ResolveLoadedAssembly;
            }
        }
    }

    private static void ScanActorLifecycleMethods(
        Type behaviorType,
        Type actorType,
        Dictionary<Type, ActorLifecycleMethods> actorLifecycles,
        List<string> diagnostics)
    {
        foreach (var method in behaviorType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var isStart = method.IsDefined(typeof(ActorStartAttribute), inherit: false);
            var isStop = method.IsDefined(typeof(ActorStopAttribute), inherit: false);
            if (!isStart && !isStop)
            {
                continue;
            }

            if (isStart && isStop)
            {
                diagnostics.Add($"Hotfix actor lifecycle method '{behaviorType.FullName}.{method.Name}' must not use both [ActorStart] and [ActorStop].");
                continue;
            }

            var expectedCallType = isStart ? typeof(ActorStartCall) : typeof(ActorStopCall);
            if (!IsValidActorLifecycleMethod(method, actorType, expectedCallType))
            {
                diagnostics.Add($"Hotfix actor lifecycle method '{behaviorType.FullName}.{method.Name}' must be public static ValueTask with parameters ({actorType.FullName} actor, {expectedCallType.Name} call).");
                continue;
            }

            if (!actorLifecycles.TryGetValue(actorType, out var lifecycle))
            {
                lifecycle = new ActorLifecycleMethods();
                actorLifecycles.Add(actorType, lifecycle);
            }

            if (isStart)
            {
                if (lifecycle.StartMethod is not null)
                {
                    diagnostics.Add($"Duplicate [ActorStart] method for actor '{actorType.FullName}'.");
                    continue;
                }

                lifecycle.StartMethod = method;
                continue;
            }

            if (lifecycle.StopMethod is not null)
            {
                diagnostics.Add($"Duplicate [ActorStop] method for actor '{actorType.FullName}'.");
                continue;
            }

            lifecycle.StopMethod = method;
        }
    }

    private static void RejectActorLifecycleMethodsOnNonActorBehavior(
        Type behaviorType,
        Type stateType,
        List<string> diagnostics)
    {
        foreach (var method in behaviorType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (!method.IsDefined(typeof(ActorStartAttribute), inherit: false) &&
                !method.IsDefined(typeof(ActorStopAttribute), inherit: false))
            {
                continue;
            }

            diagnostics.Add($"Hotfix actor lifecycle method '{behaviorType.FullName}.{method.Name}' requires actor type '{stateType.FullName}' to implement Lakona.Game.Server.Actors.IActor.");
        }
    }

    private static bool IsValidActorLifecycleMethod(
        MethodInfo method,
        Type actorType,
        Type callType)
    {
        var parameters = method.GetParameters();
        return method.IsStatic &&
            !method.IsGenericMethod &&
            method.ReturnType == typeof(ValueTask) &&
            parameters.Length == 2 &&
            parameters[0].ParameterType == actorType &&
            parameters[1].ParameterType == callType;
    }

    private static void ScanActorApiMethod(
        Type behaviorType,
        Type actorType,
        MethodInfo method,
        List<HotfixActorMethodDescriptor> actorMethods,
        List<string> diagnostics,
        HashSet<string> actorMethodKeys)
    {
        var parameters = method.GetParameters();
        if (parameters.Length is not 2 and not 3 ||
            parameters[0].ParameterType != actorType ||
            parameters[1].ParameterType == typeof(CancellationToken) ||
            parameters[1].ParameterType.ContainsGenericParameters)
        {
            diagnostics.Add($"Hotfix behavior '{behaviorType.FullName}' actor API method '{method.Name}' must use 'this {actorType.FullName} self, Request request, optional CancellationToken'.");
            return;
        }

        var hasCancellationToken = parameters.Length == 3;
        if (hasCancellationToken && parameters[2].ParameterType != typeof(CancellationToken))
        {
            diagnostics.Add($"Hotfix behavior '{behaviorType.FullName}' actor API method '{method.Name}' optional third parameter must be {typeof(CancellationToken).FullName}.");
            return;
        }

        if (method.ReturnType != typeof(ValueTask) && !IsValueTaskResult(method.ReturnType))
        {
            diagnostics.Add($"Hotfix behavior '{behaviorType.FullName}' actor API method '{method.Name}' must return ValueTask or ValueTask<TResult>.");
            return;
        }

        var requestType = parameters[1].ParameterType;
        if (ContainsTypeFromAssembly(requestType, behaviorType.Assembly))
        {
            diagnostics.Add($"Hotfix behavior '{behaviorType.FullName}' actor API method '{method.Name}' uses request type '{requestType.FullName ?? requestType.Name}' from the hotfix assembly; request DTOs must live outside hotfix code.");
            return;
        }

        var resultType = method.ReturnType == typeof(ValueTask)
            ? null
            : method.ReturnType.GetGenericArguments()[0];
        if (resultType is not null && ContainsTypeFromAssembly(resultType, behaviorType.Assembly))
        {
            diagnostics.Add($"Hotfix behavior '{behaviorType.FullName}' actor API method '{method.Name}' uses result type '{resultType.FullName ?? resultType.Name}' from the hotfix assembly; result DTOs must live outside hotfix code.");
            return;
        }

        var actorTypeIdentity = HotfixActorApiMetadata.CreateTypeIdentity(actorType);
        var requestTypeIdentity = HotfixActorApiMetadata.CreateTypeIdentity(requestType);
        var resultTypeIdentity = resultType is null
            ? HotfixActorApiMetadata.VoidResultType
            : HotfixActorApiMetadata.CreateTypeIdentity(resultType);
        var methodKey = HotfixActorApiMetadata.CreateMethodKey(
            actorTypeIdentity,
            method.Name,
            requestTypeIdentity,
            resultTypeIdentity);

        if (!actorMethodKeys.Add(methodKey))
        {
            diagnostics.Add($"Hotfix behavior '{behaviorType.FullName}' duplicate canonical actor API method key '{methodKey}' for method '{method.Name}'.");
            return;
        }

        actorMethods.Add(new HotfixActorMethodDescriptor(
            methodKey,
            actorType,
            method.Name,
            requestType,
            resultType,
            method,
            hasCancellationToken));
    }

    private static bool IsLakonaActorType(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (IsLakonaActorTypeName(current))
            {
                return true;
            }

            foreach (var interfaceType in current.GetInterfaces())
            {
                if (IsLakonaActorTypeName(interfaceType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsLakonaActorTypeName(Type type)
    {
        var fullName = type.IsGenericType ? type.GetGenericTypeDefinition().FullName : type.FullName;
        if (!string.Equals(type.Assembly.GetName().Name, "Lakona.Game.Server", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(fullName, "Lakona.Game.Server.Actors.IActor", StringComparison.Ordinal) ||
            string.Equals(fullName, "Lakona.Game.Server.Actors.Actor", StringComparison.Ordinal) ||
            string.Equals(fullName, "Lakona.Game.Server.Actors.Actor`1", StringComparison.Ordinal);
    }

    private static bool ContainsTypeFromAssembly(Type type, Assembly assembly)
    {
        if (type.Assembly == assembly)
        {
            return true;
        }

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            return ContainsTypeFromAssembly(elementType, assembly);
        }

        return type.IsGenericType &&
            type.GetGenericArguments().Any(argument => ContainsTypeFromAssembly(argument, assembly));
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

        if (serviceType.ContainsGenericParameters)
        {
            diagnostics.Add($"Hotfix service '{serviceType.FullName}' must not be an open generic type.");
            return;
        }

        var declaredMethods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (declaredMethods.Any(static method => !method.IsStatic) &&
            !ValidateServiceConstructors(serviceType, diagnostics))
        {
            return;
        }

        foreach (var method in declaredMethods)
        {
            if (IsDisposalMethod(method))
            {
                continue;
            }

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

            var returnType = method.ReturnType == typeof(ValueTask)
                ? typeof(ValueTask)
                : method.ReturnType.GetGenericArguments()[0];
            var parameterTypes = parameters.Select(static parameter => parameter.ParameterType).ToArray();
            if (!method.IsStatic && !IsSupportedInstanceCallParameter(binding.Kind, parameterTypes[0]))
            {
                diagnostics.Add(binding.Kind == HotfixServiceBindingKind.Lifecycle
                    ? $"Hotfix lifecycle method '{serviceType.FullName}.{method.Name}' must use HotfixLifecycleCall<TRequest> for instance dispatch; static methods may use raw request DTO parameters."
                    : $"Hotfix service method '{serviceType.FullName}.{method.Name}' must use HotfixServiceCall<TRequest> or HotfixServiceCall<TRequest, TCallback> for instance dispatch; static methods may use raw request DTO parameters.");
                continue;
            }

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

    private static bool ValidateServiceConstructors(Type serviceType, List<string> diagnostics)
    {
        var publicConstructors = serviceType.GetConstructors();
        if (publicConstructors.Length == 0)
        {
            diagnostics.Add($"Hotfix service '{serviceType.FullName}' must have a public constructor for instance dispatch.");
            return false;
        }

        var markedConstructors = publicConstructors
            .Where(static constructor => constructor.IsDefined(typeof(ActivatorUtilitiesConstructorAttribute), inherit: false))
            .ToArray();
        if (markedConstructors.Length > 1)
        {
            diagnostics.Add($"Hotfix service '{serviceType.FullName}' must not mark more than one public constructor with [ActivatorUtilitiesConstructor].");
            return false;
        }

        if (markedConstructors.Length == 0 && publicConstructors.Length > 1)
        {
            diagnostics.Add($"Hotfix service '{serviceType.FullName}' has multiple public constructors; mark the intended constructor with [ActivatorUtilitiesConstructor].");
            return false;
        }

        return true;
    }

    private static bool IsValueTaskResult(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);
    }

    private static bool IsDisposalMethod(MethodInfo method)
    {
        if (method.Name == nameof(IDisposable.Dispose) &&
            method.ReturnType == typeof(void) &&
            method.GetParameters().Length == 0)
        {
            return true;
        }

        return method.Name == nameof(IAsyncDisposable.DisposeAsync) &&
            method.ReturnType == typeof(ValueTask) &&
            method.GetParameters().Length == 0;
    }

    private static bool IsSupportedInstanceCallParameter(
        HotfixServiceBindingKind bindingKind,
        Type parameterType)
    {
        if (typeof(IHotfixCallContext).IsAssignableFrom(parameterType))
        {
            return true;
        }

        if (!parameterType.IsGenericType)
        {
            return false;
        }

        var genericDefinition = parameterType.GetGenericTypeDefinition();
        if (genericDefinition.Namespace != "Lakona.Game.Server.Hotfix")
        {
            return false;
        }

        var name = genericDefinition.Name;
        return bindingKind == HotfixServiceBindingKind.Lifecycle
            ? name is "HotfixLifecycleCall`1"
            : name is "HotfixServiceCall`1" or "HotfixServiceCall`2";
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

    private sealed class ActorLifecycleMethods
    {
        public MethodInfo? StartMethod { get; set; }

        public MethodInfo? StopMethod { get; set; }

        public HotfixActorLifecycleDescriptor ToDescriptor(Type actorType)
        {
            return new HotfixActorLifecycleDescriptor(actorType, StartMethod, StopMethod);
        }
    }

    private enum HotfixServiceBindingKind
    {
        Service,
        Lifecycle
    }
}
