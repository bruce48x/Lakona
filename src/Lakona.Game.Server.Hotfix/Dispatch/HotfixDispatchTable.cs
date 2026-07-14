using System.Collections.Concurrent;
using System.Reflection;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixDispatchTable : IDisposable, IAsyncDisposable
{
    private readonly IReadOnlyDictionary<HotfixMethodKey, HotfixMethodBinding> bindings;
    private readonly IReadOnlyDictionary<string, HotfixServiceMethodBinding> serviceBindings;
    private readonly IReadOnlyDictionary<ServiceMethodKey, HotfixServiceMethodBinding> serviceMethodBindings;
    private readonly IReadOnlyDictionary<string, HotfixActorMethodDescriptor> actorMethodBindings;
    private readonly IReadOnlyDictionary<ulong, HotfixActorMethodDescriptor> actorMethodIdBindings;
    private readonly IReadOnlyDictionary<Type, HotfixActorLifecycleDescriptor> actorLifecycleBindings;
    private readonly IReadOnlyDictionary<Type, ObjectFactory> serviceActivationFactories;
    private readonly ConcurrentDictionary<DelegateCacheKey, Delegate> delegates = new();
    private readonly ConcurrentDictionary<ServiceDelegateCacheKey, Delegate> serviceDelegates = new();
    private readonly object serviceActivationGate = new();
    private IReadOnlyDictionary<Type, object> serviceInstances = new Dictionary<Type, object>();
    private IReadOnlyList<object> serviceInstanceDisposalOrder = Array.Empty<object>();
    private int servicesActivated;
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
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(actorMethods);
        ArgumentNullException.ThrowIfNull(actorLifecycles);

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

        Version = version;
        bindings = methodList.ToDictionary(static method => method.Key, static method => method);
        serviceBindings = serviceList.ToDictionary(static service => service.Key, static service => service);
        serviceMethodBindings = serviceList.ToDictionary(
            static service => new ServiceMethodKey(service.ContractType, service.MethodId),
            static service => service);
        actorMethodBindings = actorMethodList.ToDictionary(static method => method.MethodKey, static method => method, StringComparer.Ordinal);
        actorMethodIdBindings = CreateActorMethodIdBindings(actorMethodList);
        actorLifecycleBindings = actorLifecycleList.ToDictionary(static lifecycle => lifecycle.ActorType, static lifecycle => lifecycle);
        serviceActivationFactories = serviceList
            .Where(static service => !service.Method.IsStatic)
            .Select(static service => service.ServiceType)
            .Distinct()
            .ToDictionary(
                static serviceType => serviceType,
                static serviceType => ActivatorUtilities.CreateFactory(serviceType, Type.EmptyTypes));
        MethodKeys = bindings.Keys.OrderBy(static key => key.ToString(), StringComparer.Ordinal).ToArray();
    }

    public long Version { get; }

    public IReadOnlyList<HotfixMethodKey> MethodKeys { get; }

    public MethodInfo Resolve(HotfixMethodKey key)
    {
        return bindings.TryGetValue(key, out var binding)
            ? binding.Method
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

    private static async ValueTask<object?> InvokeActorBindingAsync(
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
        return await binding.Invoker.InvokeAsync(actor, request, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<TResult> InvokeServiceAsync<TContract, TArg, TResult>(string methodName, TArg arg)
    {
        var key = HotfixDispatch.CreateServiceKey<TContract, TResult>(methodName, typeof(TArg));
        return InvokeServiceByKeyAsync<TArg, TResult>(key, arg);
    }

    public ValueTask<TResult> InvokeServiceAsync<TContract, TArg, TResult>(int methodId, TArg arg)
    {
        return InvokeServiceBindingAsync<TArg, TResult>(ResolveServiceBinding(typeof(TContract), methodId), arg);
    }

    public ValueTask InvokeServiceAsync<TContract, TArg>(string methodName, TArg arg)
    {
        var key = HotfixDispatch.CreateServiceKey(typeof(TContract), methodName, typeof(ValueTask), [typeof(TArg)]);
        return InvokeServiceByKeyAsync(key, arg);
    }

    public ValueTask InvokeServiceAsync<TContract, TArg>(int methodId, TArg arg)
    {
        return InvokeServiceBindingAsync(ResolveServiceBinding(typeof(TContract), methodId), arg);
    }

    private ValueTask<TResult> InvokeServiceByKeyAsync<TArg, TResult>(string key, TArg arg)
    {
        if (!serviceBindings.TryGetValue(key, out var binding))
        {
            throw new HotfixMethodNotLoadedException($"Hotfix service method '{key}' is not loaded.");
        }

        return InvokeServiceBindingAsync<TArg, TResult>(binding, arg);
    }

    private ValueTask InvokeServiceByKeyAsync<TArg>(string key, TArg arg)
    {
        if (!serviceBindings.TryGetValue(key, out var binding))
        {
            throw new HotfixMethodNotLoadedException($"Hotfix service method '{key}' is not loaded.");
        }

        return InvokeServiceBindingAsync(binding, arg);
    }

    public void ValidateServiceActivation(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        if (Volatile.Read(ref servicesActivated) != 0)
        {
            return;
        }

        lock (serviceActivationGate)
        {
            if (servicesActivated != 0)
            {
                return;
            }

            var instances = new Dictionary<Type, object>();
            var disposalOrder = new List<object>();
            try
            {
                foreach (var (serviceType, factory) in serviceActivationFactories)
                {
                    object instance;
                    try
                    {
                        instance = factory(services, Array.Empty<object?>());
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Hotfix service '{serviceType.FullName}' constructor activation failed: {ex.Message}",
                            ex);
                    }

                    instances.Add(serviceType, instance);
                    disposalOrder.Add(instance);
                }
            }
            catch
            {
                DisposeServiceInstancesAsync(disposalOrder).AsTask().GetAwaiter().GetResult();
                throw;
            }

            serviceInstances = instances;
            serviceInstanceDisposalOrder = disposalOrder;
            Volatile.Write(ref servicesActivated, 1);
        }
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
        if (binding.Method.IsStatic || Volatile.Read(ref servicesActivated) != 0)
        {
            return;
        }

        if (arg is not IHotfixCallContext callContext)
        {
            throw new InvalidOperationException(
                $"Hotfix service method '{binding.Key}' requires an argument that implements {typeof(IHotfixCallContext).FullName}.");
        }

        ValidateServiceActivation(callContext.Services);
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

        if (!serviceInstances.TryGetValue(binding.ServiceType, out var instance))
        {
            throw new InvalidOperationException($"Hotfix service '{binding.ServiceType.FullName}' has not been activated.");
        }

        return binding.Method.CreateDelegate(delegateType, instance);
    }

    private static ValueTask DisposeServiceInstanceAsync(object instance)
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

    private static async ValueTask DisposeServiceInstancesAsync(IReadOnlyList<object> instances)
    {
        for (var index = instances.Count - 1; index >= 0; index--)
        {
            await DisposeServiceInstanceAsync(instances[index]).ConfigureAwait(false);
        }
    }

    public void ValidateMethodShapes()
    {
        foreach (var binding in bindings.Values)
        {
            var parameters = binding.Method.GetParameters();
            if (!binding.Method.IsStatic)
            {
                throw new InvalidOperationException($"Hotfix method '{binding.Key}' must be static.");
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
            binding.Method.CreateDelegate(delegateType);
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
            static (candidate, table) => table.Resolve(candidate.Key).CreateDelegate(candidate.DelegateType),
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
        lock (serviceActivationGate)
        {
            instances = serviceInstanceDisposalOrder;
            serviceInstanceDisposalOrder = Array.Empty<object>();
            serviceInstances = new Dictionary<Type, object>();
            serviceDelegates.Clear();
            delegates.Clear();
        }

        await DisposeServiceInstancesAsync(instances).ConfigureAwait(false);
    }

    private readonly record struct DelegateCacheKey(HotfixMethodKey Key, Type DelegateType);

    private readonly record struct ServiceMethodKey(Type ContractType, int MethodId);

    private readonly record struct ServiceDelegateCacheKey(
        ServiceMethodKey Method,
        Type ArgumentType,
        Type ResultType);
}
