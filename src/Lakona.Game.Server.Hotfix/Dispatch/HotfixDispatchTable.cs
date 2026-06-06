using System.Reflection;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixDispatchTable
{
    private readonly IReadOnlyDictionary<HotfixMethodKey, MethodInfo> methods;
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
        this.methods = methodList.ToDictionary(static method => method.Key, static method => method.Method);
        MethodKeys = this.methods.Keys.OrderBy(static key => key.ToString(), StringComparer.Ordinal).ToArray();
    }

    public long Version { get; }

    public IReadOnlyList<HotfixMethodKey> MethodKeys { get; }

    public MethodInfo Resolve(HotfixMethodKey key)
    {
        return methods.TryGetValue(key, out var method)
            ? method
            : throw new HotfixMethodNotLoadedException($"Hotfix method '{key}' is not loaded.");
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
