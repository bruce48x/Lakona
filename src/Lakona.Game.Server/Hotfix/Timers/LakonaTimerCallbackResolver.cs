using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerCallbackResolver
{
    public HotfixTimerMethodDescriptor Resolve(HotfixRuntimeSnapshot snapshot, LakonaTimerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(descriptor);

        var table = snapshot.DispatchTable
            ?? throw new InvalidOperationException("Timer callbacks require an active hotfix dispatch table.");
        if (!table.TryResolveTimerMethod(descriptor.MethodId, out var callback))
        {
            throw new InvalidOperationException(
                $"Timer callback entry '{descriptor.CallbackFullName}.{descriptor.MethodName}' ({descriptor.MethodId}) is not loaded.");
        }

        ValidateIdentity(callback, descriptor.CallbackFullName, descriptor.MethodName);
        return callback;
    }

    public ResolvedTimerCallback Validate<TArgs>(
        HotfixRuntimeSnapshotLease lease,
        HotfixTimerEntry<TArgs> entry)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (entry.MethodId == 0 || string.IsNullOrWhiteSpace(entry.CallbackFullName) || string.IsNullOrWhiteSpace(entry.MethodName))
        {
            throw new ArgumentException("Timer callback entry is not initialized.", nameof(entry));
        }

        var snapshot = lease.Snapshot;
        var table = snapshot.DispatchTable
            ?? throw new InvalidOperationException("Timer callbacks require an active hotfix dispatch table.");
        if (!table.TryResolveTimerMethod(entry.MethodId, out var callback))
        {
            throw new InvalidOperationException(
                $"Timer callback entry '{entry.CallbackFullName}.{entry.MethodName}' ({entry.MethodId}) is not loaded.");
        }

        ValidateIdentity(callback, entry.CallbackFullName, entry.MethodName);
        if (!ReferenceEquals(callback.CallbackType.Assembly, snapshot.MainAssembly))
        {
            throw new InvalidOperationException(
                $"Timer callback type '{callback.CallbackType.FullName}' must be from the active hotfix assembly.");
        }

        if (callback.ArgsType != typeof(TArgs))
        {
            throw new InvalidOperationException(
                $"Timer callback entry '{entry.CallbackFullName}.{entry.MethodName}' requires args type '{callback.ArgsType.FullName}', not '{typeof(TArgs).FullName}'.");
        }

        return new ResolvedTimerCallback(
            callback.CallbackType.Assembly.GetName().Name ?? callback.CallbackType.Assembly.FullName!,
            callback.CallbackType.FullName ?? callback.CallbackType.Name,
            callback.MethodName,
            callback.MethodId,
            table.Version);
    }

    private static void ValidateIdentity(
        HotfixTimerMethodDescriptor callback,
        string callbackFullName,
        string methodName)
    {
        if (!string.Equals(callback.CallbackType.FullName, callbackFullName, StringComparison.Ordinal) ||
            !string.Equals(callback.MethodName, methodName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Timer callback entry id '{callback.MethodId}' resolves to '{callback.CallbackType.FullName}.{callback.MethodName}', not '{callbackFullName}.{methodName}'.");
        }
    }
}

internal sealed record ResolvedTimerCallback(
    string CallbackAssemblyName,
    string CallbackFullName,
    string MethodName,
    ulong MethodId,
    long Generation);
