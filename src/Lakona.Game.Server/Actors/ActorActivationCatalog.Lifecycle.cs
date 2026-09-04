using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors.Internal;

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
internal sealed partial class ActorActivationCatalog : IActorPlacementService, IActorSelfDeactivationSink
{
    private readonly IActorDirectoryCache? _directoryCache;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ActorActivationRollbackRecorder _rollbackRecorder;
    private readonly ActorCompensationLifetime _compensationLifetime;
    private readonly IActorLifecycleDispatcher _lifecycleDispatcher;
    private readonly ActivationOperationLocks _operationLocks = new();
    private readonly ILogger<ActorActivationCatalog>? _logger;

    private IActorDirectory? Directory => _services.GetService(typeof(IActorDirectory)) as IActorDirectory;

    internal ActorActivationCatalog(
        IServiceProvider services,
        ActorRuntimeOptions options,
        LocalActorNodeIdentity localNode,
        ActorActivationRollbackRecorder rollbackRecorder,
        IActorDirectoryCache? directoryCache = null,
        IActorLifecycleDispatcher? lifecycleDispatcher = null,
        ILogger<ActorActivationCatalog>? logger = null,
        ActorCompensationLifetime? compensationLifetime = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.MailboxCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MailboxCapacity must be greater than zero.");
        if (options.CallTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "CallTimeout must be greater than zero.");
        if (options.DeactivationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "DeactivationTimeout must be greater than zero.");
        if (options.SlowMessageThreshold is { } slowMessageThreshold && slowMessageThreshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "SlowMessageThreshold must be greater than zero when set.");
        _diagnostics = new ActorRuntimeDiagnosticsPublisher(options);
        _directoryCache = directoryCache;
        _localNode = localNode ?? throw new ArgumentNullException(nameof(localNode));
        _rollbackRecorder = rollbackRecorder ?? throw new ArgumentNullException(nameof(rollbackRecorder));
        _compensationLifetime = compensationLifetime ?? new ActorCompensationLifetime();
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
        await using var gate = await _operationLocks.EnterAsync(actorId, cancellationToken).ConfigureAwait(false);
        await CreateCoreAsync(typeof(TActor), actorId, strict: true, target: null, cancellationToken).ConfigureAwait(false);
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
        => await EnsureAsync(typeof(TActor), actorId, cancellationToken).ConfigureAwait(false);

    internal async ValueTask EnsureAsync(
        Type actorType,
        ActorId actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        await using var gate = await _operationLocks.EnterAsync(actorId, cancellationToken).ConfigureAwait(false);

        if (TryGetLocalActor(actorId, out var existingActorType, out var state))
        {
            if (!IsExactActorType(existingActorType, actorType))
            {
                throw new ActorHostingTypeMismatchException(actorId, actorType, existingActorType, nameof(EnsureAsync));
            }

            if (state != ActorActivationState.Valid)
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

        await CreateCoreAsync(actorType, actorId, strict: false, target: null, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask ActivateExactAsync(
        Type actorType,
        ActorLifecycleTarget target,
        ActorPlacementCreateMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        if (target.Owner != _localNode.Reference)
            throw new ActorHostedElsewhereException(
                target.ActorId,
                actorType,
                nameof(ActivateExactAsync),
                _localNode.NodeId,
                target.Owner.Node);

        await using var gate = await _operationLocks.EnterAsync(target.ActorId, cancellationToken).ConfigureAwait(false);
        await CreateCoreAsync(
                actorType,
                target.ActorId,
                strict: mode == ActorPlacementCreateMode.Create,
                target,
                cancellationToken)
            .ConfigureAwait(false);
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
        => await DestroyCoreAsync(
                typeof(TActor),
                actorId,
                expectedOwner: null,
                expectedActivation: null,
                expectedLocalActor: null,
                cancellationToken)
            .ConfigureAwait(false);

    internal ValueTask DestroyAsync(
        Type actorType,
        ActorId actorId,
        CancellationToken cancellationToken = default) =>
        DestroyCoreAsync(
            actorType,
            actorId,
            expectedOwner: null,
            expectedActivation: null,
            expectedLocalActor: null,
            cancellationToken);

    internal async ValueTask DestroyExactAsync<TActor>(
        ActorId actorId,
        NodeReference expectedOwner,
        ActorActivationId expectedActivation,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
        => await DestroyCoreAsync(
                typeof(TActor),
                actorId,
                expectedOwner,
                expectedActivation,
                expectedLocalActor: null,
                cancellationToken)
            .ConfigureAwait(false);

    internal async ValueTask DestroyExactAsync(
        Type actorType,
        ActorLifecycleTarget target,
        CancellationToken cancellationToken = default) =>
        await DestroyCoreAsync(
                actorType,
                target.ActorId,
                target.Owner,
                target.ActivationId,
                expectedLocalActor: null,
                cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask DestroySelfAsync(
        Type actorType,
        ActorId actorId,
        object expectedLocalActor,
        CancellationToken cancellationToken = default) =>
        await DestroyCoreAsync(
                actorType,
                actorId,
                expectedOwner: null,
                expectedActivation: null,
                expectedLocalActor,
                cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask DestroyCoreAsync(
        Type actorType,
        ActorId actorId,
        NodeReference? expectedOwner,
        ActorActivationId? expectedActivation,
        object? expectedLocalActor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        await using var gate = await _operationLocks.EnterAsync(actorId, cancellationToken).ConfigureAwait(false);

        if (expectedLocalActor is not null && !IsExactLocalActor(actorId, expectedLocalActor))
        {
            return;
        }

        if (TryGetLocalActor(actorId, out var existingActorType, out _) &&
            !IsExactActorType(existingActorType, actorType))
        {
            throw new ActorHostingTypeMismatchException(actorId, actorType, existingActorType, nameof(DestroyAsync));
        }

        ActorDirectoryRecord? registeredRecord = null;
        if (UsesDistributedLocation(actorType))
            registeredRecord = await Directory!.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);

        if (expectedActivation is { } exactActivation
            && (registeredRecord is not { OwnerReference: { } registeredOwner }
                || registeredOwner != expectedOwner
                || registeredRecord.ActivationId != exactActivation
                || registeredOwner.Node != _localNode.NodeId))
        {
            return;
        }

        ActorHostingLocalRetireResult retireResult;
        try
        {
            retireResult = await RetireLocalAsync(
                actorType,
                actorId,
                _lifecycleDispatcher.HasStopHook(actorType)
                    ? (actor, ct) => _lifecycleDispatcher.StopAsync(actorType, actorId, actor, ct)
                    : static (_, _) => default,
                _options.DeactivationTimeout,
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

        if (UsesDistributedLocation(actorType))
        {
            _directoryCache?.Remove(actorId);
            // Once the authoritative claim is gone, crash recovery must not be
            // able to publish the retired activation again while local cell
            // cleanup is still in progress.
            if (registeredRecord?.Node == _localNode.NodeId)
                await ReleaseLocalRouteAsync(registeredRecord, cancellationToken).ConfigureAwait(false);
            _directoryCache?.Remove(actorId);
        }

        ActorHostingLocalDestroyResult destroyResult;
        try
        {
            destroyResult = await DestroyLocalAsync(actorType, actorId, _options.DeactivationTimeout, cancellationToken)
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
        await DestroySelfAsync(actorType, actorId, actor, cancellationToken).ConfigureAwait(false);
    }

    async ValueTask<ActorPlacementResult> IActorPlacementService.PlaceAsync<TActor, TKey>(
        TKey key,
        ActorPlacementCreateMode createMode,
        CancellationToken cancellationToken) =>
        await ((IActorPlacementService)this).PlaceAsync<TActor, TKey>(
            ActorIdentity.CreateOrUseExact<TActor, TKey>(key),
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
        ActorLifecycleTarget? target,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _drainState) != 0)
            throw new InvalidOperationException("The Actor activation catalog is draining.");

        if (TryGetLocalActor(actorId, out var existingActorType, out var state))
        {
            if (!IsExactActorType(existingActorType, actorType))
            {
                throw new ActorHostingTypeMismatchException(
                    actorId,
                    actorType,
                    existingActorType,
                    strict ? nameof(CreateAsync) : nameof(EnsureAsync));
            }

            if (state != ActorActivationState.Valid)
            {
                throw new ActorHostingStopException(
                    actorId,
                    actorType,
                    strict ? nameof(CreateAsync) : nameof(EnsureAsync),
                    $"Actor id '{actorId.Value}' is locally hosted but not active.");
            }

            if (target is { } exactTarget)
            {
                if (!strict
                    || (_actors.TryGetValue(actorId, out var exactCell)
                        && exactCell.ActivationId == exactTarget.ActivationId))
                {
                    return;
                }

                throw new ActorAlreadyHostedException(actorId, actorType, nameof(ActivateExactAsync));
            }

            if (strict)
            {
                throw new ActorAlreadyHostedException(actorId, actorType, nameof(CreateAsync));
            }

            return;
        }

        var registeredByThisCall = false;
        ActorDirectoryRecord? registeredRecord = null;
        var localCreated = false;
        try
        {
            var usesDirectory = UsesDistributedLocation(actorType);
            ActorDirectoryRecord? proposedRecord = null;
            if (usesDirectory)
            {
                var owner = target?.Owner ?? _localNode.Reference
                    ?? throw new ActorDirectoryUnavailableException(
                        actorId,
                        actorType,
                        strict ? nameof(CreateAsync) : nameof(EnsureAsync),
                        _localNode.NodeId,
                        "The local process has no exact Membership identity.");
                proposedRecord = new ActorDirectoryRecord(
                    actorId,
                    owner,
                    target?.ActivationId ?? ActorActivationId.New(),
                    DateTimeOffset.UtcNow);
            }

            // Reserve the Catalog entry first. Directory recovery can now see
            // the Creating activation throughout ownership acquisition, while
            // business admission remains closed.
            var createResult = await ReserveLocalAsync(
                    actorType,
                    actorId,
                    proposedRecord,
                    cancellationToken)
                .ConfigureAwait(false);

            switch (createResult.Status)
            {
                case ActorHostingLocalCreateStatus.Created:
                    localCreated = true;
                    if (usesDirectory)
                    {
                        registeredRecord = await RegisterLocalRouteAsync(
                                actorType,
                                proposedRecord!,
                                target is not null,
                                strict,
                                strict ? nameof(CreateAsync) : nameof(EnsureAsync),
                                cancellationToken)
                            .ConfigureAwait(false);
                        registeredByThisCall = true;
                        SetLocalDirectoryRecord(actorId, registeredRecord);
                        var cached = await CacheLocalRouteAsync(actorId, cancellationToken)
                            .ConfigureAwait(false);
                        SetLocalDirectoryRecord(actorId, cached);
                        await RevalidateLocalRouteAsync(actorId, cached.ActivationId, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await ActivateLocalAsync(actorType, actorId, cancellationToken)
                        .ConfigureAwait(false);
                    if (_lifecycleDispatcher.HasStartHook(actorType))
                    {
                        await InvokeLocalAsync(
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

                    await OpenLocalAdmissionAsync(actorType, actorId, cancellationToken)
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
        catch (Exception failure)
        {
            _directoryCache?.Remove(actorId);
            var cleanupActorType = actorType;
            var shouldCleanupLocal = _actors.TryGetValue(actorId, out var failedCell)
                && failedCell.ActorType == actorType;
            failedCell?.BeginStopping();

            if (registeredByThisCall)
            {
                try
                {
                    await _compensationLifetime.ExecuteAsync(
                        actorId,
                        "failed-create directory release",
                        async cleanupToken =>
                        {
                            var record = await Directory!.ResolveAsync(actorId, cleanupToken)
                                .ConfigureAwait(false);
                            if (registeredRecord?.ActivationId is { } failedActivation)
                            {
                                if (record?.ActivationId == failedActivation)
                                    await ReleaseLocalRouteAsync(record, cleanupToken).ConfigureAwait(false);
                            }
                            else if (record?.Node == _localNode.NodeId)
                            {
                                await ReleaseLocalRouteAsync(record, cleanupToken).ConfigureAwait(false);
                            }
                        }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to roll back actor directory route for {ActorId}.", actorId.Value);
                    throw new ActorHostingException(
                        actorId,
                        actorType,
                        strict ? nameof(CreateAsync) : nameof(EnsureAsync),
                        $"Actor creation failed and directory compensation for actor id '{actorId.Value}' is unconfirmed.",
                        new AggregateException(failure, ex));
                }
            }

            if (shouldCleanupLocal)
            {
                try
                {
                    await DestroyLocalAsync(cleanupActorType, actorId, _options.DeactivationTimeout, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to roll back local actor {ActorId}.", actorId.Value);
                }
            }

            throw;
        }
    }

    ValueTask IActorActivationLifecycle.DrainAsync(CancellationToken cancellationToken)
    {
        lock (_disposeGate)
        {
            if (_drainTask is null)
            {
                Volatile.Write(ref _drainState, 1);
                _drainTask = DrainCoreAsync(cancellationToken);
            }

            return new ValueTask(_drainTask);
        }
    }

    private async Task DrainCoreAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var activations = _actors
            .Select(static pair => (ActorId: pair.Key, ActorType: pair.Value.ActorType))
            .OrderBy(static activation => activation.ActorId.Value, StringComparer.Ordinal)
            .ToArray();

        foreach (var activation in activations)
        {
            try
            {
                await DestroyCoreAsync(
                        activation.ActorType,
                        activation.ActorId,
                        expectedOwner: null,
                        expectedActivation: null,
                        expectedLocalActor: null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("One or more Actor activations failed to drain.", failures);
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
        return TryGetLocalActor(actorId, out var existingActorType, out var state) &&
            IsExactActorType(existingActorType, actorType) &&
            state != ActorActivationState.Invalid;
    }

    private async ValueTask EnsureLocalRouteAsync(
        Type actorType,
        ActorId actorId,
        string operation,
        CancellationToken cancellationToken)
    {
        var record = _actors.TryGetValue(actorId, out var cell)
            ? cell.DirectoryRecord
            : await Directory!.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (record is not { OwnerReference: { } owner, ActivationId: { } activation }
            || owner.Node != _localNode.NodeId)
            throw new ActorDirectoryUnavailableException(
                actorId,
                actorType,
                operation,
                _localNode.NodeId,
                "An active local Actor has no exact recovery claim.");

        var acquired = await Directory!.AcquireAsync(actorId, owner, activation, cancellationToken)
            .ConfigureAwait(false);
        if (acquired.Record.OwnerReference != owner || acquired.Record.ActivationId != activation)
            throw new ActorHostedElsewhereException(
                actorId,
                actorType,
                operation,
                _localNode.NodeId,
                acquired.Record.Node);
    }

    private async ValueTask<ActorDirectoryRecord> RegisterLocalRouteAsync(
        Type actorType,
        ActorDirectoryRecord proposal,
        bool exactProposal,
        bool strict,
        string operation,
        CancellationToken cancellationToken)
    {
        var acquired = await Directory!.AcquireAsync(
                proposal.ActorId,
                proposal.OwnerReference,
                proposal.ActivationId,
                cancellationToken)
            .ConfigureAwait(false);
        var record = acquired.Record;

        if (record.OwnerReference != _localNode.Reference)
        {
            _directoryCache?.Remove(proposal.ActorId);
            throw new ActorHostedElsewhereException(
                proposal.ActorId,
                actorType,
                operation,
                _localNode.NodeId,
                record.Node);
        }

        if (exactProposal
            && (record.OwnerReference != proposal.OwnerReference
                || record.ActivationId != proposal.ActivationId))
        {
            throw new ActorAlreadyHostedException(proposal.ActorId, actorType, operation);
        }

        if (!exactProposal && strict && !acquired.Acquired)
            throw new ActorAlreadyHostedException(proposal.ActorId, actorType, operation);

        return record;
    }

    private void SetLocalDirectoryRecord(ActorId actorId, ActorDirectoryRecord record)
    {
        if (!_actors.TryGetValue(actorId, out var cell))
            throw new ActorDirectoryUnavailableException(
                $"Actor activation '{actorId.Value}' left the Catalog before its Directory claim was attached.");
        cell.SetDirectoryRecord(record);
    }

    private async ValueTask<ActorDirectoryRecord> CacheLocalRouteAsync(
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        var record = await Directory!.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (record is not null && record.Node == _localNode.NodeId)
        {
            _directoryCache?.Set(record);
            return record;
        }

        throw new ActorDirectoryUnavailableException(
            $"Actor directory lost the exact activation claim for '{actorId.Value}' before local admission opened.");
    }

    private async ValueTask RevalidateLocalRouteAsync(
        ActorId actorId,
        ActorActivationId? expectedActivation,
        CancellationToken cancellationToken)
    {
        var record = await Directory!.ResolveAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (expectedActivation is { } activation
            && record is { OwnerReference: { } owner }
            && owner.Node == _localNode.NodeId
            && record.ActivationId == activation)
            return;

        throw new ActorDirectoryUnavailableException(
            $"Actor directory lost the exact activation claim for '{actorId.Value}' after recovery evidence was published.");
    }

    private async ValueTask ReleaseLocalRouteAsync(
        ActorDirectoryRecord retiringRecord,
        CancellationToken cancellationToken)
    {
        var activation = retiringRecord.ActivationId;
        var released = await Directory!
            .ReleaseAsync(retiringRecord.ActorId, activation, cancellationToken)
            .ConfigureAwait(false);

        if (!released)
        {
            await RemoveStaleRecoveryEvidenceAsync(retiringRecord, cancellationToken).ConfigureAwait(false);
            return;
        }

        ClearRecoveryClaim(retiringRecord);

    }

    private async ValueTask RemoveStaleRecoveryEvidenceAsync(
        ActorDirectoryRecord retiringRecord,
        CancellationToken cancellationToken)
    {
        var current = await Directory!.ResolveAsync(retiringRecord.ActorId, cancellationToken).ConfigureAwait(false);
        var sameClaim = current is not null
            && current.OwnerReference == retiringRecord.OwnerReference
            && current.ActivationId == retiringRecord.ActivationId;
        if (sameClaim)
            throw new ActorDirectoryUnavailableException(
                $"Actor directory did not release the local claim for '{retiringRecord.ActorId.Value}'.");

        ClearRecoveryClaim(retiringRecord);

    }

    private static bool IsLocalOnly(Type actorType)
    {
        return actorType.GetCustomAttributes(typeof(ActorLocalOnlyAttribute), inherit: false).Length > 0;
    }

    private void ClearRecoveryClaim(ActorDirectoryRecord record)
    {
        if (_actors.TryGetValue(record.ActorId, out var cell))
            cell.ClearDirectoryRecord(record.ActivationId);
    }

    private bool UsesDistributedLocation(Type actorType)
    {
        return Directory is not null && !IsLocalOnly(actorType);
    }

    private sealed class ActivationOperationLocks
    {
        private readonly ConcurrentDictionary<ActorId, Entry> entries = new();

        public async ValueTask<IAsyncDisposable> EnterAsync(
            ActorId actorId,
            CancellationToken cancellationToken)
        {
            Entry entry;
            do
            {
                entry = entries.GetOrAdd(actorId, static _ => new Entry());
            }
            while (!entry.TryAddReference());

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new Releaser(this, actorId, entry);
            }
            catch
            {
                ReleaseReference(actorId, entry);
                throw;
            }
        }

        private void Release(ActorId actorId, Entry entry)
        {
            entry.Semaphore.Release();
            ReleaseReference(actorId, entry);
        }

        private void ReleaseReference(ActorId actorId, Entry entry)
        {
            if (entry.ReleaseReferenceAndRetireIfUnused())
                entries.TryRemove(new KeyValuePair<ActorId, Entry>(actorId, entry));
        }

        private sealed class Entry
        {
            private const int Retired = -1;
            private int referenceCount;

            public SemaphoreSlim Semaphore { get; } = new(1, 1);

            public bool TryAddReference()
            {
                while (true)
                {
                    var current = Volatile.Read(ref referenceCount);
                    if (current == Retired) return false;
                    if (Interlocked.CompareExchange(ref referenceCount, current + 1, current) == current)
                        return true;
                }
            }

            public bool ReleaseReferenceAndRetireIfUnused() =>
                Interlocked.Decrement(ref referenceCount) == 0
                && Interlocked.CompareExchange(ref referenceCount, Retired, 0) == 0;
        }

        private sealed class Releaser(
            ActivationOperationLocks owner,
            ActorId actorId,
            Entry entry) : IAsyncDisposable
        {
            private int disposed;

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                    owner.Release(actorId, entry);
                return default;
            }
        }
    }

}

internal readonly record struct ActorHostingLocalRetireResult(
    ActorHostingLocalRetireStatus Status,
    ActorId ActorId,
    Type RequestedActorType,
    Type? ExistingActorType = null);

internal enum ActorHostingLocalRetireStatus
{
    Retired,
    NotFound,
    TypeMismatch,
    TimedOut
}

internal readonly record struct ActorHostingLocalCreateResult(
    ActorHostingLocalCreateStatus Status,
    ActorId ActorId,
    Type RequestedActorType,
    Type? ExistingActorType = null);

internal enum ActorHostingLocalCreateStatus
{
    Created,
    AlreadyExistsSameType,
    AlreadyExistsDifferentType
}

internal readonly record struct ActorHostingLocalDestroyResult(
    ActorHostingLocalDestroyStatus Status,
    ActorId ActorId,
    Type RequestedActorType,
    Type? ExistingActorType = null);

internal enum ActorHostingLocalDestroyStatus
{
    Destroyed,
    NotFound,
    TypeMismatch,
    TimedOut
}
