using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Scanning;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixManager : IHotfixManager
{
    private readonly IHotfixAssemblySource _source;
    private readonly IReadOnlyList<string> _sharedAssemblyNames;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private long _nextVersion;
    private HotfixSnapshot _current = new(null, null, null, null, 0, Array.Empty<HotfixMethodKey>(), null, null, null);
    private HotfixAssemblyLoadContext? _loadContext;

    public HotfixManager(IHotfixAssemblySource source, IEnumerable<string>? sharedAssemblyNames = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sharedAssemblyNames = (sharedAssemblyNames ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public HotfixSnapshot Current => Volatile.Read(ref _current);

    public async ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(publish: false, cancellationToken).ConfigureAwait(false);
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
            return await LoadCoreAsync(publish: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private async ValueTask<HotfixReloadResult> LoadCoreAsync(bool publish, CancellationToken cancellationToken)
    {
        HotfixAssemblySourceResult? resolved = null;
        HotfixAssemblyLoadContext? pendingContext = null;
        try
        {
            resolved = await _source.ResolveAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(resolved.AssemblyPath))
            {
                throw new FileNotFoundException("Hotfix assembly was not found.", resolved.AssemblyPath);
            }

            pendingContext = new HotfixAssemblyLoadContext(resolved.AssemblyPath, _sharedAssemblyNames);
            var assembly = pendingContext.LoadMainAssemblyFromBytes(resolved.AssemblyPath);
            var scan = HotfixBehaviorScanner.Scan(assembly);
            if (!scan.Succeeded)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, scan.Diagnostics));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var boundaryDiagnostics = HotfixDispatchBoundaryValidator.Validate(pendingContext, scan.Methods);
            if (boundaryDiagnostics.Count != 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, boundaryDiagnostics));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var tableVersion = publish ? Interlocked.Increment(ref _nextVersion) : Current.DispatchTableVersion;
            var table = new HotfixDispatchTable(tableVersion, scan.Methods, scan.Services);
            table.ValidateMethodShapes();
            table.ValidateTypedDispatchDelegates();
            var snapshot = new HotfixSnapshot(
                resolved.Version,
                resolved.SourceKind,
                resolved.AssemblyPath,
                DateTimeOffset.UtcNow,
                tableVersion,
                table.MethodKeys,
                HotfixReloadStatus.Succeeded,
                null,
                null);

            if (!publish)
            {
                pendingContext.Unload();
                pendingContext = null;
                return new HotfixReloadResult(HotfixReloadStatus.Succeeded, snapshot, resolved.Version, resolved.AssemblyPath, Array.Empty<string>());
            }

            HotfixDispatch.Replace(table);
            var oldContext = Interlocked.Exchange(ref _loadContext, pendingContext);
            pendingContext = null;
            Volatile.Write(ref _current, snapshot);
            UnloadQuietly(oldContext);

            return new HotfixReloadResult(HotfixReloadStatus.Succeeded, snapshot, resolved.Version, resolved.AssemblyPath, Array.Empty<string>());
        }
        catch (OperationCanceledException)
        {
            pendingContext?.Unload();
            throw;
        }
        catch (Exception ex)
        {
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
                ex.GetType().FullName);
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
}
