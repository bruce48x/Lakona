using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixManager : IHotfixManager, IHotfixServiceProviderAccessor, IHotfixRuntimeAccessor
{
    private readonly IHotfixAssemblySource _source;
    private readonly IReadOnlyList<string> _sharedAssemblyNames;
    private readonly IReadOnlyList<Type> _requiredServiceContracts;
    private readonly IServiceProvider? _rootServices;
    private readonly HotfixFeatureLifecycleCoordinator _featureLifecycle = new();
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private long _nextVersion;
    private HotfixFeatureLifecycleSnapshot _currentFeatureLifecycle = HotfixFeatureLifecycleSnapshot.Empty;
    private HotfixSnapshot _current = new(null, null, null, null, 0, Array.Empty<HotfixMethodKey>(), null, null, null);
    private HotfixRuntimeSnapshot _currentRuntime = new(
        new HotfixServiceInvoker(HotfixDispatch.Current),
        EmptyHotfixFeatureCommandInvoker.Instance,
        EmptyServiceProvider.Instance);

    public HotfixManager(
        IHotfixAssemblySource source,
        IEnumerable<string>? sharedAssemblyNames = null,
        IEnumerable<Type>? requiredServiceContracts = null,
        IServiceProvider? rootServices = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _rootServices = rootServices;
        _sharedAssemblyNames = (sharedAssemblyNames ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _requiredServiceContracts = (requiredServiceContracts ?? Array.Empty<Type>())
            .Distinct()
            .ToArray();
    }

    public event EventHandler<HotfixReloadResult>? Reloaded;

    public HotfixSnapshot Current => Volatile.Read(ref _current);

    IServiceProvider IHotfixServiceProviderAccessor.Current =>
        HotfixDispatchRuntimeScope.CurrentServices ?? Volatile.Read(ref _currentRuntime).Services;

    HotfixRuntimeSnapshot IHotfixRuntimeAccessor.Current => Volatile.Read(ref _currentRuntime);

    HotfixRuntimeSnapshotLease IHotfixRuntimeAccessor.AcquireCurrent()
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _currentRuntime);
            try
            {
                return snapshot.AcquireLease();
            }
            catch (ObjectDisposedException) when (!ReferenceEquals(snapshot, Volatile.Read(ref _currentRuntime)))
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
            return await LoadCoreAsync(_source, publish: true, cancellationToken).ConfigureAwait(false);
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
        try
        {
            resolved = await source.ResolveAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(resolved.AssemblyPath))
            {
                throw new FileNotFoundException("Hotfix assembly was not found.", resolved.AssemblyPath);
            }

            pendingContext = new HotfixAssemblyLoadContext(resolved.AssemblyPath, _sharedAssemblyNames);
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

            var tableVersion = publish ? Interlocked.Increment(ref _nextVersion) : Current.DispatchTableVersion;
            var table = new HotfixDispatchTable(tableVersion, scan.Methods, scan.Services, scan.Features);
            table.ValidateMethodShapes();
            table.ValidateTypedDispatchDelegates();
            table.ValidateFeatureTickMethods(scan.Features);
            table.ValidateFeatureCommandMethods();
            hotfixProvider = BuildHotfixProvider(scan);
            table.ValidateServiceActivation(hotfixProvider);
            table.ValidateFeatureCommandActivation(hotfixProvider);
            var snapshot = new HotfixSnapshot(
                resolved.Version,
                resolved.SourceKind,
                resolved.AssemblyPath,
                DateTimeOffset.UtcNow,
                tableVersion,
                table.MethodKeys,
                HotfixReloadStatus.Succeeded,
                null,
                null,
                scan.Features);

            if (!publish)
            {
                DisposeQuietly(hotfixProvider);
                pendingContext.Unload();
                pendingContext = null;
                return new HotfixReloadResult(HotfixReloadStatus.Succeeded, snapshot, resolved.Version, resolved.AssemblyPath, Array.Empty<string>());
            }

            var runtimeSnapshot = new HotfixRuntimeSnapshot(
                new HotfixServiceInvoker(table),
                new HotfixFeatureCommandInvoker(table),
                hotfixProvider,
                table,
                hotfixProvider,
                assembly,
                pendingContext,
                resolved.Version,
                resolved.SourceKind,
                resolved.AssemblyPath,
                ownsRuntimeResources: true,
                onRetired: null);
            var nextFeatureLifecycle = await _featureLifecycle.StartCandidateAsync(
                Volatile.Read(ref _currentFeatureLifecycle),
                runtimeSnapshot,
                scan.Features,
                cancellationToken).ConfigureAwait(false);

            HotfixDispatch.Replace(table);
            var oldRuntime = Interlocked.Exchange(ref _currentRuntime, runtimeSnapshot);
            var oldFeatureLifecycle = Interlocked.Exchange(ref _currentFeatureLifecycle, nextFeatureLifecycle);
            hotfixProvider = null;
            pendingContext = null;
            Volatile.Write(ref _current, snapshot);
            await _featureLifecycle.CommitCandidateTimersAsync(
                nextFeatureLifecycle,
                CancellationToken.None).ConfigureAwait(false);
            await _featureLifecycle.StopRemovedAsync(
                oldFeatureLifecycle,
                nextFeatureLifecycle,
                CancellationToken.None).ConfigureAwait(false);
            oldRuntime.Retire();

            var result = new HotfixReloadResult(HotfixReloadStatus.Succeeded, snapshot, resolved.Version, resolved.AssemblyPath, Array.Empty<string>());
            Reloaded?.Invoke(this, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            DisposeQuietly(hotfixProvider);
            pendingContext?.Unload();
            throw;
        }
        catch (Exception ex)
        {
            DisposeQuietly(hotfixProvider);
            pendingContext?.Unload();

            var previous = Current;
            var snapshot = new HotfixSnapshot(
                previous.Version,
                previous.SourceKind,
                previous.SourcePath,
                previous.LoadedAtUtc,
                previous.DispatchTableVersion,
                previous.Methods,
                HotfixReloadStatus.Failed,
                ex.Message,
                ex.GetType().FullName,
                previous.Features);
            if (publish)
            {
                Volatile.Write(ref _current, snapshot);
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

    private IServiceProvider BuildHotfixProvider(HotfixBehaviorScanResult scan)
    {
        var services = new ServiceCollection();
        foreach (var descriptor in scan.Features.SelectMany(static feature => feature.Services))
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
        if (ReferenceEquals(provider, EmptyServiceProvider.Instance))
        {
            return;
        }

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

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class ActivationFallbackServiceProvider(
        IServiceProvider hotfixServices,
        IServiceProvider rootServices) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IServiceProvider)
                ? this
                : hotfixServices.GetService(serviceType) ?? rootServices.GetService(serviceType);
        }
    }

    private sealed class FallbackServiceProvider(
        IServiceProvider hotfixServices,
        IServiceProvider rootServices) : IServiceProvider, IDisposable, IAsyncDisposable
    {
        public object? GetService(Type serviceType)
        {
            return hotfixServices.GetService(serviceType) ?? rootServices.GetService(serviceType);
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
}
