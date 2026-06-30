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
        var method = callbackType.GetMethod(
            descriptor.MethodName,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (method is null)
        {
            throw new InvalidOperationException($"Timer callback method '{descriptor.CallbackFullName}.{descriptor.MethodName}' is not loaded.");
        }

        ValidateMethodShape(callbackType, method, argsType);
        return method;
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

        var method = callbackType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (method is null)
        {
            throw new InvalidOperationException($"Timer callback method '{callbackType.FullName}.{methodName}' is not loaded.");
        }

        ValidateMethodShape(callbackType, method, typeof(TArgs));

        return new ResolvedTimerCallback(
            callbackType.Assembly.GetName().Name ?? callbackType.Assembly.FullName!,
            callbackType.FullName ?? callbackType.Name,
            method.Name,
            snapshot.DispatchTable.Version);
    }

    private static void ValidateMethodShape(Type callbackType, MethodInfo method, Type argsType)
    {
        if (!method.IsStatic)
        {
            throw new InvalidOperationException($"Timer callback method '{callbackType.FullName}.{method.Name}' must be static.");
        }

        if (method.ContainsGenericParameters || method.IsGenericMethodDefinition)
        {
            throw new InvalidOperationException($"Timer callback method '{callbackType.FullName}.{method.Name}' must not be generic.");
        }

        if (method.ReturnType != typeof(ValueTask))
        {
            throw new InvalidOperationException($"Timer callback method '{callbackType.FullName}.{method.Name}' must return ValueTask.");
        }

        var expectedParameterType = typeof(TimerTick<>).MakeGenericType(argsType);
        var parameters = method.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType != expectedParameterType)
        {
            throw new InvalidOperationException($"Timer callback method '{callbackType.FullName}.{method.Name}' must accept {expectedParameterType.FullName}.");
        }
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
