using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Actors;

/// <summary>
/// Creates and destroys local actors while keeping the actor directory and local cache consistent.
/// </summary>
/// <remarks>
/// Use this service from startup hooks, hotfix feature lifecycle hooks, or game
/// services that explicitly own actor lifetime. Ordinary gameplay calls should
/// normally use generated actor references instead of manually touching actor
/// directory or cache services.
/// </remarks>
public sealed class ActorHosting
{
    private static readonly TimeSpan DestroyDrainTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RollbackDrainTimeout = TimeSpan.FromMilliseconds(100);

    private readonly IActorHostingRuntime _runtime;
    private readonly IActorDirectory _directory;
    private readonly IActorDirectoryCache _directoryCache;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ActorHostingRollbackRecorder _rollbackRecorder;
    private readonly IActorLifecycleDispatcher _lifecycleDispatcher;
    private readonly ActorHostingOperationGate _operationGate = new();
    private readonly ILogger<ActorHosting>? _logger;

    internal ActorHosting(
        IActorHostingRuntime runtime,
        IActorDirectory directory,
        IActorDirectoryCache directoryCache,
        LocalActorNodeIdentity localNode,
        ActorHostingRollbackRecorder rollbackRecorder,
        IActorLifecycleDispatcher? lifecycleDispatcher = null,
        ILogger<ActorHosting>? logger = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _directoryCache = directoryCache ?? throw new ArgumentNullException(nameof(directoryCache));
        _localNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        _rollbackRecorder = rollbackRecorder ?? throw new ArgumentNullException(nameof(rollbackRecorder));
        _lifecycleDispatcher = lifecycleDispatcher ?? new NoopActorLifecycleDispatcher();
        _logger = logger;
    }

