using System.Reflection;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Actors;

internal sealed class StartupActorHostedService(
    ActorHosting actorHosting,
    IServiceProvider services,
    LakonaGameRuntimeOptions options,
    LocalActorNodeIdentity localNode,
    StartupActorDescriptorCatalog catalog,
    IClusterNodeRegistrationRefresher refresher,
    ILogger<StartupActorHostedService>? logger = null) : IHostedService
{
    private static readonly MethodInfo EnsureMethod = FindGeneric(nameof(ActorHosting.EnsureAsync));
    private static readonly MethodInfo DestroyMethod = FindGeneric(nameof(ActorHosting.DestroyAsync));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StartupActorDescriptorCatalog _catalog = catalog;
    private readonly IClusterNodeRegistrationRefresher _refresher = refresher;
    private Dictionary<Type, Replica> _active = [];
    private readonly List<Replica> _cleanupPending = [];
    private bool _started;

    internal bool IsStarted => Volatile.Read(ref _started);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accessor = services.GetService<IHotfixRuntimeAccessor>();
            if (accessor is null)
            {
                return;
            }
            using var lease = accessor.AcquireCurrent();
            var desired = CreateReplicas(lease.Snapshot);
            var started = new List<Replica>();
            try
            {
                foreach (var replica in desired.Values)
                {
                    await EnsureAsync(replica, cancellationToken).ConfigureAwait(false);
                    started.Add(replica);
                    logger?.LogInformation(
                        "Startup Actor replica {ActorType} is ready at {ActorId}.",
                        replica.ActorType.FullName,
                        replica.ActorId.Value);
                }
            }
            catch
            {
                foreach (var replica in started.AsEnumerable().Reverse())
                    await DestroyQuietlyAsync(replica).ConfigureAwait(false);
                throw;
            }

            _active = desired;
            _catalog.Replace(desired.Values.Select(static replica => replica.Descriptor));
            Volatile.Write(ref _started, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _catalog.Replace([]);
            await _refresher.RefreshAsync(cancellationToken).ConfigureAwait(false);
            foreach (var replica in _active.Values.Concat(_cleanupPending).Distinct().Reverse())
            {
                try { await DestroyAsync(replica, cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) { logger?.LogError(exception, "Failed to stop Startup Actor replica {ActorType}.", replica.ActorType.FullName); }
            }
            _active = [];
            _cleanupPending.Clear();
            Volatile.Write(ref _started, false);
        }
        finally { _gate.Release(); }
    }

    internal async ValueTask<IHotfixRuntimePublicationTransaction> PrepareAsync(
        HotfixRuntimeSnapshot previous,
        HotfixRuntimeSnapshot candidate,
        CancellationToken cancellationToken)
    {
        if (!IsStarted) return NoopHotfixRuntimePublicationTransaction.Instance;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = _active;
            var after = CreateReplicas(candidate);
            var removed = before.Where(pair => !after.ContainsKey(pair.Key)).Select(static pair => pair.Value).ToArray();
            var added = after.Where(pair => !before.ContainsKey(pair.Key)).Select(static pair => pair.Value).ToArray();
            try
            {
                _catalog.Replace(before.Values.Where(replica => !removed.Contains(replica)).Select(static replica => replica.Descriptor));
                await _refresher.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _catalog.Replace(before.Values.Select(static replica => replica.Descriptor));
                try { await _refresher.RefreshAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                throw;
            }
            return new PublicationTransaction(this, before, after, added, removed);
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    private Dictionary<Type, Replica> CreateReplicas(HotfixRuntimeSnapshot snapshot)
    {
        var capable = new HashSet<string>(options.ActorHosts, StringComparer.OrdinalIgnoreCase);
        return snapshot.ActorStartups
            .Where(static declaration => !declaration.IsLegacy)
            .Where(declaration => capable.Contains(ActorNameResolver.Resolve(declaration.ActorType!)))
            .Select(declaration => CreateReplica(declaration, snapshot.SourceVersion))
            .ToDictionary(static replica => replica.ActorType);
    }

    private Replica CreateReplica(ActorStartupDeclaration declaration, string? buildTag)
    {
        var actorType = declaration.ActorType!;
        var actorName = ActorNameResolver.Resolve(actorType);
        return new Replica(
            actorType,
            StartupActorIdentity.CreateReplicaId(actorName, localNode.NodeId),
            new StartupActorDescriptor(
                actorName,
                StartupActorIdentity.CreatePolicyHash(actorType, declaration.KeyType!),
                StartupActorIdentity.NormalizeBuildTag(buildTag)));
    }

    private ValueTask EnsureAsync(Replica replica, CancellationToken cancellationToken) =>
        (ValueTask)EnsureMethod.MakeGenericMethod(replica.ActorType).Invoke(actorHosting, [replica.ActorId, cancellationToken])!;

    private ValueTask DestroyAsync(Replica replica, CancellationToken cancellationToken) =>
        (ValueTask)DestroyMethod.MakeGenericMethod(replica.ActorType).Invoke(actorHosting, [replica.ActorId, cancellationToken])!;

    private async ValueTask<bool> DestroyQuietlyAsync(Replica replica)
    {
        try { await DestroyAsync(replica, CancellationToken.None).ConfigureAwait(false); return true; }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Failed to clean up Startup Actor replica {ActorType}.", replica.ActorType.FullName);
            return false;
        }
    }

    private static MethodInfo FindGeneric(string name) => typeof(ActorHosting).GetMethods()
        .Single(method => method.Name == name && method.IsGenericMethodDefinition && method.GetParameters().Length == 2);

    private sealed record Replica(Type ActorType, ActorId ActorId, StartupActorDescriptor Descriptor);

    private sealed class PublicationTransaction(
        StartupActorHostedService owner,
        Dictionary<Type, Replica> before,
        Dictionary<Type, Replica> after,
        IReadOnlyList<Replica> added,
        IReadOnlyList<Replica> removed) : IHotfixRuntimePublicationTransaction
    {
        private readonly List<Replica> _started = [];
        private int _disposed;

        public async ValueTask ActivateAsync(CancellationToken cancellationToken = default)
        {
            foreach (var replica in added)
            {
                await owner.EnsureAsync(replica, cancellationToken).ConfigureAwait(false);
                _started.Add(replica);
            }
            owner._catalog.Replace(after.Values.Select(static replica => replica.Descriptor));
            await owner._refresher.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            owner._active = after;
            foreach (var replica in removed)
                if (!await owner.DestroyQuietlyAsync(replica).ConfigureAwait(false)) owner._cleanupPending.Add(replica);
        }

        public async ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            foreach (var replica in _started.AsEnumerable().Reverse())
                if (!await owner.DestroyQuietlyAsync(replica).ConfigureAwait(false)) owner._cleanupPending.Add(replica);
            owner._active = before;
            owner._catalog.Replace(before.Values.Select(static replica => replica.Descriptor));
            try { await owner._refresher.RefreshAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner._gate.Release();
            return default;
        }
    }
}
