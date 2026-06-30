using System.Reflection;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerCallbackResolver
{
    public MethodInfo Resolve(HotfixRuntimeSnapshot snapshot, LakonaTimerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (snapshot.MainAssembly is null)
        {
            throw new InvalidOperationException("Timer callbacks require an active hotfix snapshot with a main assembly.");
        }

        var callbackAssemblyName = snapshot.MainAssembly.GetName().Name ?? snapshot.MainAssembly.FullName!;
        if (!StringComparer.Ordinal.Equals(callbackAssemblyName, descriptor.CallbackAssemblyName))
        {
            throw new InvalidOperationException($"Timer callback assembly '{descriptor.CallbackAssemblyName}' is not the active hotfix assembly.");
        }

        var callbackType = snapshot.MainAssembly.GetType(descriptor.CallbackFullName, throwOnError: false);
        if (callbackType is null)
        {
            throw new InvalidOperationException($"Timer callback type '{descriptor.CallbackFullName}' is not loaded.");
        }

        var argsType = ResolveArgsType(snapshot, descriptor);
        return ResolveTimerCallbackMethod(
            callbackType,
            descriptor.MethodName,
            argsType);
    }

    public ResolvedTimerCallback Validate<TCallback, TArgs>(HotfixRuntimeSnapshotLease lease, string methodName)
        where TCallback : class
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);

        var snapshot = lease.Snapshot;
        if (snapshot.MainAssembly is null)
        {
            throw new InvalidOperationException("Timer callbacks require an active hotfix snapshot with a main assembly.");
        }

        if (snapshot.DispatchTable is null)
        {
            throw new InvalidOperationException("Timer callbacks require an active hotfix snapshot with a dispatch table.");
        }

        var callbackType = typeof(TCallback);
        if (callbackType.IsGenericType)
        {
            throw new InvalidOperationException($"Timer callback type '{callbackType.FullName}' must not be generic.");
        }

        if (!ReferenceEquals(callbackType.Assembly, snapshot.MainAssembly))
        {
            throw new InvalidOperationException($"Timer callback type '{callbackType.FullName}' must be from the active hotfix assembly.");
        }

        var method = ResolveTimerCallbackMethod(
            callbackType,
            methodName,
            typeof(TArgs));

        return new ResolvedTimerCallback(
            callbackType.Assembly.GetName().Name ?? callbackType.Assembly.FullName!,
            callbackType.FullName ?? callbackType.Name,
            method.Name,
            snapshot.DispatchTable.Version);
    }

    private static MethodInfo ResolveTimerCallbackMethod(Type callbackType, string methodName, Type argsType)
    {
        var matches = callbackType
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .Where(method => IsTimerCallbackMethod(method, argsType))
            .ToArray();

        if (matches.Length == 1)
        {
            return matches[0];
        }

        throw new InvalidOperationException(
            $"Timer callback method '{callbackType.FullName}.{methodName}' must resolve to exactly one non-generic public static ValueTask method accepting {typeof(TimerTick<>).MakeGenericType(argsType).FullName}; found {matches.Length} loaded matches.");
    }

    private static bool IsTimerCallbackMethod(MethodInfo method, Type argsType)
    {
        if (!method.IsStatic)
        {
            return false;
        }

        if (method.ContainsGenericParameters || method.IsGenericMethodDefinition)
        {
            return false;
        }

        if (method.ReturnType != typeof(ValueTask))
        {
            return false;
        }

        var expectedParameterType = typeof(TimerTick<>).MakeGenericType(argsType);
        var parameters = method.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType == expectedParameterType;
    }

    private static Type ResolveArgsType(HotfixRuntimeSnapshot snapshot, LakonaTimerDescriptor descriptor)
    {
        var mainAssemblyName = snapshot.MainAssembly!.GetName().Name ?? snapshot.MainAssembly.FullName!;
        if (StringComparer.Ordinal.Equals(mainAssemblyName, descriptor.ArgsAssemblyName))
        {
            var hotfixArgsType = snapshot.MainAssembly.GetType(descriptor.ArgsFullName, throwOnError: false);
            if (hotfixArgsType is not null)
            {
                return hotfixArgsType;
            }

            throw new InvalidOperationException($"Timer args type '{descriptor.ArgsFullName}' is not loaded in the active hotfix assembly.");
        }

        var argsAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
                StringComparer.Ordinal.Equals(assembly.GetName().Name, descriptor.ArgsAssemblyName));
        var argsType = argsAssembly?.GetType(descriptor.ArgsFullName, throwOnError: false);
        return argsType ?? throw new InvalidOperationException($"Timer args type '{descriptor.ArgsFullName}' is not loaded.");
    }
}

internal sealed record ResolvedTimerCallback(
    string CallbackAssemblyName,
    string CallbackFullName,
    string MethodName,
    long Generation);
