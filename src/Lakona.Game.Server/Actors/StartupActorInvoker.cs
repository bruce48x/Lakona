using System.Security.Cryptography;
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Actors;

internal sealed class StartupActorInvoker(
    IHotfixRuntimeAccessor hotfixRuntime,
    ClusterCapabilityIndex? capabilityIndex,
    LocalActorNodeIdentity localNode,
    IRemoteActorInvoker remote,
    RemoteActorOptions remoteOptions,
    ILogger<StartupActorInvoker>? logger = null,
    IStartupActorAffinityDirectory? affinityDirectory = null,
    IActorActivationDirectory? activationDirectory = null,
    IActorDirectory? actorDirectory = null,
    IClusterMembership? membership = null) : IStartupActorInvoker
{
    public async ValueTask CallAsync<TActor, TKey, TRequest>(TKey key, string actorName, string methodName, ulong remoteMethodId, TRequest request, Func<ActorId, TRequest, CancellationToken, ValueTask> invokeLocal, CancellationToken cancellationToken = default) where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(invokeLocal);
        var excluded = new HashSet<NodeId>();
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
        var excluded = new HashSet<NodeId>();
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

            var result = await remote.AskAsync(CreateInvocation<TRequest, TResult>(target, actorName, methodName, remoteMethodId, request), cancellationToken).ConfigureAwait(false);
            if (result.Status == RemoteActorStatus.Replied)
                return RemoteActorCall.GetReply<TResult>(result, target.ActorId, actorName, methodName, target.Node);
            if (result.RetrySafety == RemoteActorRetrySafety.DefinitelyNotExecuted) { LogExcluded<TActor>(target, result); ExcludeOrThrow<TActor>(target, excluded, ref remainingAttempts); continue; }
            RemoteActorCall.EnsureReplied(result, target.ActorId, actorName, methodName, target.Node);
        }
    }

    public async ValueTask PostAsync<TActor, TKey, TRequest>(TKey key, string actorName, string methodName, ulong remoteMethodId, TRequest request, Func<ActorId, TRequest, CancellationToken, ValueTask<ActorTellResult>> invokeLocal, CancellationToken cancellationToken = default) where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(invokeLocal);
        var excluded = new HashSet<NodeId>();
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

    private async ValueTask<(StartupActorTarget Target, int CandidateCount)> SelectAsync<TActor, TKey>(TKey key, string actorName, HashSet<NodeId> excluded, CancellationToken cancellationToken) where TActor : class, IActor
    {
        var registeredActorName = ActorNameResolver.Resolve(typeof(TActor));
        if (!string.Equals(actorName, registeredActorName, StringComparison.Ordinal))
            throw new StartupActorSelectionException(typeof(TActor), $"Startup Actor name '{actorName}' does not match registered actor name '{registeredActorName}'.");
        using var lease = hotfixRuntime.AcquireCurrent();
        var snapshot = lease.Snapshot;
        var declarations = snapshot.ActorStartups.Where(static declaration => declaration.ActorType == typeof(TActor)).ToArray();
        if (declarations.Length != 1 || declarations[0].KeyType != typeof(TKey))
            throw new StartupActorSelectionException(typeof(TActor), $"Startup Actor registration for '{typeof(TActor).FullName}' does not use key type '{typeof(TKey).FullName}'.");

        var policy = StartupActorIdentity.CreatePolicyHash(typeof(TActor), typeof(TKey));
        var buildTag = StartupActorIdentity.NormalizeBuildTag(snapshot.SourceVersion);
        var candidates = capabilityIndex is null
            ? [new StartupActorCandidate(localNode.NodeId.Value, new Dictionary<string, string>())]
            : capabilityIndex.FindReadyStartupActors(actorName, policy, buildTag)
                .Where(record => !excluded.Contains(record.Node))
                .Select(static record => new StartupActorCandidate(record.Node.Value, record.Startup.Metadata))
                .ToArray();
        if (candidates.Length == 0) throw new StartupActorUnavailableException(typeof(TActor));

        if (affinityDirectory is not null && activationDirectory is not null && actorDirectory is not null && membership is not null)
        {
            return await SelectStickyAsync<TActor, TKey>(
                key,
                actorName,
                declarations[0],
                candidates,
                affinityDirectory,
                activationDirectory,
                actorDirectory,
                membership,
                policy,
                buildTag,
                cancellationToken).ConfigureAwait(false);
        }

        StartupActorCandidate selected;
        try { selected = ((Func<StartupActorSelectionContext<TKey>, StartupActorCandidate>)declarations[0].Selector!)(new StartupActorSelectionContext<TKey>(candidates, key)); }
        catch (Exception exception) when (exception is not StartupActorSelectionException) { throw new StartupActorSelectionException(typeof(TActor), $"Startup Actor selector for '{typeof(TActor).FullName}' failed.", exception); }
        if (selected is null || !candidates.Any(candidate => ReferenceEquals(candidate, selected)))
            throw new StartupActorSelectionException(typeof(TActor), $"Startup Actor selector for '{typeof(TActor).FullName}' returned a candidate that was not offered.");
        var node = new NodeId(selected.NodeId);
        return (new StartupActorTarget(StartupActorIdentity.CreateReplicaId(actorName, node), node), candidates.Length);
    }

    private static async ValueTask<(StartupActorTarget Target, int CandidateCount)> SelectStickyAsync<TActor, TKey>(
        TKey key,
        string actorName,
        ActorStartupDeclaration declaration,
        IReadOnlyList<StartupActorCandidate> candidates,
        IStartupActorAffinityDirectory affinityDirectory,
        IActorActivationDirectory activationDirectory,
        IActorDirectory actorDirectory,
        IClusterMembership membership,
        string policyHash,
        string buildTag,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        var affinityId = CreateAffinityId(actorName, policyHash, buildTag, key);
        var existing = await affinityDirectory.LookupAsync(affinityId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            var snapshot = membership.Current;
            if (snapshot.TryGetMember(existing.Target, out var existingMember)
                && existingMember!.State == ClusterMemberState.Ready
                && existingMember.StartupActors.Any(startup =>
                    string.Equals(startup.Actor, actorName, StringComparison.Ordinal)
                    && string.Equals(startup.PolicyHash, policyHash, StringComparison.Ordinal)
                    && string.Equals(startup.BuildTag, buildTag, StringComparison.Ordinal)))
            {
                return await ToStickyTargetAsync<TActor>(
                    actorName,
                    existing.Target,
                    candidates,
                    activationDirectory,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        StartupActorCandidate selected;
        try
        {
            selected = ((Func<StartupActorSelectionContext<TKey>, StartupActorCandidate>)declaration.Selector!)(
                new StartupActorSelectionContext<TKey>(candidates, key));
        }
        catch (Exception exception) when (exception is not StartupActorSelectionException)
        {
            throw new StartupActorSelectionException(
                typeof(TActor),
                $"Startup Actor selector for '{typeof(TActor).FullName}' failed.",
                exception);
        }

        if (selected is null || !candidates.Any(candidate => ReferenceEquals(candidate, selected)))
        {
            throw new StartupActorSelectionException(
                typeof(TActor),
                $"Startup Actor selector for '{typeof(TActor).FullName}' returned a candidate that was not offered.");
        }

        var owner = membership.Current.Members.SingleOrDefault(member =>
            member.State == ClusterMemberState.Ready
            && string.Equals(member.Reference.Node.Value, selected.NodeId, StringComparison.Ordinal)
            && member.StartupActors.Any(startup =>
                string.Equals(startup.Actor, actorName, StringComparison.Ordinal)
                && string.Equals(startup.PolicyHash, policyHash, StringComparison.Ordinal)
                && string.Equals(startup.BuildTag, buildTag, StringComparison.Ordinal)));
        if (owner is null)
        {
            throw new StartupActorUnavailableException(typeof(TActor));
        }

        var acquired = await affinityDirectory.BindAsync(
            affinityId,
            owner.Reference,
            actorName,
            policyHash,
            buildTag,
            cancellationToken).ConfigureAwait(false);
        return await ToStickyTargetAsync<TActor>(
            actorName,
            acquired.Target,
            candidates,
            activationDirectory,
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<(StartupActorTarget Target, int CandidateCount)> ToStickyTargetAsync<TActor>(
        string actorName,
        NodeReference affinityTarget,
        IReadOnlyList<StartupActorCandidate> candidates,
        IActorActivationDirectory activationDirectory,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        var candidate = candidates.SingleOrDefault(item =>
            string.Equals(item.NodeId, affinityTarget.Node.Value, StringComparison.Ordinal));
        if (candidate is null)
        {
            throw new StartupActorUnavailableException(typeof(TActor));
        }

        var node = affinityTarget.Node;
        var replicaId = StartupActorIdentity.CreateReplicaId(actorName, node);
        var replicaActivation = await activationDirectory.AcquireAsync(
            replicaId,
            affinityTarget,
            ActorActivationId.New(),
            cancellationToken).ConfigureAwait(false);
        if (replicaActivation.Record.OwnerReference != affinityTarget)
        {
            throw new StartupActorUnavailableException(typeof(TActor));
        }

        return (
            new StartupActorTarget(
                replicaId,
                node,
                replicaActivation.Record),
            candidates.Count);
    }

    private static ActorId CreateAffinityId<TKey>(string actorName, string policyHash, string buildTag, TKey key)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(key);
        var digest = Convert.ToHexString(SHA256.HashData(payload));
        return ActorId.From($"@startup-affinity/{actorName}/{policyHash}/{buildTag}/{digest}");
    }

    private static void ExcludeOrThrow<TActor>(StartupActorTarget target, HashSet<NodeId> excluded, ref int? remainingAttempts)
    {
        excluded.Add(target.Node);
        remainingAttempts--;
        if (remainingAttempts <= 0) throw new StartupActorUnavailableException(typeof(TActor));
    }

    private void LogExcluded<TActor>(StartupActorTarget target, RemoteActorInvocationResult result)
    {
        logger?.LogWarning(
            "Startup Actor attempt for {ActorType} on {NodeId} was definitely not executed ({Status}): {Error}",
            typeof(TActor).FullName,
            target.Node.Value,
            result.Status,
            result.Message);
    }

    private RemoteActorInvocation CreateInvocation<TRequest>(StartupActorTarget target, string actorName, string methodName, ulong remoteMethodId, TRequest request)
    {
        return RemoteActorInvocation.Create(
            target.Node,
            target.ActorId,
            actorName,
            methodName,
            remoteMethodId,
            request,
            DateTimeOffset.UtcNow.Add(remoteOptions.DefaultTimeout),
            expectedNodeEpoch: null,
            target.Activation?.OwnerReference,
            target.Activation?.ActivationId);
    }

    private RemoteActorInvocation CreateInvocation<TRequest, TResult>(
        StartupActorTarget target,
        string actorName,
        string methodName,
        ulong remoteMethodId,
        TRequest request)
    {
        return RemoteActorInvocation.Create<TRequest, TResult>(
            target.Node,
            target.ActorId,
            actorName,
            methodName,
            remoteMethodId,
            request,
            DateTimeOffset.UtcNow.Add(remoteOptions.DefaultTimeout),
            expectedNodeEpoch: null,
            target.Activation?.OwnerReference,
            target.Activation?.ActivationId);
    }
}
