using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.BuildTag;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixManager
    : IHotfixManager,
      IHotfixServiceProviderAccessor,
      IHotfixRuntimeAccessor,
      IDisposable,
      IAsyncDisposable
{
    private const string LoggerCategory = "Lakona.Game.Hotfix";

    private readonly IHotfixAssemblySource _source;
    private readonly IReadOnlyList<string> _hostAssemblyNames;
    private readonly IReadOnlyList<Type> _requiredServiceContracts;
    private readonly IServiceProvider? _rootServices;
    private readonly ILogger? _logger;
    private readonly IReadOnlyList<IHotfixRuntimePublicationParticipant> _publicationParticipants;
    private readonly Func<HotfixDispatchTable> _dispatchTableProvider;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly List<Task> _retirementTasks = [];
    private int _disposeState;
    private long _nextVersion;
    private HotfixPublicationState _publication = HotfixPublicationState.Empty;

    public HotfixManager(
        IHotfixAssemblySource source,
        IEnumerable<string>? hostAssemblyNames = null,
        IEnumerable<Type>? requiredServiceContracts = null,
        IServiceProvider? rootServices = null,
        IEnumerable<IHotfixRuntimePublicationParticipant>? participants = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _rootServices = rootServices;
        _logger = rootServices?.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory);
        _hostAssemblyNames = (hostAssemblyNames ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _requiredServiceContracts = (requiredServiceContracts ?? Array.Empty<Type>())
            .Distinct()
            .ToArray();
        _publicationParticipants = (participants
                ?? rootServices?.GetServices<IHotfixRuntimePublicationParticipant>()
                ?? Array.Empty<IHotfixRuntimePublicationParticipant>())
            .ToArray();
        _dispatchTableProvider = () => Volatile.Read(ref _publication).DispatchTable;
    }

    public event EventHandler<HotfixReloadResult>? Reloaded;

    public HotfixSnapshot Current => Volatile.Read(ref _publication).Snapshot;

    IServiceProvider IHotfixServiceProviderAccessor.Current =>
        HotfixDispatchRuntimeScope.CurrentServices ?? Volatile.Read(ref _publication).Runtime.Services;

    HotfixRuntimeSnapshot IHotfixRuntimeAccessor.Current => ResolveCurrentRuntime();

    HotfixRuntimeSnapshotLease IHotfixRuntimeAccessor.AcquireCurrent()
    {
        while (true)
        {
            var snapshot = ResolveCurrentRuntime();
            try
            {
                return snapshot.AcquireLease();
            }
            catch (ObjectDisposedException) when (!ReferenceEquals(snapshot, ResolveCurrentRuntime()))
            {
            }
        }
    }

    private HotfixRuntimeSnapshot ResolveCurrentRuntime()
    {
        var context = HotfixDispatchRuntimeScope.Current;
        return context is not null && context.TryGetSnapshot(out var scoped)
            ? scoped
            : Volatile.Read(ref _publication).Runtime;
    }

    public async ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        return await ValidateAsync(_source, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<HotfixReloadResult> ValidateAsync(
        IHotfixAssemblySource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

        await _reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            return await LoadCoreAsync(source, publish: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public async ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        await _reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            var result = await LoadCoreAsync(_source, publish: true, cancellationToken).ConfigureAwait(false);
            LogReloadResult(result);
            return result;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private async ValueTask<HotfixReloadResult> LoadCoreAsync(
        IHotfixAssemblySource source,
        bool publish,
        CancellationToken cancellationToken)
    {
        HotfixAssemblySourceResult? resolved = null;
        HotfixAssemblyLoadContext? pendingContext = null;
        IServiceProvider? hotfixProvider = null;
        HotfixDispatchTable? pendingTable = null;
        var cleanupFailures = new List<Exception>();
        var dependencyWarnings = new List<string>();
        async ValueTask CleanupCandidateAsync()
        {
            var tableToDispose = pendingTable;
            var providerToDispose = hotfixProvider;
            var contextToUnload = pendingContext;
            pendingTable = null;
            hotfixProvider = null;
            pendingContext = null;
            var failures = await HotfixResourceCleanup.RunAsync(tableToDispose, providerToDispose, contextToUnload).ConfigureAwait(false);
            cleanupFailures.AddRange(failures);
            LogResourceCleanupResult(failures, "candidate", resolved?.Version, resolved?.AssemblyPath);
        }
        try
        {
            resolved = await source.ResolveAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(resolved.AssemblyPath))
            {
                throw new FileNotFoundException("Hotfix assembly was not found.", resolved.AssemblyPath);
            }

            pendingContext = new HotfixAssemblyLoadContext(resolved.AssemblyPath, _hostAssemblyNames);
            var assembly = pendingContext.LoadMainAssemblyFromBytes(resolved.AssemblyPath);
            var scan = HotfixBehaviorScanner.Scan(
                assembly,
                requiredServiceContracts: _requiredServiceContracts);
            if (!scan.Succeeded)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, scan.Diagnostics));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var boundaryDiagnostics = HotfixDispatchBoundaryValidator.Validate(pendingContext, scan.Methods, scan.Services, scan.TimerMethods, scan.Lifecycles);
            if (boundaryDiagnostics.Count != 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, boundaryDiagnostics));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var actorHosts = CreateActorHostDescriptors(scan, resolved.Version);
            var roleCatalog = _rootServices?.GetService<NodeRoleCatalog>();
            var localActorMethods = roleCatalog is null
                ? scan.ActorMethods
                : scan.ActorMethods.Where(method => roleCatalog.IsLocal(method.ActorType)).ToArray();
            var localActorLifecycles = roleCatalog is null
                ? scan.ActorLifecycles
                : scan.ActorLifecycles.Where(lifecycle => roleCatalog.IsLocal(lifecycle.ActorType)).ToArray();
            var localBehaviorTypes = localActorMethods
                .Select(static method => method.BehaviorType)
                .Concat(localActorLifecycles.Select(static lifecycle => lifecycle.BehaviorType))
                .ToHashSet();
            var localMethods = roleCatalog is null
                ? scan.Methods
                : scan.Methods.Where(method => localBehaviorTypes.Contains(method.BehaviorType)).ToArray();
            var localHttpEndpoints = SelectLocalHttpEndpoints(scan.HttpEndpoints);
            var tableVersion = publish ? Interlocked.Increment(ref _nextVersion) : Current.DispatchTableVersion;
            var table = new HotfixDispatchTable(
                tableVersion,
                localMethods,
                scan.Services,
                localActorMethods,
                localActorLifecycles,
                scan.TimerMethods,
                localHttpEndpoints,
                scan.Lifecycles);
            pendingTable = table;
            table.ValidateMethodShapes();
            hotfixProvider = BuildHotfixProvider(scan.StartupServices, assembly, table.ModuleTypes, dependencyWarnings);
            table.ValidateModuleActivation(hotfixProvider);
            table.ValidateTypedDispatchDelegates();
            var snapshot = new HotfixSnapshot(
                resolved.Version,
                resolved.AssemblyPath,
                DateTimeOffset.UtcNow,
                tableVersion,
                table.MethodKeys,
                HotfixReloadStatus.Succeeded,
                null,
                null,
                actorHosts);

            if (!publish)
            {
                var candidateRuntime = new HotfixRuntimeSnapshot(
                    new HotfixServiceInvoker(table),
                    hotfixProvider,
                    table,
                    hotfixProvider,
                    assembly,
                    pendingContext,
                    resolved.Version,
                    resolved.AssemblyPath,
                    ownsRuntimeResources: false,
                    onRetired: null,
                    actorStartups: scan.ActorStartups,
                    actorPlacements: scan.ActorPlacements);
                await ValidatePublicationCandidateAsync(
                    candidateRuntime,
                    cancellationToken).ConfigureAwait(false);
                await CleanupCandidateAsync().ConfigureAwait(false);
                if (cleanupFailures.Count != 0)
                    throw new InvalidOperationException("Hotfix candidate validation cleanup failed; inspect cleanup diagnostics and fix Dispose/DisposeAsync before retrying.");
                var status = dependencyWarnings.Count == 0 ? HotfixReloadStatus.Succeeded : HotfixReloadStatus.SucceededWithWarnings;
                return new HotfixReloadResult(status, WithReloadStatus(snapshot, status), resolved.Version, resolved.AssemblyPath, dependencyWarnings);
            }

            var runtimeSnapshot = new HotfixRuntimeSnapshot(
                new HotfixServiceInvoker(table),
                hotfixProvider,
                table,
                hotfixProvider,
                assembly,
                pendingContext,
                resolved.Version,
                resolved.AssemblyPath,
                ownsRuntimeResources: true,
                onRetired: null,
                actorStartups: scan.ActorStartups,
                actorPlacements: scan.ActorPlacements);
            pendingTable = null;
            // Ownership transfers to the runtime before publication, including its failure paths.
            hotfixProvider = null;
            pendingContext = null;
            var result = await PublishCandidateAsync(
                runtimeSnapshot,
                snapshot,
                cancellationToken,
                resolved.Version,
                resolved.AssemblyPath,
                dependencyWarnings).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return result;
            }

            Reloaded?.Invoke(this, result);
            return result;
        }
        catch (OperationCanceledException cancellationException)
        {
            await CleanupCandidateAsync().ConfigureAwait(false);
            if (cleanupFailures.Count != 0)
                throw new AggregateException("Hotfix candidate cancellation and cleanup failed.", [cancellationException, .. cleanupFailures]);
            throw;
        }
        catch (Exception ex)
        {
            await CleanupCandidateAsync().ConfigureAwait(false);

            var previous = Current;
            var snapshot = new HotfixSnapshot(
                previous.Version,
                previous.SourcePath,
                previous.LoadedAtUtc,
                previous.DispatchTableVersion,
                previous.Methods,
                HotfixReloadStatus.Failed,
                ex.Message,
                ex.GetType().FullName,
                previous.ActorHosts);
            if (publish)
            {
                var publication = Volatile.Read(ref _publication);
                Volatile.Write(
                    ref _publication,
                    new HotfixPublicationState(
                        snapshot,
                        publication.Runtime,
                        publication.DispatchTable));
            }

            return new HotfixReloadResult(
                HotfixReloadStatus.Failed,
                snapshot,
                resolved?.Version,
                resolved?.AssemblyPath,
                [ex.Message, .. dependencyWarnings, .. cleanupFailures.Select(static failure => failure.Message)],
                ex.Message,
                ex.GetType().FullName);
        }
    }

    private IReadOnlyList<HotfixHttpEndpointMethodBinding> SelectLocalHttpEndpoints(
        IReadOnlyList<HotfixHttpEndpointMethodBinding> endpoints)
    {
        var runtime = _rootServices?.GetService<LakonaGameRuntimeOptions>();
        if (runtime is null)
        {
            return endpoints;
        }

        var enabledServices = runtime.Http.Listeners
            .SelectMany(static listener => listener.Services)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return endpoints
            .Where(endpoint => enabledServices.Contains(endpoint.Endpoint.Service))
            .ToArray();
    }

    private async ValueTask ValidatePublicationCandidateAsync(
        HotfixRuntimeSnapshot candidate,
        CancellationToken cancellationToken)
    {
        _rootServices?.GetService<Sessions.GameSessionLifecycleBindings>()?.Validate(candidate.DispatchTable?.SessionLifecycleIdentities ?? []);
        var previous = Volatile.Read(ref _publication).Runtime;
        foreach (var participant in _publicationParticipants)
        {
            await participant.ValidateAsync(
                previous,
                candidate,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<HotfixActorHostDescriptor> CreateActorHostDescriptors(
        HotfixBehaviorScanResult scan,
        string? hotfixVersion)
    {
        var descriptors = new Dictionary<string, HotfixActorHostDescriptor>(StringComparer.OrdinalIgnoreCase);
        var actorTypes = scan.ActorMethods
            .Select(static method => method.ActorType)
            .Concat(scan.ActorLifecycles.Select(static lifecycle => lifecycle.ActorType))
            .Concat(scan.ActorStartups.Select(static startup => startup.ActorType))
            .Concat(scan.ActorPlacements.Select(static placement => placement.ActorType))
            .Distinct();
        foreach (var actorType in actorTypes)
        {
            AddActorHostDescriptor(
                descriptors,
                ActorNameConventions.Resolve(actorType),
                "placement:" + actorType.FullName,
                hotfixVersion);
        }

        foreach (var startup in scan.ActorStartups)
        {
            var actorType = startup.ActorType;
            var keyType = startup.KeyType;
            AddActorHostDescriptor(
                descriptors,
                ActorNameConventions.Resolve(actorType),
                $"startup:v1:{actorType.FullName}:{keyType.FullName}",
                hotfixVersion);
        }

        foreach (var placement in scan.ActorPlacements)
        {
            var name = ActorNameConventions.Resolve(placement.ActorType);
            AddActorHostDescriptor(
                descriptors,
                name,
                "placement:" + placement.ActorType.FullName,
                hotfixVersion);
        }

        return descriptors.Values
            .OrderBy(static descriptor => descriptor.Actor, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddActorHostDescriptor(
        IDictionary<string, HotfixActorHostDescriptor> descriptors,
        string actor,
        string policyHash,
        string? hotfixVersion)
    {
        descriptors[actor] = new HotfixActorHostDescriptor(
            actor,
            policyHash,
            string.IsNullOrWhiteSpace(hotfixVersion) ? "hotfix" : hotfixVersion);
    }

    internal async ValueTask<HotfixReloadResult> PublishCandidateAsync(
        HotfixRuntimeSnapshot runtimeSnapshot,
        HotfixSnapshot snapshot,
        CancellationToken cancellationToken,
        string? requestedVersion = null,
        string? requestedPath = null,
        IReadOnlyList<string>? dependencyWarnings = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeSnapshot);
        ArgumentNullException.ThrowIfNull(snapshot);
        var previousPublication = Volatile.Read(ref _publication);
        var transactions = new List<IHotfixRuntimePublicationTransaction>(_publicationParticipants.Count);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var participant in _publicationParticipants)
            {
                transactions.Add(await participant.PrepareAsync(
                    previousPublication.Runtime,
                    runtimeSnapshot,
                    cancellationToken).ConfigureAwait(false));
            }

            using (runtimeSnapshot.AcquireLease())
            {
                foreach (var transaction in transactions)
                {
                    await transaction.ActivateAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var nextPublication = new HotfixPublicationState(
                snapshot,
                runtimeSnapshot,
                runtimeSnapshot.DispatchTable ?? previousPublication.DispatchTable);
            void Publish()
            {
                HotfixDispatch.ReplaceProvider(_dispatchTableProvider);
                Volatile.Write(ref _publication, nextPublication);
            }
            if (_rootServices?.GetService<Sessions.GameSessionLifecycleBindings>() is { } sessionBindings)
                sessionBindings.Publish(runtimeSnapshot.DispatchTable?.SessionLifecycleIdentities ?? [], Publish);
            else
                Publish();
        }
        catch (OperationCanceledException cancellationException)
        {
            IReadOnlyList<Exception> rollbackFailures;
            using (runtimeSnapshot.AcquireLease())
            {
                rollbackFailures = await RollbackPublicationTransactionsAsync(transactions).ConfigureAwait(false);
            }
            var disposalFailures = await DisposePublicationTransactionsAsync(
                transactions,
                "cancellation").ConfigureAwait(false);
            var resourceFailures = await RetireCandidateAsync(runtimeSnapshot).ConfigureAwait(false);
            var cleanupExceptions = rollbackFailures
                .Concat(disposalFailures.Select(static failure => failure.Exception))
                .Concat(resourceFailures)
                .ToArray();
            if (cleanupExceptions.Length != 0)
            {
                throw new AggregateException(
                    "Hotfix publication cancellation cleanup failed.",
                    [cancellationException, .. cleanupExceptions]);
            }
            throw;
        }
        catch (Exception ex)
        {
            IReadOnlyList<Exception> rollbackFailures;
            using (runtimeSnapshot.AcquireLease())
            {
                rollbackFailures = await RollbackPublicationTransactionsAsync(transactions).ConfigureAwait(false);
            }
            var disposalFailures = await DisposePublicationTransactionsAsync(
                transactions,
                "rollback").ConfigureAwait(false);
            var resourceFailures = await RetireCandidateAsync(runtimeSnapshot).ConfigureAwait(false);
            var cleanupExceptions = rollbackFailures
                .Concat(disposalFailures.Select(static failure => failure.Exception))
                .Concat(resourceFailures)
                .ToArray();
            var failure = cleanupExceptions.Length == 0
                ? ex
                : new AggregateException("Hotfix publication and cleanup failed.", [ex, .. cleanupExceptions]);
            return new HotfixReloadResult(
                HotfixReloadStatus.Failed,
                previousPublication.Snapshot,
                requestedVersion ?? snapshot.Version,
                requestedPath ?? snapshot.SourcePath,
                [
                    ex.Message,
                    .. rollbackFailures.Select(static item => item.Message),
                    .. disposalFailures.Select(static item => item.Diagnostic),
                    .. resourceFailures.Select(static item => item.Message)
                ],
                failure.Message,
                failure.GetType().FullName);
        }

        var cleanupFailures = new List<PublicationCleanupFailure>();
        cleanupFailures.AddRange(await CommitPublicationTransactionsAsync(transactions).ConfigureAwait(false));
        cleanupFailures.AddRange(await DisposePublicationTransactionsAsync(
            transactions,
            "published").ConfigureAwait(false));

        _retirementTasks.RemoveAll(static task => task.IsCompletedSuccessfully);
        if (!ReferenceEquals(previousPublication.Runtime, HotfixPublicationState.Empty.Runtime))
        {
            previousPublication.Runtime.Retire();
            if (previousPublication.Runtime.RetirementCompletion.IsCompletedSuccessfully)
            {
                var failures = await ObserveAndLogRetirementAsync(
                        previousPublication.Runtime,
                        "retired",
                        previousPublication.Snapshot.Version,
                        previousPublication.Snapshot.SourcePath)
                    .ConfigureAwait(false);
                cleanupFailures.AddRange(failures.Select(static failure =>
                    new PublicationCleanupFailure("published", "retirement", "previous runtime", failure)));
            }
            else
            {
                // Existing calls may still hold leases. Do not delay publication for their retirement.
                _retirementTasks.Add(ObserveAndLogRetirementAsync(
                    previousPublication.Runtime,
                    "retired",
                    previousPublication.Snapshot.Version,
                    previousPublication.Snapshot.SourcePath));
            }
        }

        var status = cleanupFailures.Count == 0 && (dependencyWarnings?.Count ?? 0) == 0
            ? HotfixReloadStatus.Succeeded
            : HotfixReloadStatus.SucceededWithWarnings;
        var currentSnapshot = status == HotfixReloadStatus.Succeeded
            ? snapshot
            : WithReloadStatus(snapshot, status);
        if (status == HotfixReloadStatus.SucceededWithWarnings)
        {
            var publication = Volatile.Read(ref _publication);
            Volatile.Write(
                ref _publication,
                new HotfixPublicationState(
                    currentSnapshot,
                    publication.Runtime,
                    publication.DispatchTable));
        }

        return new HotfixReloadResult(
            status,
            currentSnapshot,
            requestedVersion ?? snapshot.Version,
            requestedPath ?? snapshot.SourcePath,
            [.. (dependencyWarnings ?? []), .. cleanupFailures.Select(static failure => failure.Diagnostic)]);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        await _reloadLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var publication = Interlocked.Exchange(ref _publication, HotfixPublicationState.Empty);
            HotfixDispatch.RemoveProvider(_dispatchTableProvider);
            Reloaded = null;
            try
            {
                if (!ReferenceEquals(publication.Runtime, HotfixPublicationState.Empty.Runtime))
                {
                    publication.Runtime.Retire();
                    var failures = await ObserveAndLogRetirementAsync(
                            publication.Runtime,
                            "shutdown",
                            publication.Snapshot.Version,
                            publication.Snapshot.SourcePath)
                        .ConfigureAwait(false);
                    if (failures.Count != 0)
                    {
                        throw new AggregateException(
                            "Hotfix runtime retirement cleanup failed.",
                            failures);
                    }
                }
            }
            finally
            {
                await Task.WhenAll(_retirementTasks).ConfigureAwait(false);
                _retirementTasks.Clear();
            }
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask<IReadOnlyList<Exception>> RollbackPublicationTransactionsAsync(
        IReadOnlyList<IHotfixRuntimePublicationTransaction> transactions)
    {
        var failures = new List<Exception>();
        for (var index = transactions.Count - 1; index >= 0; index--)
        {
            try
            {
                await transactions[index].RollbackAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                _logger?.LogError(exception, "Hotfix publication rollback failed.");
            }
        }
        return failures;
    }

    private async ValueTask<IReadOnlyList<PublicationCleanupFailure>> DisposePublicationTransactionsAsync(
        IReadOnlyList<IHotfixRuntimePublicationTransaction> transactions,
        string phase)
    {
        var failures = new List<PublicationCleanupFailure>();
        for (var index = transactions.Count - 1; index >= 0; index--)
        {
            var transaction = transactions[index];
            try
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var failure = new PublicationCleanupFailure(
                    phase,
                    "disposal",
                    transaction.GetType().FullName ?? transaction.GetType().Name,
                    exception);
                failures.Add(failure);
                _logger?.LogError(
                    exception,
                    "Hotfix publication transaction {TransactionType} disposal failed during {PublicationPhase} cleanup.",
                    failure.TransactionType,
                    phase);
            }
        }

        return failures;
    }

    private async ValueTask<IReadOnlyList<PublicationCleanupFailure>> CommitPublicationTransactionsAsync(
        IReadOnlyList<IHotfixRuntimePublicationTransaction> transactions)
    {
        var failures = new List<PublicationCleanupFailure>();
        foreach (var transaction in transactions)
        {
            try
            {
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var failure = new PublicationCleanupFailure(
                    "published",
                    "commit",
                    transaction.GetType().FullName ?? transaction.GetType().Name,
                    exception);
                failures.Add(failure);
                _logger?.LogError(
                    exception,
                    "Hotfix publication transaction {TransactionType} commit failed during published cleanup.",
                    failure.TransactionType);
            }
        }

        return failures;
    }

    private static HotfixSnapshot WithReloadStatus(
        HotfixSnapshot snapshot,
        HotfixReloadStatus status) =>
        new(
            snapshot.Version,
            snapshot.SourcePath,
            snapshot.LoadedAtUtc,
            snapshot.DispatchTableVersion,
            snapshot.Methods,
            status,
            snapshot.LastFailureMessage,
            snapshot.LastFailureExceptionType,
            snapshot.ActorHosts);

    private sealed record PublicationCleanupFailure(
        string Phase,
        string Operation,
        string TransactionType,
        Exception Exception)
    {
        public string Diagnostic =>
            $"Hotfix publication transaction {TransactionType} {Operation} failed during {Phase} cleanup: {Exception.Message}";
    }

    private void LogReloadResult(HotfixReloadResult result)
    {
        if (_logger is null)
        {
            return;
        }

        if (result.Status == HotfixReloadStatus.SucceededWithWarnings)
        {
            _logger.LogWarning(
                "Hotfix reload succeeded with {WarningCount} warning(s) from {HotfixPath}. LakonaBuildTag={LakonaBuildTag}. Version={Version}. Diagnostics={Diagnostics}",
                result.Diagnostics.Count,
                result.Current.SourcePath,
                GetBuildTag(),
                result.Current.Version,
                string.Join(Environment.NewLine, result.Diagnostics));
            return;
        }

        if (result.Succeeded)
        {
            _logger.LogInformation(
                "Hotfix reload succeeded from {HotfixPath} with {MethodCount} method(s). LakonaBuildTag={LakonaBuildTag}. Version={Version}.",
                result.Current.SourcePath,
                result.Current.Methods.Count,
                GetBuildTag(),
                result.Current.Version);
            return;
        }

        _logger.LogError(
            "Hotfix reload failed for {HotfixPath}: {ErrorMessage}",
            result.RequestedPath ?? result.Current.SourcePath ?? "(unresolved)",
            result.ErrorMessage ?? string.Join(Environment.NewLine, result.Diagnostics));
    }

    private static string GetBuildTag()
    {
        return HotfixBuildTag.Get(Assembly.GetEntryAssembly() ?? typeof(HotfixManager).Assembly);
    }

    private IServiceProvider BuildHotfixProvider()
    {
        return BuildHotfixProvider(Array.Empty<ServiceDescriptor>(), typeof(HotfixManager).Assembly);
    }

    private IServiceProvider BuildHotfixProvider(
        Assembly hotfixAssembly)
    {
        return BuildHotfixProvider(Array.Empty<ServiceDescriptor>(), hotfixAssembly);
    }

    private IServiceProvider BuildHotfixProvider(
        IReadOnlyList<ServiceDescriptor> startupServices,
        Assembly hotfixAssembly)
    {
        return BuildHotfixProvider(startupServices, hotfixAssembly, Array.Empty<Type>());
    }

    private IServiceProvider BuildHotfixProvider(
        IReadOnlyList<ServiceDescriptor> startupServices,
        Assembly hotfixAssembly,
        IReadOnlyList<Type> moduleTypes,
        List<string>? dependencyWarnings = null)
    {
        ArgumentNullException.ThrowIfNull(startupServices);
        ArgumentNullException.ThrowIfNull(hotfixAssembly);
        ArgumentNullException.ThrowIfNull(moduleTypes);

        var registrations = DiscoverGeneratedServiceRegistrations(hotfixAssembly);
        var rawServices = new ServiceCollection();
        foreach (var descriptor in startupServices)
        {
            ((ICollection<ServiceDescriptor>)rawServices).Add(descriptor);
        }

        foreach (var registration in registrations)
        {
            registration.Register(rawServices);
        }

        foreach (var moduleType in moduleTypes)
        {
            if (rawServices.All(descriptor => descriptor.ServiceType != moduleType))
            {
                ((ICollection<ServiceDescriptor>)rawServices).Add(
                    ServiceDescriptor.Singleton(moduleType, moduleType));
            }
        }

        var warnings = HotfixComponentDependencyPrecheck.Validate(rawServices.ToArray(), hotfixAssembly, _rootServices);
        dependencyWarnings?.AddRange(warnings);
        var services = new ServiceCollection();
        var activationTracker = new HotfixActivationTracker();
        foreach (var descriptor in rawServices)
        {
            ((ICollection<ServiceDescriptor>)services).Add(
                CreateFallbackActivationDescriptor(descriptor, _rootServices, activationTracker));
        }

        var hotfixProvider = services.BuildServiceProvider(validateScopes: true);
        return _rootServices is null
            ? hotfixProvider
            : new FallbackServiceProvider(hotfixProvider, _rootServices);
    }

    private static IReadOnlyList<IHotfixGeneratedServiceRegistration> DiscoverGeneratedServiceRegistrations(
        Assembly hotfixAssembly)
    {
        return hotfixAssembly
            .GetTypes()
            .Where(static type => !type.IsAbstract
                && !type.IsInterface
                && typeof(IHotfixGeneratedServiceRegistration).IsAssignableFrom(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(static type =>
            {
                try
                {
                    return (IHotfixGeneratedServiceRegistration)Activator.CreateInstance(type)!;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Could not activate hotfix generated service registration '{type.FullName}'.",
                        ex);
                }
            })
            .ToArray();
    }

    private static ServiceDescriptor CreateFallbackActivationDescriptor(
        ServiceDescriptor descriptor,
        IServiceProvider? rootServices,
        HotfixActivationTracker activationTracker)
    {
        // Preserve native keyed activation; it does not use the two-provider fallback.
        if (descriptor.IsKeyedService)
        {
            if (descriptor.KeyedImplementationFactory is not { } keyedFactory) return descriptor;
            return ServiceDescriptor.DescribeKeyed(descriptor.ServiceType, descriptor.ServiceKey,
                (provider, key) =>
                {
                    using var activation = activationTracker.Enter(descriptor);
                    return keyedFactory(provider, key);
                }, descriptor.Lifetime);
        }

        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return ServiceDescriptor.Describe(
                descriptor.ServiceType,
                provider =>
                {
                    using var activation = activationTracker.Enter(descriptor);
                    return descriptor.ImplementationFactory(rootServices is null
                        ? provider
                        : new ActivationFallbackServiceProvider(provider, rootServices));
                },
                descriptor.Lifetime);
        }

        if (rootServices is not null && descriptor.ImplementationType is not null && !descriptor.ServiceType.IsGenericTypeDefinition)
        {
            return ServiceDescriptor.Describe(
                descriptor.ServiceType,
                provider =>
                {
                    using var activation = activationTracker.Enter(descriptor);
                    return ActivatorUtilities.CreateInstance(
                        new ActivationFallbackServiceProvider(provider, rootServices),
                        descriptor.ImplementationType);
                },
                descriptor.Lifetime);
        }

        return descriptor;
    }

    private async ValueTask<IReadOnlyList<Exception>> RetireCandidateAsync(HotfixRuntimeSnapshot runtime)
    {
        runtime.Retire();
        return await ObserveAndLogRetirementAsync(
            runtime,
            "candidate",
            runtime.SourceVersion,
            runtime.SourcePath).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Exception>> ObserveAndLogRetirementAsync(
        HotfixRuntimeSnapshot runtime,
        string phase,
        string? version,
        string? sourcePath)
    {
        var failures = await runtime.RetirementCompletion.ConfigureAwait(false);
        LogResourceCleanupResult(failures, phase, version, sourcePath);
        return failures;
    }

    private void LogResourceCleanupResult(
        IReadOnlyList<Exception> failures,
        string phase,
        string? version,
        string? sourcePath)
    {
        if (_logger is null)
        {
            return;
        }

        if (failures.Count == 0)
        {
            _logger.LogInformation(
                "Hotfix generation unloaded during {Phase}. Version={HotfixVersion}. SourcePath={HotfixPath}.",
                phase,
                version ?? "(unknown)",
                sourcePath ?? "(unresolved)");
            return;
        }

        foreach (var failure in failures)
        {
            _logger.LogError(
                failure,
                "Hotfix resource cleanup failed during {Phase} for version {HotfixVersion}: {CleanupError}",
                phase,
                version,
                failure.Message);
        }
    }

    private sealed class ActivationFallbackServiceProvider(
        IServiceProvider hotfixServices,
        IServiceProvider rootServices) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider))
            {
                return this;
            }

            return TryGetCombinedEnumerable(serviceType, hotfixServices, rootServices, out var services)
                ? services
                : hotfixServices.GetService(serviceType) ?? rootServices.GetService(serviceType);
        }
    }

    private sealed class FallbackServiceProvider(
        IServiceProvider hotfixServices,
        IServiceProvider rootServices) : IServiceProvider, IDisposable, IAsyncDisposable
    {
        public object? GetService(Type serviceType)
        {
            return TryGetCombinedEnumerable(serviceType, hotfixServices, rootServices, out var services)
                ? services
                : hotfixServices.GetService(serviceType) ?? rootServices.GetService(serviceType);
        }

        public void Dispose()
        {
            (hotfixServices as IDisposable)?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (hotfixServices is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                return;
            }

            (hotfixServices as IDisposable)?.Dispose();
        }
    }

    private static bool TryGetCombinedEnumerable(
        Type serviceType,
        IServiceProvider hotfixServices,
        IServiceProvider rootServices,
        out object? services)
    {
        services = null;
        if (!serviceType.IsGenericType ||
            serviceType.GetGenericTypeDefinition() != typeof(IEnumerable<>))
        {
            return false;
        }

        var elementType = serviceType.GetGenericArguments()[0];
        var hotfixItems = ToList(hotfixServices.GetService(serviceType));
        var rootItems = ToList(rootServices.GetService(serviceType));
        var combined = Array.CreateInstance(elementType, hotfixItems.Count + rootItems.Count);
        var index = 0;
        foreach (var item in hotfixItems)
        {
            combined.SetValue(item, index++);
        }

        foreach (var item in rootItems)
        {
            combined.SetValue(item, index++);
        }

        services = combined;
        return true;
    }

    private static List<object?> ToList(object? services)
    {
        var list = new List<object?>();
        if (services is System.Collections.IEnumerable enumerable)
        {
            foreach (var service in enumerable)
            {
                list.Add(service);
            }
        }

        return list;
    }
}
