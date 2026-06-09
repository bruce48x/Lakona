using System.Reflection;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixDispatchTable
{
    private readonly IReadOnlyDictionary<HotfixMethodKey, HotfixMethodBinding> bindings;
    private readonly Dictionary<DelegateCacheKey, Delegate> delegates = new();

    public HotfixDispatchTable(long version, IEnumerable<HotfixMethodBinding> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);

        var methodList = new List<HotfixMethodBinding>();
        foreach (var method in methods)
        {
            if (method is null)
            {
                throw new ArgumentException("Method bindings cannot contain null.", nameof(methods));
            }

            methodList.Add(method);
        }

        Version = version;
        bindings = methodList.ToDictionary(static method => method.Key, static method => method);
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

    private readonly record struct DelegateCacheKey(HotfixMethodKey Key, Type DelegateType);
}
