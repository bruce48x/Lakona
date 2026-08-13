using Microsoft.Extensions.Logging;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

/// <summary>
/// Creates and destroys local actors while keeping the actor directory and local cache consistent.
/// </summary>
/// <remarks>
/// Framework startup, placement, remote Host RPC, and hotfix rollback converge
/// on this module. Business code provisions logical actors through generated
/// <c>ActorAccess.Place</c> selectors instead of accessing local hosting,
/// directory, or cache services directly.
/// </remarks>
internal sealed class ActorHosting : IActorPlacementService, IActorSelfDeactivationSink
{
    private static readonly TimeSpan DestroyDrainTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RollbackDrainTimeout = TimeSpan.FromMilliseconds(100);

    private readonly IActorHostingRuntime _runtime;
    private readonly IActorDirectory? _directory;
    private readonly IActorDirectoryCache? _directoryCache;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ActorHostingRollbackRecorder _rollbackRecorder;
    private readonly IActorLifecycleDispatcher _lifecycleDispatcher;
    private readonly ActorHostingOperationGate _operationGate = new();
    private readonly ILogger<ActorHosting>? _logger;
    private readonly ActorActivationRegistry? _activationRegistry;

    internal ActorHosting(
        IActorHostingRuntime runtime,
        LocalActorNodeIdentity localNode,
        ActorHostingRollbackRecorder rollbackRecorder,
        IActorDirectory? directory = null,
        IActorDirectoryCache? directoryCache = null,
        IActorLifecycleDispatcher? lifecycleDispatcher = null,
        ILogger<ActorHosting>? logger = null,
        ActorActivationRegistry? activationRegistry = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _directory = directory;
        _directoryCache = directoryCache;
        _localNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        _rollbackRecorder = rollbackRecorder ?? throw new ArgumentNullException(nameof(rollbackRecorder));
        _lifecycleDispatcher = lifecycleDispatcher ?? new NoopActorLifecycleDispatcher();
        _logger = logger;
        _activationRegistry = activationRegistry;
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

            if (UsesDistributedLocation(actorType))
            {
                await EnsureLocalRouteAsync(actorType, actorId, nameof(EnsureAsync), cancellationToken).ConfigureAwait(false);
                await CacheLocalRouteAsync(actorId, cancellationToken).ConfigureAwait(false);
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
        => await DestroyCoreAsync<TActor>(
                actorId,
                expectedOwner: null,
                expectedActivation: null,
                expectedVersion: 0,
                expectedLocalActor: null,
                cancellationToken)
            .ConfigureAwait(false);

    internal async ValueTask DestroyExactAsync<TActor>(
        ActorId actorId,
        NodeReference expectedOwner,
        ActorActivationId expectedActivation,
        long expectedVersion,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
        => await DestroyCoreAsync<TActor>(
                actorId,
                expectedOwner,
                expectedActivation,
                expectedVersion,
                expectedLocalActor: null,
                cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask DestroySelfAsync<TActor>(
        ActorId actorId,
        object expectedLocalActor,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
        => await DestroyCoreAsync<TActor>(
                actorId,
                expectedOwner: null,
                expectedActivation: null,
                expectedVersion: 0,
                expectedLocalActor,
                cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask DestroyCoreAsync<TActor>(
        ActorId actorId,
        NodeReference? expectedOwner,
        ActorActivationId? expectedActivation,
        long expectedVersion,
        object? expectedLocalActor,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        var actorType = typeof(TActor);
        await using var gate = await _operationGate.EnterAsync(actorId, cancellationToken).ConfigureAwait(false);

        if (expectedLocalActor is not null && !_runtime.IsExactLocalActor(actorId, expectedLocalActor))
        {
            return;
        }

        if (_runtime.TryGetLocalActor(actorId, out var existingActorType, out _) &&
            !IsExactActorType(existingActorType, actorType))
        {
            throw new ActorHostingTypeMismatchException(actorId, actorType, existingActorType, nameof(DestroyAsync));
        }

        ActorDirectoryRecord? registeredRecord = null;
        if (UsesDistributedLocation(actorType))
            registeredRecord = await _directory!.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);

        if (expectedActivation is { } exactActivation
            && (registeredRecord is not { OwnerReference: { } registeredOwner }
                || registeredOwner != expectedOwner
                || registeredRecord.ActivationId != exactActivation
                || registeredRecord.Version != expectedVersion
                || registeredOwner.Node != _localNode.NodeId))
        {
            return;
        }

        ActorHostingLocalRetireResult retireResult;
        try
        {
            retireResult = await _runtime.RetireLocalAsync(
                actorType,
                actorId,
                _lifecycleDispatcher.HasStopHook(actorType)
                    ? (actor, ct) => _lifecycleDispatcher.StopAsync(actorType, actorId, actor, ct)
                    : static (_, _) => default,
                DestroyDrainTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new ActorHostingStopException(
                actorId,
                actorType,
                nameof(DestroyAsync),
                $"Failed while retiring actor id '{actorId.Value}' as '{actorType.FullName}'.",
                ex);
        }

        if (retireResult.Status == ActorHostingLocalRetireStatus.TypeMismatch)
            throw new ActorHostingTypeMismatchException(
                actorId,
                actorType,
                retireResult.ExistingActorType ?? typeof(IActor),
                nameof(DestroyAsync));
        if (retireResult.Status == ActorHostingLocalRetireStatus.TimedOut)
            throw new ActorHostingStopException(
                actorId,
                actorType,
                nameof(DestroyAsync),
                $"Timed out while draining actor id '{actorId.Value}' as '{actorType.FullName}'.");

        var localRouteRemoved = false;
        if (UsesDistributedLocation(actorType))
        {
            _directoryCache?.Remove(actorId);
            // A retired cell is no longer recovery evidence. Withdraw it before
            // the remote release so a concurrent recovery cannot resurrect it.
            // If release fails, the exact Directory claim remains reserved and
            // a later Destroy retry completes the same operation.
            if (registeredRecord?.ActivationId is { } retiringActivation)
                _activationRegistry?.Remove(actorId, retiringActivation);
            if (registeredRecord is { OwnerReference: { } owner, ActivationId: { } activation }
                && owner.Node == _localNode.NodeId
                && _directory is IActorActivationDirectory activationDirectory)
            {
                localRouteRemoved = await activationDirectory
                    .ReleaseAsync(actorId, activation, registeredRecord.Version, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (registeredRecord?.Node == _localNode.NodeId)
            {
                localRouteRemoved = await _directory!
                    .UnregisterAsync(actorId, _localNode.NodeId, cancellationToken)
                    .ConfigureAwait(false) == ActorDirectoryUnregisterStatus.Unregistered;
            }
            _directoryCache?.Remove(actorId);

            // Once the authoritative claim is gone, crash recovery must not be
            // able to publish the retired activation again while local cell
            // cleanup is still in progress.
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
            throw new ActorHostingStopException(
                actorId,
                actorType,
                nameof(DestroyAsync),
                $"Failed while removing retired actor id '{actorId.Value}' as '{actorType.FullName}'.",
                ex);
        }
        if (destroyResult.Status == ActorHostingLocalDestroyStatus.TypeMismatch)
            throw new ActorHostingTypeMismatchException(
                actorId, actorType, destroyResult.ExistingActorType ?? typeof(IActor), nameof(DestroyAsync));
        if (destroyResult.Status == ActorHostingLocalDestroyStatus.TimedOut)
            throw new ActorHostingStopException(
                actorId, actorType, nameof(DestroyAsync),
                $"Timed out while removing retired actor id '{actorId.Value}'.");

        _rollbackRecorder.RecordDestroyed(actorType, actorId);
    }

    async ValueTask IActorSelfDeactivationSink.DeactivateAsync(
        Type actorType,
        ActorId actorId,
        object actor,
        CancellationToken cancellationToken)
    {
        var method = typeof(ActorHosting).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == nameof(DestroySelfAsync) && candidate.IsGenericMethodDefinition);
        await ((ValueTask)method.MakeGenericMethod(actorType)
            .Invoke(this, [actorId, actor, cancellationToken])!).ConfigureAwait(false);
    }

    async ValueTask<ActorPlacementResult> IActorPlacementService.PlaceAsync<TActor, TKey>(
        TKey key,
        ActorPlacementCreateMode createMode,
        CancellationToken cancellationToken) =>
        await ((IActorPlacementService)this).PlaceAsync<TActor, TKey>(
            key is ActorId id ? id : ActorIdentity.Create<TActor, TKey>(key),
            key,
            createMode,
            cancellationToken).ConfigureAwait(false);

    async ValueTask<ActorPlacementResult> IActorPlacementService.PlaceAsync<TActor, TKey>(
        ActorId actorId,
        TKey key,
        ActorPlacementCreateMode createMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        try
        {
            if (createMode == ActorPlacementCreateMode.Create)
            {
                await CreateAsync<TActor>(actorId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await EnsureAsync<TActor>(actorId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ActorHostingException exception)
        {
            throw new ActorPlacementException(typeof(TActor), actorId, exception.Message, exception);
        }

        return new ActorPlacementResult(actorId, _localNode.NodeId);
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
        ActorActivationId? registeredActivation = null;
        var localCreated = false;
        try
        {
            // Publish the exact activation claim before an executable mailbox exists. A losing
            // creator therefore fails without ever admitting actor work, while a winner that
            // later fails construction/startup rolls the claim back below.
            if (UsesDistributedLocation(actorType))
            {
                registeredByThisCall = await RegisterLocalRouteAsync(
                        actorType,
                        actorId,
                        strict ? nameof(CreateAsync) : nameof(EnsureAsync),
                        cancellationToken)
                    .ConfigureAwait(false);
                // Publish the local recovery watermark before any executable cell exists.
                registeredActivation = await CacheLocalRouteAsync(actorId, cancellationToken)
                    .ConfigureAwait(false);
            }

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

                    await _runtime.OpenLocalAdmissionAsync(actorType, actorId, cancellationToken)
                        .ConfigureAwait(false);

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

            if (localCreated)
            {
                _rollbackRecorder.RecordCreated(actorType, actorId);
            }
        }
        catch
        {
            _directoryCache?.Remove(actorId);
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
                    if (registeredActivation is { } failedActivation)
                        _activationRegistry?.Remove(actorId, failedActivation);
                    await _directory!.UnregisterAsync(actorId, _localNode.NodeId, CancellationToken.None)
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

    async ValueTask IActorPlacementService.DestroyAsync<TActor>(
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        try
        {
            await DestroyAsync<TActor>(actorId, cancellationToken).ConfigureAwait(false);
        }
        catch (ActorHostingException exception)
        {
            throw new ActorPlacementException(typeof(TActor), actorId, exception.Message, exception);
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
        var registerStatus = await _directory!
            .RegisterAsync(actorId, _localNode.NodeId, cancellationToken)
            .ConfigureAwait(false);

        if (registerStatus == ActorDirectoryRegisterStatus.Conflict)
        {
            var record = await _directory.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                _directoryCache?.Remove(actorId);
                throw new ActorDirectoryUnavailableException(
                    actorId,
                    actorType,
                    operation,
                    _localNode.NodeId,
                    "Actor directory returned a conflicting state without a resolvable owner.");
            }

            if (record.Node != _localNode.NodeId)
            {
                _directoryCache?.Remove(actorId);
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
        var registerStatus = await _directory!
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
            _directoryCache?.Remove(actorId);
            throw new ActorHostedElsewhereException(actorId, actorType, operation, _localNode.NodeId, record.Node);
        }

        return false;
    }

    private async ValueTask<ActorActivationId?> CacheLocalRouteAsync(
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        var record = await _directory!.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (record is not null && record.Node == _localNode.NodeId)
        {
            _directoryCache?.Set(record);
            _activationRegistry?.Set(record);
            return record.ActivationId;
        }

        _directoryCache?.Set(actorId, _localNode.NodeId);
        return null;
    }

    private static bool IsLocalOnly(Type actorType)
    {
        return actorType.GetCustomAttributes(typeof(ActorLocalOnlyAttribute), inherit: false).Length > 0;
    }

    private bool UsesDistributedLocation(Type actorType)
    {
        return _directory is not null && !IsLocalOnly(actorType);
    }

    private static bool IsExactActorType(Type existingActorType, Type requestedActorType)
    {
        return existingActorType == requestedActorType;
    }
}
