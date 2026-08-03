using System.Collections.Concurrent;
using System.Reflection;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixDispatchTable : IDisposable, IAsyncDisposable
{
    private readonly IReadOnlyDictionary<HotfixMethodKey, HotfixMethodBinding> bindings;
    private readonly IReadOnlyDictionary<ServiceMethodKey, HotfixServiceMethodBinding> serviceMethodBindings;
    private readonly IReadOnlyList<HotfixHttpEndpointMethodBinding> httpEndpointBindings;
    private readonly IReadOnlyDictionary<string, HotfixActorMethodDescriptor> actorMethodBindings;
    private readonly IReadOnlyDictionary<ulong, HotfixActorMethodDescriptor> actorMethodIdBindings;
    private readonly IReadOnlyDictionary<Type, HotfixActorLifecycleDescriptor> actorLifecycleBindings;
    private readonly IReadOnlyDictionary<ulong, HotfixTimerMethodDescriptor> timerMethodBindings;
    private readonly IReadOnlyDictionary<MethodInfo, HotfixTimerMethodDescriptor> timerMethodDelegateBindings;
    private readonly IReadOnlyDictionary<Type, ObjectFactory> moduleActivationFactories;
    private readonly IReadOnlyList<Type> moduleTypes;
    private readonly ConcurrentDictionary<DelegateCacheKey, Delegate> delegates = new();
    private readonly ConcurrentDictionary<ServiceDelegateCacheKey, Delegate> serviceDelegates = new();
    private readonly ConcurrentDictionary<HttpEndpointDelegateCacheKey, Delegate> httpEndpointDelegates = new();
    private readonly object moduleActivationGate = new();
    private IReadOnlyDictionary<Type, object> moduleInstances = new Dictionary<Type, object>();
    private IReadOnlyList<object> moduleInstanceDisposalOrder = Array.Empty<object>();
    private int modulesActivated;
    private int disposed;

    public HotfixDispatchTable(long version, IEnumerable<HotfixMethodBinding> methods)
        : this(
            version,
            methods,
            Array.Empty<HotfixServiceMethodBinding>())
    {
    }

    public HotfixDispatchTable(
        long version,
        IEnumerable<HotfixMethodBinding> methods,
        IEnumerable<HotfixServiceMethodBinding> services)
        : this(version, methods, services, Array.Empty<HotfixActorMethodDescriptor>())
    {
    }

    public HotfixDispatchTable(
        long version,
        IEnumerable<HotfixMethodBinding> methods,
        IEnumerable<HotfixServiceMethodBinding> services,
        IEnumerable<HotfixActorMethodDescriptor> actorMethods)
        : this(version, methods, services, actorMethods, Array.Empty<HotfixActorLifecycleDescriptor>())
    {
    }

    public HotfixDispatchTable(
        long version,
        IEnumerable<HotfixMethodBinding> methods,
        IEnumerable<HotfixServiceMethodBinding> services,
        IEnumerable<HotfixActorMethodDescriptor> actorMethods,
        IEnumerable<HotfixActorLifecycleDescriptor> actorLifecycles)
        : this(version, methods, services, actorMethods, actorLifecycles, Array.Empty<HotfixTimerMethodDescriptor>())
    {
    }

    public HotfixDispatchTable(
        long version,
        IEnumerable<HotfixMethodBinding> methods,
        IEnumerable<HotfixServiceMethodBinding> services,
        IEnumerable<HotfixActorMethodDescriptor> actorMethods,
        IEnumerable<HotfixActorLifecycleDescriptor> actorLifecycles,
        IEnumerable<HotfixTimerMethodDescriptor> timerMethods,
        IEnumerable<HotfixHttpEndpointMethodBinding>? httpEndpoints = null)
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(actorMethods);
        ArgumentNullException.ThrowIfNull(actorLifecycles);
        ArgumentNullException.ThrowIfNull(timerMethods);

        var methodList = new List<HotfixMethodBinding>();
        foreach (var method in methods)
        {
            if (method is null)
            {
                throw new ArgumentException("Method bindings cannot contain null.", nameof(methods));
            }

            methodList.Add(method);
        }

        var serviceList = new List<HotfixServiceMethodBinding>();
        foreach (var service in services)
        {
            if (service is null)
            {
                throw new ArgumentException("Service bindings cannot contain null.", nameof(services));
            }

            serviceList.Add(service);
        }

        var actorMethodList = new List<HotfixActorMethodDescriptor>();
        foreach (var actorMethod in actorMethods)
        {
            if (actorMethod is null)
            {
                throw new ArgumentException("Actor method descriptors cannot contain null.", nameof(actorMethods));
            }

            actorMethodList.Add(actorMethod);
        }

        var actorLifecycleList = new List<HotfixActorLifecycleDescriptor>();
        foreach (var actorLifecycle in actorLifecycles)
        {
            if (actorLifecycle is null)
            {
                throw new ArgumentException("Actor lifecycle descriptors cannot contain null.", nameof(actorLifecycles));
            }

            actorLifecycleList.Add(actorLifecycle);
        }

        var timerMethodList = new List<HotfixTimerMethodDescriptor>();
        foreach (var timerMethod in timerMethods)
        {
            if (timerMethod is null)
            {
                throw new ArgumentException("Timer method descriptors cannot contain null.", nameof(timerMethods));
            }

            timerMethodList.Add(timerMethod);
        }

        var httpEndpointList = new List<HotfixHttpEndpointMethodBinding>();
        foreach (var httpEndpoint in httpEndpoints ?? Array.Empty<HotfixHttpEndpointMethodBinding>())
        {
            if (httpEndpoint is null)
            {
                throw new ArgumentException(
                    "HTTP endpoint bindings cannot contain null.",
                    nameof(httpEndpoints));
            }

            httpEndpointList.Add(httpEndpoint);
        }

        httpEndpointList.Sort(static (left, right) =>
        {
            var service = StringComparer.OrdinalIgnoreCase.Compare(
                left.Endpoint.Service,
                right.Endpoint.Service);
            if (service != 0)
            {
                return service;
            }

            var method = StringComparer.OrdinalIgnoreCase.Compare(
                left.Endpoint.Method,
                right.Endpoint.Method);
            return method != 0
                ? method
                : StringComparer.OrdinalIgnoreCase.Compare(
                    left.Endpoint.RoutePattern,
                    right.Endpoint.RoutePattern);
        });

        Version = version;
        bindings = methodList.ToDictionary(static method => method.Key, static method => method);
        serviceMethodBindings = serviceList.ToDictionary(
            static service => new ServiceMethodKey(service.ContractType, service.MethodId),
            static service => service);
        httpEndpointBindings = httpEndpointList;
        actorMethodBindings = actorMethodList.ToDictionary(static method => method.MethodKey, static method => method, StringComparer.Ordinal);
        actorMethodIdBindings = CreateActorMethodIdBindings(actorMethodList);
        actorLifecycleBindings = actorLifecycleList.ToDictionary(static lifecycle => lifecycle.ActorType, static lifecycle => lifecycle);
        timerMethodBindings = CreateTimerMethodIdBindings(timerMethodList);
        timerMethodDelegateBindings = timerMethodList.ToDictionary(static method => method.Method);
        moduleActivationFactories = serviceList
            .Where(static service => !service.Method.IsStatic)
            .Select(static service => service.ServiceType)
            .Concat(httpEndpointList.Select(static endpoint => endpoint.ServiceType))
            .Concat(methodList.Select(static method => method.BehaviorType))
            .Concat(actorMethodList.Select(static method => method.BehaviorType))
            .Concat(actorLifecycleList.Select(static lifecycle => lifecycle.BehaviorType))
            .Concat(timerMethodList.Select(static method => method.CallbackType))
            .Distinct()
            .ToDictionary(
                static serviceType => serviceType,
                static serviceType => ActivatorUtilities.CreateFactory(serviceType, Type.EmptyTypes));
        moduleTypes = moduleActivationFactories.Keys
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        ActorTypes = actorMethodList
            .Select(static method => method.ActorType)
            .Concat(actorLifecycleList.Select(static lifecycle => lifecycle.ActorType))
            .Distinct()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        MethodKeys = bindings.Keys.OrderBy(static key => key.ToString(), StringComparer.Ordinal).ToArray();
        HttpEndpoints = httpEndpointList
            .Select(static endpoint => endpoint.Endpoint)
            .ToArray();
    }

    public long Version { get; }

    public IReadOnlyList<HotfixMethodKey> MethodKeys { get; }

    public IReadOnlyList<HotfixHttpEndpointDescriptor> HttpEndpoints { get; }

    internal IReadOnlyList<Type> ModuleTypes => moduleTypes;

    internal IReadOnlyList<Type> ActorTypes { get; }

    public MethodInfo Resolve(HotfixMethodKey key)
    {
        return ResolveBinding(key).Method;
    }

    internal HotfixMethodBinding ResolveBinding(HotfixMethodKey key)
    {
        return bindings.TryGetValue(key, out var binding)
            ? binding
            : throw new HotfixMethodNotLoadedException($"Hotfix method '{key}' is not loaded.");
    }

    public bool TryResolveActorMethod(
        string methodKey,
        out HotfixActorMethodDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodKey);
        return actorMethodBindings.TryGetValue(methodKey, out descriptor!);
    }

    public bool TryResolveActorMethod(
        ulong methodId,
        out HotfixActorMethodDescriptor descriptor)
    {
        return actorMethodIdBindings.TryGetValue(methodId, out descriptor!);
    }

    public bool TryResolveActorLifecycle(
        Type actorType,
        out HotfixActorLifecycleDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        return actorLifecycleBindings.TryGetValue(actorType, out descriptor!);
    }

    public async ValueTask<object?> InvokeActorAsync(
        string methodKey,
        object actor,
        object? request,
        Type? expectedResultType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodKey);
        cancellationToken.ThrowIfCancellationRequested();

        if (!actorMethodBindings.TryGetValue(methodKey, out var binding))
        {
            throw new HotfixMethodNotLoadedException($"Hotfix actor method '{methodKey}' is not loaded.");
        }

        return await InvokeActorBindingAsync(binding, actor, request, expectedResultType, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<object?> InvokeActorAsync(
        ulong methodId,
        object actor,
        object? request,
        Type? expectedResultType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!actorMethodIdBindings.TryGetValue(methodId, out var binding))
        {
            throw new HotfixMethodNotLoadedException($"Hotfix actor method id '{methodId}' is not loaded.");
        }

        return await InvokeActorBindingAsync(binding, actor, request, expectedResultType, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<object?> InvokeActorBindingAsync(
        HotfixActorMethodDescriptor binding,
        object actor,
        object? request,
        Type? expectedResultType,
        CancellationToken cancellationToken)
    {
        var methodKey = binding.MethodKey;

        if (!binding.ActorType.IsInstanceOfType(actor))
        {
            throw new ArgumentException(
                $"Hotfix actor method '{methodKey}' requires actor type '{binding.ActorType.FullName}'.",
                nameof(actor));
        }

        if (request is null && binding.RequestType.IsValueType ||
            request is not null && !binding.RequestType.IsInstanceOfType(request))
        {
            throw new ArgumentException(
                $"Hotfix actor method '{methodKey}' requires request type '{binding.RequestType.FullName}'.",
                nameof(request));
        }

        var actualResultType = binding.ResultType ?? typeof(void);
        if (expectedResultType is not null && expectedResultType != actualResultType)
        {
            throw new InvalidOperationException(
                $"Hotfix actor method '{methodKey}' result type '{actualResultType.FullName}' does not match expected result type '{expectedResultType.FullName}'.");
        }

        using var timerScope = HotfixDispatchRuntimeScope.EnterTimerScope();
        return await binding.Invoker.InvokeAsync(
                GetActivatedModule(binding.BehaviorType),
                actor,
                request,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryResolveTimerMethod(ulong methodId, out HotfixTimerMethodDescriptor descriptor)
    {
        return timerMethodBindings.TryGetValue(methodId, out descriptor!);
    }

    internal HotfixTimerEntry<TArgs> ResolveTimerEntry<TCallback, TArgs>(
        Func<TCallback, HotfixTimerCallback<TArgs>> selector)
        where TCallback : class
    {
        ArgumentNullException.ThrowIfNull(selector);
        var callback = selector((TCallback)GetActivatedModule(typeof(TCallback)));
        if (callback is null)
        {
            throw new ArgumentException(
                "The supplied callback selector returned null.",
                nameof(selector));
        }

        if (!timerMethodDelegateBindings.TryGetValue(callback.Method, out var descriptor) ||
            descriptor.CallbackType != typeof(TCallback) ||
            descriptor.ArgsType != typeof(TArgs))
        {
            throw new ArgumentException(
                "The supplied callback selector must directly select a generated hotfix timer method.",
                nameof(selector));
        }

        return new HotfixTimerEntry<TArgs>(
            descriptor.CallbackType.FullName ?? descriptor.CallbackType.Name,
            descriptor.MethodName,
            descriptor.MethodId);
    }

    public ValueTask InvokeTimerAsync(ulong methodId, object tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (!timerMethodBindings.TryGetValue(methodId, out var binding))
        {
            throw new HotfixMethodNotLoadedException($"Hotfix timer method id '{methodId}' is not loaded.");
        }

        var expectedTickType = typeof(Lakona.Game.Server.Hotfix.Abstractions.Timers.TimerTick<>).MakeGenericType(binding.ArgsType);
        if (!expectedTickType.IsInstanceOfType(tick))
        {
            throw new ArgumentException(
                $"Hotfix timer method '{binding.MethodKey}' requires tick type '{expectedTickType.FullName}'.",
                nameof(tick));
        }

        using var timerScope = HotfixDispatchRuntimeScope.EnterTimerScope();
        return binding.Invoker.InvokeAsync(GetActivatedModule(binding.CallbackType), tick);
    }

    public ValueTask<TResult> InvokeServiceAsync<TContract, TArg, TResult>(int methodId, TArg arg)
    {
        return InvokeServiceBindingAsync<TArg, TResult>(ResolveServiceBinding(typeof(TContract), methodId), arg);
    }

    public ValueTask InvokeServiceAsync<TContract, TArg>(int methodId, TArg arg)
    {
        return InvokeServiceBindingAsync(ResolveServiceBinding(typeof(TContract), methodId), arg);
    }

    public ValueTask<TResult> InvokeHttpAsync<TArg, TResult>(int endpointSlot, TArg arg)
    {
        if ((uint)endpointSlot >= (uint)httpEndpointBindings.Count)
        {
            throw new HotfixMethodNotLoadedException(
                $"Application HTTP endpoint slot '{endpointSlot}' is not loaded.");
        }

        var binding = httpEndpointBindings[endpointSlot];
        EnsureHttpEndpointActivation(binding, arg);
        if (binding.ArgumentType != typeof(TArg) || binding.ResultType != typeof(TResult))
        {
            throw new InvalidOperationException(
                $"Application HTTP endpoint '{binding.Endpoint.Method} {binding.Endpoint.RoutePattern}' does not match the requested typed invocation.");
        }

        var key = new HttpEndpointDelegateCacheKey(
            endpointSlot,
            typeof(TArg),
            typeof(TResult));
        var invoker = (Func<TArg, ValueTask<TResult>>)httpEndpointDelegates.GetOrAdd(
            key,
            _ => CreateHttpEndpointDelegate(
                binding,
                typeof(Func<TArg, ValueTask<TResult>>)));
        return invoker(arg);
    }

    public void ValidateModuleActivation(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        if (Volatile.Read(ref modulesActivated) != 0)
        {
            return;
        }

        lock (moduleActivationGate)
        {
            if (modulesActivated != 0)
            {
                return;
            }

            var instances = new Dictionary<Type, object>();
            var disposalOrder = new List<object>();
            try
            {
                // Activate modules in the canonical type order so activation, rollback,
                // and generation retirement remain deterministic.
                foreach (var moduleType in moduleTypes)
                {
                    var factory = moduleActivationFactories[moduleType];
                    object instance;
                    object? registeredInstance;
                    try
                    {
                        registeredInstance = services.GetService(moduleType);
                        instance = registeredInstance ?? factory(services, Array.Empty<object?>());
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Hotfix module '{moduleType.FullName}' constructor activation failed: {ex.Message}",
                            ex);
                    }

                    instances.Add(moduleType, instance);
                    if (registeredInstance is null)
                    {
                        disposalOrder.Add(instance);
                    }
                }
            }
            catch
            {
                DisposeModuleInstancesAsync(disposalOrder).AsTask().GetAwaiter().GetResult();
                throw;
            }

            moduleInstances = instances;
            moduleInstanceDisposalOrder = disposalOrder;
            Volatile.Write(ref modulesActivated, 1);
        }
    }

    internal object GetActivatedModule(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        if (Volatile.Read(ref modulesActivated) == 0)
        {
            throw new InvalidOperationException("Hotfix modules have not been activated.");
        }

        return moduleInstances.TryGetValue(moduleType, out var instance)
            ? instance
            : throw new InvalidOperationException($"Hotfix module '{moduleType.FullName}' has not been activated.");
    }

    private HotfixServiceMethodBinding ResolveServiceBinding(Type contractType, int methodId)
    {
        var key = new ServiceMethodKey(contractType, methodId);
        return serviceMethodBindings.TryGetValue(key, out var binding)
            ? binding
            : throw new HotfixMethodNotLoadedException(
                $"Hotfix service method '{contractType.FullName ?? contractType.Name}#{methodId}' is not loaded.");
    }

    private ValueTask<TResult> InvokeServiceBindingAsync<TArg, TResult>(HotfixServiceMethodBinding binding, TArg arg)
    {
        EnsureServiceActivation(binding, arg);
        ValidateServiceDelegateShape<TArg>(binding, typeof(TResult));
        var key = new ServiceDelegateCacheKey(
            new ServiceMethodKey(binding.ContractType, binding.MethodId),
            typeof(TArg),
            typeof(TResult));
        var invoker = (Func<TArg, ValueTask<TResult>>)serviceDelegates.GetOrAdd(
            key,
            _ => CreateServiceDelegate(binding, typeof(Func<TArg, ValueTask<TResult>>)));
        return invoker(arg);
    }

    private ValueTask InvokeServiceBindingAsync<TArg>(HotfixServiceMethodBinding binding, TArg arg)
    {
        EnsureServiceActivation(binding, arg);
        ValidateServiceDelegateShape<TArg>(binding, typeof(ValueTask));
        var key = new ServiceDelegateCacheKey(
            new ServiceMethodKey(binding.ContractType, binding.MethodId),
            typeof(TArg),
            typeof(void));
        var invoker = (Func<TArg, ValueTask>)serviceDelegates.GetOrAdd(
            key,
            _ => CreateServiceDelegate(binding, typeof(Func<TArg, ValueTask>)));
        return invoker(arg);
    }

    private void EnsureServiceActivation<TArg>(HotfixServiceMethodBinding binding, TArg arg)
    {
        if (binding.Method.IsStatic || Volatile.Read(ref modulesActivated) != 0)
        {
            return;
        }

        if (arg is not IHotfixCallContext callContext)
        {
            throw new InvalidOperationException(
                $"Hotfix service method '{binding.Key}' requires an argument that implements {typeof(IHotfixCallContext).FullName}.");
        }

        ValidateModuleActivation(callContext.Services);
    }

    private void EnsureHttpEndpointActivation<TArg>(
        HotfixHttpEndpointMethodBinding binding,
        TArg arg)
    {
        if (Volatile.Read(ref modulesActivated) != 0)
        {
            return;
        }

        if (arg is not IHotfixCallContext callContext)
        {
            throw new InvalidOperationException(
                $"Application HTTP endpoint '{binding.Endpoint.Method} {binding.Endpoint.RoutePattern}' requires an argument that implements {typeof(IHotfixCallContext).FullName}.");
        }

        ValidateModuleActivation(callContext.Services);
    }

    private static void ValidateServiceDelegateShape<TArg>(HotfixServiceMethodBinding binding, Type resultType)
    {
        if (binding.ParameterTypes.Count != 1 || binding.ParameterTypes[0] != typeof(TArg) || binding.ReturnType != resultType)
        {
            throw new InvalidOperationException($"Hotfix service method '{binding.Key}' does not match the requested typed invocation.");
        }
    }

    private Delegate CreateServiceDelegate(HotfixServiceMethodBinding binding, Type delegateType)
    {
        if (binding.Method.IsStatic)
        {
            return binding.Method.CreateDelegate(delegateType);
        }

        if (!moduleInstances.TryGetValue(binding.ServiceType, out var instance))
        {
            throw new InvalidOperationException($"Hotfix service '{binding.ServiceType.FullName}' has not been activated.");
        }

        return binding.Method.CreateDelegate(delegateType, instance);
    }

    private Delegate CreateHttpEndpointDelegate(
        HotfixHttpEndpointMethodBinding binding,
        Type delegateType)
    {
        if (!moduleInstances.TryGetValue(binding.ServiceType, out var instance))
        {
            throw new InvalidOperationException(
                $"Application HTTP service '{binding.ServiceType.FullName}' has not been activated.");
        }

        return binding.Method.CreateDelegate(delegateType, instance);
    }

    private static ValueTask DisposeModuleInstanceAsync(object instance)
    {
        switch (instance)
        {
            case IAsyncDisposable asyncDisposable:
                return asyncDisposable.DisposeAsync();
            case IDisposable disposable:
                disposable.Dispose();
                return default;
            default:
                return default;
        }
    }

    private static async ValueTask DisposeModuleInstancesAsync(IReadOnlyList<object> instances)
    {
        for (var index = instances.Count - 1; index >= 0; index--)
        {
            await DisposeModuleInstanceAsync(instances[index]).ConfigureAwait(false);
        }
    }

    public void ValidateMethodShapes()
    {
        foreach (var binding in bindings.Values)
        {
            var parameters = binding.Method.GetParameters();
            if (binding.Method.IsStatic)
            {
                throw new InvalidOperationException($"Hotfix method '{binding.Key}' must be an instance method.");
            }

            if (parameters.Length != binding.ParameterTypes.Count + 1)
            {
                throw new InvalidOperationException($"Hotfix method '{binding.Key}' parameter count does not match its dispatch key.");
            }

            if (parameters[0].ParameterType != binding.StateType)
            {
                throw new InvalidOperationException($"Hotfix method '{binding.Key}' state parameter does not match its dispatch key.");
            }

            for (var i = 0; i < binding.ParameterTypes.Count; i++)
            {
                if (parameters[i + 1].ParameterType != binding.ParameterTypes[i])
                {
                    throw new InvalidOperationException($"Hotfix method '{binding.Key}' argument parameter {i} does not match its dispatch key.");
                }
            }

            if (binding.Method.ReturnType != binding.ReturnType)
            {
                throw new InvalidOperationException($"Hotfix method '{binding.Key}' return type does not match its dispatch key.");
            }
        }
    }

    public void ValidateTypedDispatchDelegates()
    {
        foreach (var binding in bindings.Values)
        {
            if (binding.ReturnType == typeof(void) || binding.ParameterTypes.Count > 1)
            {
                continue;
            }

            var delegateType = binding.ParameterTypes.Count == 0
                ? typeof(Func<,>).MakeGenericType(binding.StateType, binding.ReturnType)
                : typeof(Func<,,>).MakeGenericType(binding.StateType, binding.ParameterTypes[0], binding.ReturnType);
            binding.Method.CreateDelegate(delegateType, GetActivatedModule(binding.BehaviorType));
        }

        foreach (var binding in httpEndpointBindings)
        {
            var delegateType = typeof(Func<,>).MakeGenericType(
                binding.ArgumentType,
                typeof(ValueTask<>).MakeGenericType(binding.ResultType));
            binding.Method.CreateDelegate(
                delegateType,
                GetActivatedModule(binding.ServiceType));
        }
    }

    private static IReadOnlyDictionary<ulong, HotfixActorMethodDescriptor> CreateActorMethodIdBindings(
        IReadOnlyList<HotfixActorMethodDescriptor> actorMethods)
    {
        var dictionary = new Dictionary<ulong, HotfixActorMethodDescriptor>();
        foreach (var actorMethod in actorMethods)
        {
            if (dictionary.TryGetValue(actorMethod.MethodId, out var existing))
            {
                throw new InvalidOperationException(
                    $"Hotfix actor method id collision '{actorMethod.MethodId}' between '{existing.MethodKey}' and '{actorMethod.MethodKey}'.");
            }

            dictionary.Add(actorMethod.MethodId, actorMethod);
        }

        return dictionary;
    }

    private static IReadOnlyDictionary<ulong, HotfixTimerMethodDescriptor> CreateTimerMethodIdBindings(
        IReadOnlyList<HotfixTimerMethodDescriptor> timerMethods)
    {
        var dictionary = new Dictionary<ulong, HotfixTimerMethodDescriptor>();
        foreach (var timerMethod in timerMethods)
        {
            if (dictionary.TryGetValue(timerMethod.MethodId, out var existing))
            {
                throw new InvalidOperationException(
                    $"Hotfix timer method id collision '{timerMethod.MethodId}' between '{existing.MethodKey}' and '{timerMethod.MethodKey}'.");
            }

            dictionary.Add(timerMethod.MethodId, timerMethod);
        }

        return dictionary;
    }

    public Func<TState, TResult> Resolve<TState, TResult>(HotfixMethodKey key)
    {
        return (Func<TState, TResult>)ResolveDelegate(key, typeof(Func<TState, TResult>));
    }

    public Func<TState, TArg, TResult> Resolve<TState, TArg, TResult>(HotfixMethodKey key)
    {
        return (Func<TState, TArg, TResult>)ResolveDelegate(key, typeof(Func<TState, TArg, TResult>));
    }

    private Delegate ResolveDelegate(HotfixMethodKey key, Type delegateType)
    {
        var cacheKey = new DelegateCacheKey(key, delegateType);
        return delegates.GetOrAdd(
            cacheKey,
            static (candidate, table) =>
            {
                var binding = table.ResolveBinding(candidate.Key);
                return binding.Method.CreateDelegate(
                    candidate.DelegateType,
                    table.GetActivatedModule(binding.BehaviorType));
            },
            this);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        IReadOnlyList<object> instances;
        lock (moduleActivationGate)
        {
            instances = moduleInstanceDisposalOrder;
            moduleInstanceDisposalOrder = Array.Empty<object>();
            moduleInstances = new Dictionary<Type, object>();
            httpEndpointDelegates.Clear();
            serviceDelegates.Clear();
            delegates.Clear();
        }

        await DisposeModuleInstancesAsync(instances).ConfigureAwait(false);
    }

    private readonly record struct DelegateCacheKey(HotfixMethodKey Key, Type DelegateType);

    private readonly record struct ServiceMethodKey(Type ContractType, int MethodId);

    private readonly record struct ServiceDelegateCacheKey(
        ServiceMethodKey Method,
        Type ArgumentType,
        Type ResultType);

    private readonly record struct HttpEndpointDelegateCacheKey(
        int EndpointSlot,
        Type ArgumentType,
        Type ResultType);
}
