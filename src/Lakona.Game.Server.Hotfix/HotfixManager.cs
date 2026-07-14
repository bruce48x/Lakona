using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixManager : IHotfixManager, IHotfixServiceProviderAccessor, IHotfixRuntimeAccessor
{
    private const string LoggerCategory = "Lakona.Game.Hotfix";

    private readonly IHotfixAssemblySource _source;
    private readonly IReadOnlyList<string> _hostAssemblyNames;
    private readonly IReadOnlyList<Type> _requiredServiceContracts;
    private readonly IServiceProvider? _rootServices;
    private readonly ILogger? _logger;
    private readonly IReadOnlyList<IHotfixRuntimePublicationParticipant> _publicationParticipants;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
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
    }

    public event EventHandler<HotfixReloadResult>? Reloaded;

    public HotfixSnapshot Current => Volatile.Read(ref _publication).Snapshot;

    IServiceProvider IHotfixServiceProviderAccessor.Current =>
        HotfixDispatchRuntimeScope.CurrentServices ?? Volatile.Read(ref _publication).Runtime.Services;

    HotfixRuntimeSnapshot IHotfixRuntimeAccessor.Current => Volatile.Read(ref _publication).Runtime;

    HotfixRuntimeSnapshotLease IHotfixRuntimeAccessor.AcquireCurrent()
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _publication).Runtime;
            try
            {
                return snapshot.AcquireLease();
            }
            catch (ObjectDisposedException) when (!ReferenceEquals(snapshot, Volatile.Read(ref _publication).Runtime))
            {
            }
        }
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

        await _reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(source, publish: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public async ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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

            var boundaryDiagnostics = HotfixDispatchBoundaryValidator.Validate(pendingContext, scan.Methods, scan.Services);
            if (boundaryDiagnostics.Count != 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, boundaryDiagnostics));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var actorHosts = CreateActorHostDescriptors(scan, resolved.Version);
            var tableVersion = publish ? Interlocked.Increment(ref _nextVersion) : Current.DispatchTableVersion;
            var table = new HotfixDispatchTable(
                tableVersion,
                scan.Methods,
                scan.Services,
                scan.ActorMethods,
                scan.ActorLifecycles);
            pendingTable = table;
            table.ValidateMethodShapes();
            table.ValidateTypedDispatchDelegates();
            hotfixProvider = BuildHotfixProvider(scan.StartupServices, assembly);
            table.ValidateServiceActivation(hotfixProvider);
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
                await table.DisposeAsync().ConfigureAwait(false);
                pendingTable = null;
                DisposeQuietly(hotfixProvider);
                pendingContext.Unload();
                pendingContext = null;
                return new HotfixReloadResult(HotfixReloadStatus.Succeeded, snapshot, resolved.Version, resolved.AssemblyPath, Array.Empty<string>());
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
            var result = await PublishCandidateAsync(
                runtimeSnapshot,
                snapshot,
                cancellationToken,
                resolved.Version,
                resolved.AssemblyPath).ConfigureAwait(false);
            hotfixProvider = null;
            pendingContext = null;
            if (!result.Succeeded)
            {
                return result;
            }

            Reloaded?.Invoke(this, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (pendingTable is not null)
            {
                await pendingTable.DisposeAsync().ConfigureAwait(false);
            }
            DisposeQuietly(hotfixProvider);
            pendingContext?.Unload();
            throw;
        }
        catch (Exception ex)
        {
            if (pendingTable is not null)
            {
                await pendingTable.DisposeAsync().ConfigureAwait(false);
            }
            DisposeQuietly(hotfixProvider);
            pendingContext?.Unload();

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
                [ex.Message],
                ex.Message,
                ex.GetType().FullName);
        }
    }

    private static IReadOnlyList<HotfixActorHostDescriptor> CreateActorHostDescriptors(
        HotfixBehaviorScanResult scan,
        string? buildTag)
    {
        var descriptors = new Dictionary<string, HotfixActorHostDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var startup in scan.ActorStartups)
        {
            if (startup.IsLegacy)
            {
                AddActorHostDescriptor(
                    descriptors,
                    startup.Name!,
                    "startup:" + startup.Name,
                    buildTag);
                continue;
            }

            var actorType = startup.ActorType!;
            var keyType = startup.KeyType!;
            AddActorHostDescriptor(
                descriptors,
                ActorNameConventions.Resolve(actorType),
                $"startup:v1:{actorType.FullName}:{keyType.FullName}",
                buildTag);
        }

        foreach (var placement in scan.ActorPlacements)
        {
            var name = ActorNameConventions.Resolve(placement.ActorType);
            AddActorHostDescriptor(
                descriptors,
                name,
                "placement:" + placement.ActorType.FullName,
                buildTag);
        }

        return descriptors.Values
            .OrderBy(static descriptor => descriptor.Actor, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddActorHostDescriptor(
        IDictionary<string, HotfixActorHostDescriptor> descriptors,
        string actor,
        string policyHash,
        string? buildTag)
    {
        descriptors[actor] = new HotfixActorHostDescriptor(
            actor,
            policyHash,
            string.IsNullOrWhiteSpace(buildTag) ? "hotfix" : buildTag);
    }

    internal async ValueTask<HotfixReloadResult> PublishCandidateAsync(
        HotfixRuntimeSnapshot runtimeSnapshot,
        HotfixSnapshot snapshot,
        CancellationToken cancellationToken,
        string? requestedVersion = null,
        string? requestedPath = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeSnapshot);
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        var previousPublication = Volatile.Read(ref _publication);
        var transactions = new List<IHotfixRuntimePublicationTransaction>(_publicationParticipants.Count);
        var swapped = false;
        try
        {
            foreach (var participant in _publicationParticipants)
            {
                transactions.Add(await participant.PrepareAsync(
                    previousPublication.Runtime,
                    runtimeSnapshot,
                    cancellationToken).ConfigureAwait(false));
            }

            var nextPublication = new HotfixPublicationState(
                snapshot,
                runtimeSnapshot,
                runtimeSnapshot.DispatchTable ?? previousPublication.DispatchTable);
            HotfixDispatch.ReplaceProvider(() => Volatile.Read(ref _publication).DispatchTable);
            Volatile.Write(ref _publication, nextPublication);
            swapped = true;

            foreach (var transaction in transactions)
            {
                await transaction.ActivateAsync(cancellationToken).ConfigureAwait(false);
            }

        }
        catch (OperationCanceledException cancellationException)
        {
            if (swapped) Volatile.Write(ref _publication, previousPublication);
            var rollbackFailures = await RollbackPublicationTransactionsAsync(transactions).ConfigureAwait(false);
            await DisposePublicationTransactionsAsync(transactions).ConfigureAwait(false);
            runtimeSnapshot.Retire();
            if (rollbackFailures.Count != 0)
                throw new AggregateException("Hotfix publication cancellation rollback failed.", [cancellationException, .. rollbackFailures]);
            throw;
        }
        catch (Exception ex)
        {
            if (swapped) Volatile.Write(ref _publication, previousPublication);
            var rollbackFailures = await RollbackPublicationTransactionsAsync(transactions).ConfigureAwait(false);
            await DisposePublicationTransactionsAsync(transactions).ConfigureAwait(false);
            runtimeSnapshot.Retire();
            var failure = rollbackFailures.Count == 0
                ? ex
                : new AggregateException("Hotfix publication and rollback failed.", [ex, .. rollbackFailures]);
            return new HotfixReloadResult(
                HotfixReloadStatus.Failed,
                previousPublication.Snapshot,
                requestedVersion ?? snapshot.Version,
                requestedPath ?? snapshot.SourcePath,
                [failure.Message, .. rollbackFailures.Select(static item => item.Message)],
                failure.Message,
                failure.GetType().FullName);
        }

        foreach (var transaction in transactions)
        {
            try
            {
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Hotfix publication cleanup commit failed.");
            }
        }

        await DisposePublicationTransactionsAsync(transactions).ConfigureAwait(false);

        previousPublication.Runtime.Retire();

        return new HotfixReloadResult(
            HotfixReloadStatus.Succeeded,
            snapshot,
            requestedVersion ?? snapshot.Version,
            requestedPath ?? snapshot.SourcePath,
            Array.Empty<string>());
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

    private static async ValueTask DisposePublicationTransactionsAsync(
        IReadOnlyList<IHotfixRuntimePublicationTransaction> transactions)
    {
        foreach (var transaction in transactions)
        {
            try { await transaction.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }

    private void LogReloadResult(HotfixReloadResult result)
    {
        if (_logger is null)
        {
            return;
        }

        if (result.Succeeded)
        {
            _logger.LogInformation(
                "Hotfix reload succeeded from {HotfixPath} with {MethodCount} method(s).",
                result.Current.SourcePath,
                result.Current.Methods.Count);
            return;
        }

        _logger.LogError(
            "Hotfix reload failed for {HotfixPath}: {ErrorMessage}",
            result.RequestedPath ?? result.Current.SourcePath ?? "(unresolved)",
            result.ErrorMessage ?? string.Join(Environment.NewLine, result.Diagnostics));
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
        ArgumentNullException.ThrowIfNull(startupServices);
        ArgumentNullException.ThrowIfNull(hotfixAssembly);

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

        var services = new ServiceCollection();
        foreach (var descriptor in rawServices)
        {
            ((ICollection<ServiceDescriptor>)services).Add(_rootServices is null
                ? descriptor
                : CreateFallbackActivationDescriptor(descriptor, _rootServices));
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
        IServiceProvider rootServices)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return ServiceDescriptor.Describe(
                descriptor.ServiceType,
                provider => descriptor.ImplementationFactory(
                    new ActivationFallbackServiceProvider(provider, rootServices)),
                descriptor.Lifetime);
        }

        if (descriptor.ImplementationType is not null && !descriptor.ServiceType.IsGenericTypeDefinition)
        {
            return ServiceDescriptor.Describe(
                descriptor.ServiceType,
                provider => ActivatorUtilities.CreateInstance(
                    new ActivationFallbackServiceProvider(provider, rootServices),
                    descriptor.ImplementationType),
                descriptor.Lifetime);
        }

        return descriptor;
    }

    private static void UnloadQuietly(HotfixAssemblyLoadContext? loadContext)
    {
        try
        {
            loadContext?.Unload();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void DisposeQuietly(IServiceProvider? provider)
    {
        try
        {
            switch (provider)
            {
                case IAsyncDisposable asyncDisposable:
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch (InvalidOperationException)
        {
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
