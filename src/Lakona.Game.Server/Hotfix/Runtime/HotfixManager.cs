using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.BuildTag;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Scanning;
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

            var actorHosts = HotfixRuntimeComposition.CreateActorHostDescriptors(scan, resolved.Version);
            var tableVersion = publish ? Interlocked.Increment(ref _nextVersion) : Current.DispatchTableVersion;
            var table = HotfixRuntimeComposition.CreateDispatchTable(scan, tableVersion, _rootServices);
            pendingTable = table;
            table.ValidateMethodShapes();
            hotfixProvider = HotfixRuntimeComposition.BuildProvider(scan.StartupServices, assembly, table.ModuleTypes, _rootServices, dependencyWarnings);
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

}
