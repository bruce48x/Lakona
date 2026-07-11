using System.Globalization;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Actors;

public sealed class StartupActorInvoker(
    IHotfixRuntimeAccessor hotfixRuntime,
    INodeDirectory nodeDirectory,
    LocalActorNodeIdentity localNode,
    IRemoteActorInvoker remote,
    IRemoteActorSerializer serializer,
    ClusterNodeSenderOptions clusterOptions,
    RemoteActorOptions remoteOptions,
    ILogger<StartupActorInvoker>? logger = null) : IStartupActorInvoker
{
    public async ValueTask CallAsync<TActor, TKey, TRequest>(TKey key, string actorName, string methodName, ulong remoteMethodId, TRequest request, Func<ActorId, TRequest, CancellationToken, ValueTask> invokeLocal, CancellationToken cancellationToken = default) where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(invokeLocal);
        var excluded = new HashSet<(NodeId, long)>();
        int? remainingAttempts = null;
        while (true)
        {
            var selection = await SelectAsync<TActor, TKey>(key, actorName, excluded, cancellationToken).ConfigureAwait(false);
            var target = selection.Target;
            remainingAttempts ??= selection.CandidateCount;
            if (target.Node == localNode.NodeId)
            {
                try { await invokeLocal(target.ActorId, request, cancellationToken).ConfigureAwait(false); return; }
                catch (ActorNotFoundException exception) when (exception.DefinitelyNotExecuted) { ExcludeOrThrow<TActor>(target, excluded, ref remainingAttempts); continue; }
            }

            var result = await remote.AskAsync(CreateInvocation(target, actorName, methodName, remoteMethodId, request), cancellationToken).ConfigureAwait(false);
            if (result.Status == RemoteActorStatus.Replied) return;
            if (result.RetrySafety == RemoteActorRetrySafety.DefinitelyNotExecuted) { LogExcluded<TActor>(target, result); ExcludeOrThrow<TActor>(target, excluded, ref remainingAttempts); continue; }
            RemoteActorCall.EnsureReplied(result, target.ActorId, actorName, methodName, target.Node);
        }
    }

    public async ValueTask<TResult> CallAsync<TActor, TKey, TRequest, TResult>(TKey key, string actorName, string methodName, ulong remoteMethodId, TRequest request, Func<ActorId, TRequest, CancellationToken, ValueTask<TResult>> invokeLocal, CancellationToken cancellationToken = default) where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(invokeLocal);
        var excluded = new HashSet<(NodeId, long)>();
        int? remainingAttempts = null;
        while (true)
        {
            var selection = await SelectAsync<TActor, TKey>(key, actorName, excluded, cancellationToken).ConfigureAwait(false);
            var target = selection.Target;
            remainingAttempts ??= selection.CandidateCount;
            if (target.Node == localNode.NodeId)
            {
                try { return await invokeLocal(target.ActorId, request, cancellationToken).ConfigureAwait(false); }
                catch (ActorNotFoundException exception) when (exception.DefinitelyNotExecuted) { ExcludeOrThrow<TActor>(target, excluded, ref remainingAttempts); continue; }
            }

            var result = await remote.AskAsync(CreateInvocation(target, actorName, methodName, remoteMethodId, request), cancellationToken).ConfigureAwait(false);
            if (result.Status == RemoteActorStatus.Replied) return serializer.Deserialize<TResult>(result.Payload);
            if (result.RetrySafety == RemoteActorRetrySafety.DefinitelyNotExecuted) { LogExcluded<TActor>(target, result); ExcludeOrThrow<TActor>(target, excluded, ref remainingAttempts); continue; }
            RemoteActorCall.EnsureReplied(result, target.ActorId, actorName, methodName, target.Node);
        }
    }

    public async ValueTask PostAsync<TActor, TKey, TRequest>(TKey key, string actorName, string methodName, ulong remoteMethodId, TRequest request, Func<ActorId, TRequest, CancellationToken, ValueTask<ActorTellResult>> invokeLocal, CancellationToken cancellationToken = default) where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(invokeLocal);
        var excluded = new HashSet<(NodeId, long)>();
        int? remainingAttempts = null;
        while (true)
        {
            var selection = await SelectAsync<TActor, TKey>(key, actorName, excluded, cancellationToken).ConfigureAwait(false);
            var target = selection.Target;
            remainingAttempts ??= selection.CandidateCount;
            if (target.Node == localNode.NodeId)
            {
                var result = await invokeLocal(target.ActorId, request, cancellationToken).ConfigureAwait(false);
                if (result == ActorTellResult.Accepted) return;
                if (result == ActorTellResult.ActorNotFound) { ExcludeOrThrow<TActor>(target, excluded, ref remainingAttempts); continue; }
                throw new InvalidOperationException($"Startup Actor post was not accepted: {result}.");
            }

            var remoteResult = await remote.TellAsync(CreateInvocation(target, actorName, methodName, remoteMethodId, request), cancellationToken).ConfigureAwait(false);
            if (remoteResult.Status == RemoteActorStatus.Accepted) return;
            if (remoteResult.RetrySafety == RemoteActorRetrySafety.DefinitelyNotExecuted) { LogExcluded<TActor>(target, remoteResult); ExcludeOrThrow<TActor>(target, excluded, ref remainingAttempts); continue; }
            RemoteActorCall.EnsureAccepted(remoteResult, target.ActorId, actorName, methodName, target.Node);
        }
    }

    private async ValueTask<(StartupActorTarget Target, int CandidateCount)> SelectAsync<TActor, TKey>(TKey key, string actorName, HashSet<(NodeId, long)> excluded, CancellationToken cancellationToken) where TActor : class, IActor
    {
        var registeredActorName = ActorNameResolver.Resolve(typeof(TActor));
        if (!string.Equals(actorName, registeredActorName, StringComparison.Ordinal))
            throw new StartupActorSelectionException(typeof(TActor), $"Startup Actor name '{actorName}' does not match registered actor name '{registeredActorName}'.");
        using var lease = hotfixRuntime.AcquireCurrent();
        var snapshot = lease.Snapshot;
        var declarations = snapshot.ActorStartups.Where(static declaration => !declaration.IsLegacy && declaration.ActorType == typeof(TActor)).ToArray();
        if (declarations.Length != 1 || declarations[0].KeyType != typeof(TKey))
            throw new StartupActorSelectionException(typeof(TActor), $"Startup Actor registration for '{typeof(TActor).FullName}' does not use key type '{typeof(TKey).FullName}'.");

        var policy = StartupActorIdentity.CreatePolicyHash(typeof(TActor), typeof(TKey));
        var buildTag = StartupActorIdentity.NormalizeBuildTag(snapshot.SourceVersion);
        var records = await nodeDirectory.QueryAsync(new NodeDirectoryQuery(clusterOptions.ClusterName, state: NodeState.Ready, startupActorName: actorName, startupActorPolicyHash: policy), DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        var candidates = records
            .Where(record => record.State == NodeState.Ready && !record.IsExpired(DateTimeOffset.UtcNow) && !excluded.Contains((record.NodeId, record.NodeEpoch)))
            .Select(record => (Record: record, Descriptor: record.StartupActors.SingleOrDefault(descriptor => descriptor.Actor == actorName && descriptor.PolicyHash == policy && descriptor.BuildTag == buildTag)))
            .Where(static pair => pair.Descriptor is not null)
            .OrderBy(static pair => pair.Record.NodeId.Value, StringComparer.Ordinal)
            .Select(static pair => new StartupActorCandidate(pair.Record.NodeId.Value, pair.Record.NodeEpoch, pair.Descriptor!.Metadata))
            .ToArray();
        if (candidates.Length == 0) throw new StartupActorUnavailableException(typeof(TActor));

        StartupActorCandidate selected;
        try { selected = ((Func<StartupActorSelectionContext<TKey>, StartupActorCandidate>)declarations[0].Selector!)(new StartupActorSelectionContext<TKey>(candidates, key)); }
        catch (Exception exception) when (exception is not StartupActorSelectionException) { throw new StartupActorSelectionException(typeof(TActor), $"Startup Actor selector for '{typeof(TActor).FullName}' failed.", exception); }
        if (selected is null || !candidates.Any(candidate => ReferenceEquals(candidate, selected)))
            throw new StartupActorSelectionException(typeof(TActor), $"Startup Actor selector for '{typeof(TActor).FullName}' returned a candidate that was not offered.");
        var node = new NodeId(selected.NodeId);
        return (new StartupActorTarget(StartupActorIdentity.CreateReplicaId(actorName, node), node, selected.NodeEpoch), candidates.Length);
    }

    private static void ExcludeOrThrow<TActor>(StartupActorTarget target, HashSet<(NodeId, long)> excluded, ref int? remainingAttempts)
    {
        excluded.Add((target.Node, target.NodeEpoch));
        remainingAttempts--;
        if (remainingAttempts <= 0) throw new StartupActorUnavailableException(typeof(TActor));
    }

    private void LogExcluded<TActor>(StartupActorTarget target, RemoteActorInvocationResult result)
    {
        logger?.LogWarning(
            "Startup Actor attempt for {ActorType} on {NodeId} epoch {NodeEpoch} was definitely not executed ({Status}): {Error}",
            typeof(TActor).FullName,
            target.Node.Value,
            target.NodeEpoch,
            result.Status,
            result.Message);
    }

    private RemoteActorInvocation CreateInvocation<TRequest>(StartupActorTarget target, string actorName, string methodName, ulong remoteMethodId, TRequest request)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        return new RemoteActorInvocation(target.Node, target.ActorId, actorName, methodName, serializer.Serialize(request), DateTimeOffset.UtcNow.Add(remoteOptions.DefaultTimeout), correlationId,
            new Dictionary<string, string> { [HotfixActorApiMetadata.MethodIdKey] = remoteMethodId.ToString(CultureInfo.InvariantCulture) }, target.NodeEpoch);
    }
}