    /// <summary>
    /// Creates a new local actor with the specified id.
    /// </summary>
    /// <typeparam name="TActor">The actor implementation type to host locally.</typeparam>
    /// <param name="actorId">The stable actor id.</param>
    /// <param name="cancellationToken">A token that cancels route registration or local actor creation.</param>
    /// <remarks>
    /// This method is strict: it fails when the same actor id is already hosted
    /// locally or registered to another node. Use <see cref="EnsureAsync{TActor}"/>
    /// when idempotent startup is desired.
    /// </remarks>
    public async ValueTask CreateAsync<TActor>(
        ActorId actorId,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        await using var gate = await _operationGate.EnterAsync(actorId, cancellationToken).ConfigureAwait(false);
        await CreateCoreAsync(typeof(TActor), actorId, strict: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures that a local actor with the specified id exists and is active.
    /// </summary>
    /// <typeparam name="TActor">The actor implementation type that must be hosted.</typeparam>
    /// <param name="actorId">The stable actor id.</param>
    /// <param name="cancellationToken">A token that cancels route registration or local actor creation.</param>
    /// <remarks>
    /// This method is idempotent for an already active local actor of the exact
    /// requested type. It still fails when the id is hosted by a different actor
    /// type, is in a non-active local state, or is registered to another node.
    /// </remarks>
    public async ValueTask EnsureAsync<TActor>(
        ActorId actorId,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        var actorType = typeof(TActor);
        await using var gate = await _operationGate.EnterAsync(actorId, cancellationToken).ConfigureAwait(false);

        if (_runtime.TryGetLocalActor(actorId, out var existingActorType, out var state))
        {
            if (!IsExactActorType(existingActorType, actorType))
            {
                throw new ActorHostingTypeMismatchException(actorId, actorType, existingActorType, nameof(EnsureAsync));
            }

            if (state != ActorState.Active)
            {
                throw new ActorHostingStopException(
                    actorId,
                    actorType,
                    nameof(EnsureAsync),
                    $"Actor id '{actorId.Value}' is locally hosted but not active.");
            }

            if (!IsLocalOnly(actorType))
            {
                await EnsureLocalRouteAsync(actorType, actorId, nameof(EnsureAsync), cancellationToken).ConfigureAwait(false);
                _directoryCache.Set(actorId, _localNode.NodeId);
            }

            return;
        }

        await CreateCoreAsync(actorType, actorId, strict: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops a locally hosted actor and removes its local actor-directory route.
    /// </summary>
    /// <typeparam name="TActor">The actor implementation type expected for the id.</typeparam>
    /// <param name="actorId">The stable actor id.</param>
    /// <param name="cancellationToken">A token that cancels route removal or local actor stop.</param>
    /// <remarks>
    /// Destroying a missing actor is treated as success. If the id is currently
    /// hosted by a different actor type, the method fails instead of stopping the
    /// wrong actor.
    /// </remarks>
    public async ValueTask DestroyAsync<TActor>(
        ActorId actorId,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        var actorType = typeof(TActor);
        await using var gate = await _operationGate.EnterAsync(actorId, cancellationToken).ConfigureAwait(false);

        if (_runtime.TryGetLocalActor(actorId, out var existingActorType, out _) &&
            !IsExactActorType(existingActorType, actorType))
        {
            throw new ActorHostingTypeMismatchException(actorId, actorType, existingActorType, nameof(DestroyAsync));
        }

        var localRouteRemoved = false;
        if (!IsLocalOnly(actorType))
        {
            var unregisterStatus = await _directory
                .UnregisterAsync(actorId, _localNode.NodeId, cancellationToken)
                .ConfigureAwait(false);
            localRouteRemoved = unregisterStatus == ActorDirectoryUnregisterStatus.Unregistered;
            _directoryCache.Remove(actorId);
        }

        if (_runtime.TryGetLocalActor(actorId, out existingActorType, out var existingState) &&
            IsExactActorType(existingActorType, actorType) &&
            existingState != ActorState.Dead &&
            _lifecycleDispatcher.HasStopHook(actorType))
        {
            try
            {
                await _runtime
                    .InvokeLocalAsync(
                        actorType,
                        actorId,
                        async (actor, ct) =>
                        {
                            await _lifecycleDispatcher
                                .StopAsync(actorType, actorId, actor, ct)
                                .ConfigureAwait(false);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (localRouteRemoved && IsStillHosted(actorType, actorId))
                {
                    await RestoreLocalRouteAsync(actorId, CancellationToken.None).ConfigureAwait(false);
                }

                throw new ActorHostingStopException(
                    actorId,
                    actorType,
                    nameof(DestroyAsync),
                    $"Failed while running actor stop hook for actor id '{actorId.Value}' as '{actorType.FullName}'.",
                    ex);
            }
        }

        ActorHostingLocalDestroyResult destroyResult;
        try
        {
            destroyResult = await _runtime
                .DestroyLocalAsync(actorType, actorId, DestroyDrainTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (localRouteRemoved && IsStillHosted(actorType, actorId))
            {
                await RestoreLocalRouteAsync(actorId, CancellationToken.None).ConfigureAwait(false);
            }

            throw new ActorHostingStopException(
                actorId,
                actorType,
                nameof(DestroyAsync),
                $"Failed while stopping actor id '{actorId.Value}' as '{actorType.FullName}'.",
                ex);
        }

        if (destroyResult.Status == ActorHostingLocalDestroyStatus.TypeMismatch)
        {
            if (localRouteRemoved && IsStillHosted(actorType, actorId))
            {
                await RestoreLocalRouteAsync(actorId, CancellationToken.None).ConfigureAwait(false);
            }

            throw new ActorHostingTypeMismatchException(
                actorId,
                actorType,
                destroyResult.ExistingActorType ?? typeof(IActor),
                nameof(DestroyAsync));
        }

        if (destroyResult.Status == ActorHostingLocalDestroyStatus.TimedOut)
        {
            if (localRouteRemoved && IsStillHosted(actorType, actorId))
            {
                await RestoreLocalRouteAsync(actorId, CancellationToken.None).ConfigureAwait(false);
            }

            throw new ActorHostingStopException(
                actorId,
                actorType,
                nameof(DestroyAsync),
                $"Timed out while stopping actor id '{actorId.Value}' as '{actorType.FullName}'.");
        }

        if (destroyResult.Status == ActorHostingLocalDestroyStatus.Destroyed)
        {
            _rollbackRecorder.RecordDestroyed(actorType, actorId);
        }
    }

    private async ValueTask CreateCoreAsync(
        Type actorType,
        ActorId actorId,
        bool strict,
        CancellationToken cancellationToken)
    {
        if (_runtime.TryGetLocalActor(actorId, out var existingActorType, out var state))
        {
            if (!IsExactActorType(existingActorType, actorType))
            {
                throw new ActorHostingTypeMismatchException(
                    actorId,
                    actorType,
                    existingActorType,
                    strict ? nameof(CreateAsync) : nameof(EnsureAsync));
            }

            if (state != ActorState.Active)
            {
                throw new ActorHostingStopException(
                    actorId,
                    actorType,
                    strict ? nameof(CreateAsync) : nameof(EnsureAsync),
                    $"Actor id '{actorId.Value}' is locally hosted but not active.");
            }

            if (strict)
            {
                throw new ActorAlreadyHostedException(actorId, actorType, nameof(CreateAsync));
            }

            return;
        }

        var registeredByThisCall = false;
        var localCreated = false;
        try
        {
            var createResult = await _runtime
                .CreateLocalAsync(actorType, actorId, cancellationToken)
                .ConfigureAwait(false);

            switch (createResult.Status)
            {
                case ActorHostingLocalCreateStatus.Created:
                    localCreated = true;
                    if (_lifecycleDispatcher.HasStartHook(actorType))
                    {
                        await _runtime
                            .InvokeLocalAsync(
                                actorType,
                                actorId,
                                async (actor, ct) =>
                                {
                                    await _lifecycleDispatcher
                                        .StartAsync(actorType, actorId, actor, ct)
                                        .ConfigureAwait(false);
                                },
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    break;
                case ActorHostingLocalCreateStatus.AlreadyExistsSameType when strict:
                    throw new ActorAlreadyHostedException(actorId, actorType, nameof(CreateAsync));
                case ActorHostingLocalCreateStatus.AlreadyExistsSameType:
                    break;
                case ActorHostingLocalCreateStatus.AlreadyExistsDifferentType:
                    throw new ActorHostingTypeMismatchException(
                        actorId,
                        actorType,
                        createResult.ExistingActorType ?? typeof(IActor),
                        strict ? nameof(CreateAsync) : nameof(EnsureAsync));
            }

            if (!IsLocalOnly(actorType))
            {
                registeredByThisCall = await RegisterLocalRouteAsync(
                    actorType,
                    actorId,
                    strict ? nameof(CreateAsync) : nameof(EnsureAsync),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!IsLocalOnly(actorType))
            {
                _directoryCache.Set(actorId, _localNode.NodeId);
            }

            if (localCreated)
            {
                _rollbackRecorder.RecordCreated(actorType, actorId);
            }
        }
        catch
        {
            _directoryCache.Remove(actorId);

            var cleanupActorType = actorType;
            var shouldCleanupLocal = localCreated;

            if (shouldCleanupLocal)
            {
                try
                {
                    await _runtime
                        .DestroyLocalAsync(cleanupActorType, actorId, RollbackDrainTimeout, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to roll back local actor {ActorId}.", actorId.Value);
                }
            }

            if (registeredByThisCall)
            {
                try
                {
                    await _directory
                        .UnregisterAsync(actorId, _localNode.NodeId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to roll back actor directory route for {ActorId}.", actorId.Value);
                }
            }

            throw;
        }
    }

    private bool IsStillHosted(Type actorType, ActorId actorId)
    {
        return _runtime.TryGetLocalActor(actorId, out var existingActorType, out var state) &&
            IsExactActorType(existingActorType, actorType) &&
            state != ActorState.Dead;
    }

    private async ValueTask EnsureLocalRouteAsync(
        Type actorType,
        ActorId actorId,
        string operation,
        CancellationToken cancellationToken)
    {
        var registerStatus = await _directory
            .RegisterAsync(actorId, _localNode.NodeId, cancellationToken)
            .ConfigureAwait(false);

        if (registerStatus == ActorDirectoryRegisterStatus.Conflict)
        {
            var record = await _directory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                _directoryCache.Remove(actorId);
                throw new ActorDirectoryUnavailableException(
                    actorId,
                    actorType,
                    operation,
                    _localNode.NodeId,
                    "Actor directory returned a conflicting state without a resolvable owner.");
            }

            if (record.Node != _localNode.NodeId)
            {
                _directoryCache.Remove(actorId);
                throw new ActorHostedElsewhereException(actorId, actorType, operation, _localNode.NodeId, record.Node);
            }
        }
    }

    private async ValueTask<bool> RegisterLocalRouteAsync(
        Type actorType,
        ActorId actorId,
        string operation,
        CancellationToken cancellationToken)
    {
        var registerStatus = await _directory
            .RegisterAsync(actorId, _localNode.NodeId, cancellationToken)
            .ConfigureAwait(false);

        if (registerStatus == ActorDirectoryRegisterStatus.Registered)
        {
            return true;
        }

        var record = await _directory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            throw new ActorDirectoryUnavailableException(
                actorId,
                actorType,
                operation,
                _localNode.NodeId,
                "Actor directory returned a conflicting state without a resolvable owner.");
        }

        if (record.Node != _localNode.NodeId)
        {
            _directoryCache.Remove(actorId);
            throw new ActorHostedElsewhereException(actorId, actorType, operation, _localNode.NodeId, record.Node);
        }

        return false;
    }

    private async ValueTask RestoreLocalRouteAsync(ActorId actorId, CancellationToken cancellationToken)
    {
        try
        {
            var registerStatus = await _directory
                .RegisterAsync(actorId, _localNode.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (registerStatus == ActorDirectoryRegisterStatus.Registered)
            {
                _directoryCache.Set(actorId, _localNode.NodeId);
                return;
            }

            var record = await _directory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
            if (record is not null && record.Node == _localNode.NodeId)
            {
                _directoryCache.Set(actorId, _localNode.NodeId);
                return;
            }

            _directoryCache.Remove(actorId);
            if (record is null)
            {
                _logger?.LogWarning("Actor route restore for {ActorId} conflicted without a resolvable owner.", actorId.Value);
            }
            else
            {
                _logger?.LogWarning(
                    "Actor route restore for {ActorId} found remote owner {OwnerNode}.",
                    actorId.Value,
                    record.Node.Value);
            }
        }
        catch (Exception ex)
        {
            _directoryCache.Remove(actorId);
            _logger?.LogWarning(ex, "Failed to restore actor route for {ActorId}.", actorId.Value);
        }
    }

    private static bool IsLocalOnly(Type actorType)
    {
        return actorType.GetCustomAttributes(typeof(ActorLocalOnlyAttribute), inherit: false).Length > 0;
    }

    private static bool IsExactActorType(Type existingActorType, Type requestedActorType)
    {
        return existingActorType == requestedActorType;
    }
}
