using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Rpc.Core;

namespace Lakona.Game.Client;

/// <summary>Stable dispatch target used by generated service proxies across connection generations.</summary>
public sealed class ReconnectableRpcClient : IRpcClient
{
    private readonly object gate = new();
    private IRpcClient? current;
    private readonly List<Action<IRpcClient>> registrations = [];

    public void SetCurrent(IRpcClient client)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));
        Action<IRpcClient>[] snapshot;
        lock (gate)
        {
            current = client;
            snapshot = [.. registrations];
        }
        foreach (var registration in snapshot) registration(client);
    }

    public void ClearCurrent(IRpcClient client)
    {
        lock (gate)
        {
            if (ReferenceEquals(current, client)) current = null;
        }
    }

    public ValueTask<TResult> CallAsync<TArg, TResult>(
        RpcMethod<TArg, TResult> method,
        TArg? arg,
        CancellationToken ct = default)
    {
        IRpcClient target;
        lock (gate)
            target = current ?? throw new InvalidOperationException("Lakona game client is reconnecting.");
        return target.CallAsync(method, arg, ct);
    }

    public void RegisterNotificationHandler<TArg>(
        RpcNotificationMethod<TArg> method,
        Func<TArg, ValueTask> handler)
    {
        Action<IRpcClient> registration = client => client.RegisterNotificationHandler(method, handler);
        IRpcClient? target;
        lock (gate)
        {
            registrations.Add(registration);
            target = current;
        }
        if (target is not null) registration(target);
    }
}
