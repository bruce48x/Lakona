using System.Reflection;
using System.Runtime.ExceptionServices;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixDispatchTable
{
    private readonly IReadOnlyDictionary<HotfixMethodKey, HotfixMethodBinding> bindings;
    private readonly IReadOnlyDictionary<string, HotfixServiceMethodBinding> serviceBindings;
    private readonly IReadOnlyDictionary<string, HotfixActorMethodDescriptor> actorMethodBindings;
    private readonly IReadOnlyDictionary<Type, ObjectFactory> serviceActivationFactories;
    private readonly IReadOnlyDictionary<string, HotfixFeatureCommandBinding> featureCommandBindings;
    private readonly IReadOnlyDictionary<Type, ObjectFactory> featureActivationFactories;
    private readonly Dictionary<DelegateCacheKey, Delegate> delegates = new();

    public HotfixDispatchTable(long version, IEnumerable<HotfixMethodBinding> methods)
        : this(
            version,
            methods,
            Array.Empty<HotfixServiceMethodBinding>(),
            Array.Empty<HotfixFeatureDeclaration>())
    {
    }

    public HotfixDispatchTable(
        long version,
        IEnumerable<HotfixMethodBinding> methods,
        IEnumerable<HotfixServiceMethodBinding> services)
        : this(version, methods, services, Array.Empty<HotfixFeatureDeclaration>())
    {
    }

    public HotfixDispatchTable(
        long version,
        IEnumerable<HotfixMethodBinding> methods,
        IEnumerable<HotfixServiceMethodBinding> services,
        IEnumerable<HotfixFeatureDeclaration> features)
        : this(version, methods, services, features, Array.Empty<HotfixActorMethodDescriptor>())
    {
    }

    public HotfixDispatchTable(
        long version,
        IEnumerable<HotfixMethodBinding> methods,
        IEnumerable<HotfixServiceMethodBinding> services,
        IEnumerable<HotfixActorMethodDescriptor> actorMethods)
        : this(version, methods, services, Array.Empty<HotfixFeatureDeclaration>(), actorMethods)
    {
    }

    public HotfixDispatchTable(
        long version,
        IEnumerable<HotfixMethodBinding> methods,
        IEnumerable<HotfixServiceMethodBinding> services,
        IEnumerable<HotfixFeatureDeclaration> features,
        IEnumerable<HotfixActorMethodDescriptor> actorMethods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(actorMethods);

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

        var featureCommandList = new List<HotfixFeatureCommandBinding>();
        foreach (var feature in features)
        {
            if (feature is null)
            {
                throw new ArgumentException("Feature declarations cannot contain null.", nameof(features));
            }

            foreach (var command in feature.Commands)
            {
                if (command is null)
                {
                    throw new ArgumentException("Feature command declarations cannot contain null.", nameof(features));
                }

                var commandId = FeatureCommandId.From(command.CommandId);
                var key = CreateFeatureCommandKey(feature.Name, commandId);
                var method = ResolveFeatureCommandMethod(feature, command);
                featureCommandList.Add(new HotfixFeatureCommandBinding(
                    key,
                    feature.Name,
                    commandId,
                    feature.FeatureType,
                    command.RequestType,
                    command.ReplyType,
                    method));
            }
        }

        var featureCommandDictionary = new Dictionary<string, HotfixFeatureCommandBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in featureCommandList)
        {
            if (featureCommandDictionary.ContainsKey(binding.Key))
            {
                throw new InvalidOperationException($"Duplicate hotfix feature command '{binding.Key}'.");
            }

            featureCommandDictionary.Add(binding.Key, binding);
        }

        Version = version;
        Features = features.ToArray();
        bindings = methodList.ToDictionary(static method => method.Key, static method => method);
        serviceBindings = serviceList.ToDictionary(static service => service.Key, static service => service);
        actorMethodBindings = actorMethodList.ToDictionary(static method => method.MethodKey, static method => method, StringComparer.Ordinal);
        serviceActivationFactories = serviceList
            .Where(static service => !service.Method.IsStatic)
            .Select(static service => service.ServiceType)
            .Distinct()
            .ToDictionary(
                static serviceType => serviceType,
                static serviceType => ActivatorUtilities.CreateFactory(serviceType, Type.EmptyTypes));
        featureCommandBindings = featureCommandDictionary;
        featureActivationFactories = featureCommandList
            .Where(static command => !command.Method.IsStatic)
            .Select(static command => command.FeatureType)
            .Distinct()
            .ToDictionary(
                static featureType => featureType,
                static featureType => ActivatorUtilities.CreateFactory(featureType, Type.EmptyTypes));
        MethodKeys = bindings.Keys.OrderBy(static key => key.ToString(), StringComparer.Ordinal).ToArray();
    }

    public long Version { get; }

    public IReadOnlyList<HotfixMethodKey> MethodKeys { get; }

    public IReadOnlyList<HotfixFeatureDeclaration> Features { get; }

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

        object? result;
        try
        {
            result = binding.Method.Invoke(
                null,
                binding.HasCancellationToken
                    ? [actor, request, cancellationToken]
                    : [actor, request]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        if (binding.ResultType is null)
        {
            if (result is ValueTask task)
            {
                await task.ConfigureAwait(false);
                return null;
            }

            throw new InvalidOperationException($"Hotfix actor method '{methodKey}' returned an invalid result.");
        }

        var awaitMethod = typeof(HotfixDispatchTable)
            .GetMethod(nameof(AwaitActorResultAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(binding.ResultType);
        return await (ValueTask<object?>)awaitMethod.Invoke(null, [result])!;
    }

    public ValueTask<TResult> InvokeServiceAsync<TContract, TArg, TResult>(string methodName, TArg arg)
    {
        var key = HotfixDispatch.CreateServiceKey<TContract, TResult>(methodName, typeof(TArg));
        return InvokeServiceByKeyAsync<TArg, TResult>(key, arg);
    }

    public ValueTask<TResult> InvokeServiceAsync<TContract, TArg, TResult>(int methodId, TArg arg)
    {
        var key = HotfixDispatch.CreateServiceKey<TContract, TResult>(methodId, typeof(TArg));
        return InvokeServiceByKeyAsync<TArg, TResult>(key, arg);
    }

    public ValueTask InvokeServiceAsync<TContract, TArg>(string methodName, TArg arg)
    {
        var key = HotfixDispatch.CreateServiceKey(typeof(TContract), methodName, typeof(ValueTask), [typeof(TArg)]);
        return InvokeServiceByKeyAsync(key, arg);
    }

    public ValueTask InvokeServiceAsync<TContract, TArg>(int methodId, TArg arg)
    {
        var key = HotfixDispatch.CreateServiceKey(typeof(TContract), methodId, typeof(ValueTask), [typeof(TArg)]);
        return InvokeServiceByKeyAsync(key, arg);
    }

    public bool TryResolveFeatureCommand(
        string featureName,
        FeatureCommandId commandId,
        out HotfixFeatureCommandDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        var key = CreateFeatureCommandKey(featureName, commandId);
        if (featureCommandBindings.TryGetValue(key, out var binding))
        {
            descriptor = new HotfixFeatureCommandDescriptor(
                binding.Key,
                binding.FeatureName,
                binding.CommandId,
                binding.RequestType,
                binding.ReplyType);
            return true;
        }

        descriptor = null!;
        return false;
    }

    public ValueTask<object?> InvokeFeatureCommandAsync(
        HotfixFeatureCommandDescriptor descriptor,
        object? request,
        FeatureMessageRequest message,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);
        cancellationToken.ThrowIfCancellationRequested();

        if (!featureCommandBindings.TryGetValue(descriptor.Key, out var binding))
        {
            throw new HotfixMethodNotLoadedException($"Hotfix feature command '{descriptor.Key}' is not loaded.");
        }

        if (binding.RequestType != descriptor.RequestType || binding.ReplyType != descriptor.ReplyType)
        {
            throw new InvalidOperationException($"Hotfix feature command descriptor '{descriptor.Key}' does not match the loaded binding.");
        }

        if (request is null && binding.RequestType.IsValueType ||
            request is not null && !binding.RequestType.IsInstanceOfType(request))
        {
            throw new ArgumentException(
                $"Hotfix feature command '{binding.Key}' requires request type '{binding.RequestType.FullName}'.",
                nameof(request));
        }

        var target = CreateFeatureCommandTarget(binding, services);
        object? result;
        try
        {
            var call = CreateFeatureCommandCall(binding, request, message, services, cancellationToken);
            result = binding.Method.Invoke(target.Instance, [call]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        catch
        {
            DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
            throw;
        }

        var awaitMethod = typeof(HotfixDispatchTable)
            .GetMethod(nameof(AwaitFeatureCommandResultAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(binding.ReplyType);
        return (ValueTask<object?>)awaitMethod.Invoke(null, [result, target])!;
    }

    private ValueTask<TResult> InvokeServiceByKeyAsync<TArg, TResult>(string key, TArg arg)
    {
        if (!serviceBindings.TryGetValue(key, out var binding))
        {
            throw new HotfixMethodNotLoadedException($"Hotfix service method '{key}' is not loaded.");
        }

        var target = CreateServiceTarget(binding, arg);
        object? result;
        try
        {
            result = binding.Method.Invoke(target.Instance, [arg]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        catch
        {
            DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
            throw;
        }

        if (result is ValueTask<TResult> valueTask)
        {
            return AwaitAndDisposeAsync(valueTask, target);
        }

        if (result is TResult value)
        {
            DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
            return new ValueTask<TResult>(value);
        }

        if (result is null && default(TResult) is null)
        {
            DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
            return new ValueTask<TResult>(default(TResult)!);
        }

        DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
        throw new InvalidOperationException($"Hotfix service method '{key}' returned an invalid result.");
    }

    private ValueTask InvokeServiceByKeyAsync<TArg>(string key, TArg arg)
    {
        if (!serviceBindings.TryGetValue(key, out var binding))
        {
            throw new HotfixMethodNotLoadedException($"Hotfix service method '{key}' is not loaded.");
        }

        var target = CreateServiceTarget(binding, arg);
        object? result;
        try
        {
            result = binding.Method.Invoke(target.Instance, [arg]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        catch
        {
            DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
            throw;
        }

        if (result is ValueTask valueTask)
        {
            return AwaitAndDisposeAsync(valueTask, target);
        }

        if (result is null)
        {
            DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
            return default;
        }

        DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
        throw new InvalidOperationException($"Hotfix service method '{key}' returned an invalid result.");
    }

    public void ValidateServiceActivation(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var validated = new HashSet<Type>();
        foreach (var binding in serviceBindings.Values)
        {
            if (binding.Method.IsStatic || !validated.Add(binding.ServiceType))
            {
                continue;
            }

            if (!serviceActivationFactories.TryGetValue(binding.ServiceType, out var factory))
            {
                throw new InvalidOperationException($"Hotfix service '{binding.ServiceType.FullName}' does not have an activation factory.");
            }

            ServiceTarget target = default;
            try
            {
                target = new ServiceTarget(factory(services, Array.Empty<object?>()));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Hotfix service '{binding.ServiceType.FullName}' constructor activation failed: {ex.Message}",
                    ex);
            }
            finally
            {
                DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
            }
        }
    }

    public void ValidateFeatureCommandMethods()
    {
        foreach (var binding in featureCommandBindings.Values)
        {
            var callType = typeof(HotfixFeatureCommandCall<>).MakeGenericType(binding.RequestType);
            var returnType = typeof(ValueTask<>).MakeGenericType(binding.ReplyType);
            var parameters = binding.Method.GetParameters();
            if (binding.Method.ContainsGenericParameters ||
                binding.Method.ReturnType != returnType ||
                parameters.Length != 1 ||
                parameters[0].ParameterType != callType)
            {
                throw new InvalidOperationException(
                    $"Hotfix feature command '{binding.Key}' must return {returnType.FullName} and accept {callType.FullName}.");
            }
        }
    }

    public void ValidateFeatureCommandActivation(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var validated = new HashSet<Type>();
        foreach (var binding in featureCommandBindings.Values)
        {
            if (binding.Method.IsStatic || !validated.Add(binding.FeatureType))
            {
                continue;
            }

            if (!featureActivationFactories.TryGetValue(binding.FeatureType, out var factory))
            {
                throw new InvalidOperationException($"Hotfix feature '{binding.FeatureType.FullName}' does not have an activation factory.");
            }

            ServiceTarget target = default;
            try
            {
                target = new ServiceTarget(factory(services, Array.Empty<object?>()));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Hotfix feature '{binding.FeatureType.FullName}' constructor activation failed: {ex.Message}",
                    ex);
            }
            finally
            {
                DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private ServiceTarget CreateServiceTarget<TArg>(HotfixServiceMethodBinding binding, TArg arg)
    {
        if (binding.Method.IsStatic)
        {
            return new ServiceTarget(null);
        }

        if (!serviceActivationFactories.TryGetValue(binding.ServiceType, out var factory))
        {
            throw new InvalidOperationException($"Hotfix service '{binding.ServiceType.FullName}' does not have an activation factory.");
        }

        if (arg is not IHotfixCallContext callContext)
        {
            throw new InvalidOperationException(
                $"Hotfix service method '{binding.Key}' requires an argument that implements {typeof(IHotfixCallContext).FullName}.");
        }

        return new ServiceTarget(factory(callContext.Services, Array.Empty<object?>()));
    }

    private ServiceTarget CreateFeatureCommandTarget(HotfixFeatureCommandBinding binding, IServiceProvider services)
    {
        if (binding.Method.IsStatic)
        {
            return new ServiceTarget(null);
        }

        if (!featureActivationFactories.TryGetValue(binding.FeatureType, out var factory))
        {
            throw new InvalidOperationException($"Hotfix feature '{binding.FeatureType.FullName}' does not have an activation factory.");
        }

        return new ServiceTarget(factory(services, Array.Empty<object?>()));
    }

    private static object CreateFeatureCommandCall(
        HotfixFeatureCommandBinding binding,
        object? request,
        FeatureMessageRequest message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var callType = typeof(HotfixFeatureCommandCall<>).MakeGenericType(binding.RequestType);
        return Activator.CreateInstance(
            callType,
            request,
            binding.FeatureName,
            binding.CommandId,
            message.CorrelationId,
            message.SourceNode,
            message.ExpiresAt,
            cancellationToken,
            services)!;
    }

    private static async ValueTask<object?> AwaitFeatureCommandResultAsync<TReply>(
        ValueTask<TReply> task,
        ServiceTarget target)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            await DisposeServiceTargetAsync(target).ConfigureAwait(false);
        }
    }

    private static async ValueTask<object?> AwaitActorResultAsync<TResult>(ValueTask<TResult> task)
    {
        return await task.ConfigureAwait(false);
    }

    private static ValueTask DisposeServiceTargetAsync(ServiceTarget target)
    {
        switch (target.Instance)
        {
            case null:
                return default;
            case IAsyncDisposable asyncDisposable:
                return asyncDisposable.DisposeAsync();
            case IDisposable disposable:
                disposable.Dispose();
                return default;
            default:
                return default;
        }
    }

    private static async ValueTask<TResult> AwaitAndDisposeAsync<TResult>(
        ValueTask<TResult> task,
        ServiceTarget target)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            await DisposeServiceTargetAsync(target).ConfigureAwait(false);
        }
    }

    private static async ValueTask AwaitAndDisposeAsync(
        ValueTask task,
        ServiceTarget target)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            await DisposeServiceTargetAsync(target).ConfigureAwait(false);
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

    private static MethodInfo ResolveFeatureCommandMethod(
        HotfixFeatureDeclaration feature,
        HotfixFeatureCommandDeclaration command)
    {
        var callType = typeof(HotfixFeatureCommandCall<>).MakeGenericType(command.RequestType);
        var returnType = typeof(ValueTask<>).MakeGenericType(command.ReplyType);
        var matches = feature.FeatureType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => string.Equals(method.Name, command.MethodName, StringComparison.Ordinal))
            .ToArray();

        var method = matches.SingleOrDefault(method =>
        {
            var parameters = method.GetParameters();
            return !method.ContainsGenericParameters &&
                method.ReturnType == returnType &&
                parameters.Length == 1 &&
                parameters[0].ParameterType == callType;
        });

        if (method is null)
        {
            throw new InvalidOperationException(
                $"Hotfix feature command '{feature.Name}#{command.CommandId}' must map to public instance or static method '{command.MethodName}' returning {returnType.FullName} and accepting {callType.FullName}.");
        }

        return method;
    }

    private static string CreateFeatureCommandKey(string featureName, FeatureCommandId commandId)
    {
        return $"{featureName}#{commandId.Value}";
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
        lock (delegates)
        {
            if (delegates.TryGetValue(cacheKey, out var existing))
            {
                return existing;
            }

            var method = Resolve(key);
            var typed = method.CreateDelegate(delegateType);
            delegates.Add(cacheKey, typed);
            return typed;
        }
    }

    private readonly record struct ServiceTarget(object? Instance);

    private readonly record struct DelegateCacheKey(HotfixMethodKey Key, Type DelegateType);
}
