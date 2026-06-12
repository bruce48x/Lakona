using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Loading;

namespace Lakona.Game.Server.HotfixAdmin;

public sealed class HotfixAdminController
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly HotfixAdminOptions _options;
    private readonly HotfixVersionStore _store;
    private readonly IHotfixManager _manager;

    public HotfixAdminController(
        HotfixAdminOptions options,
        HotfixVersionStore store,
        IHotfixManager manager)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _options.Validate();
    }

    public async Task<HotfixStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var current = await _store.ReadPointerAsync("current.txt", cancellationToken).ConfigureAwait(false);
        var previous = await _store.ReadPointerAsync("previous.txt", cancellationToken).ConfigureAwait(false);
        var snapshot = _manager.Current;
        return new HotfixStatusResponse(
            _options.Mode,
            current,
            previous,
            snapshot.Version,
            snapshot.DispatchTableVersion,
            snapshot.Methods.Count,
            snapshot.LastReloadStatus?.ToString(),
            snapshot.LastFailureMessage,
            _options.BuildTag);
    }

    public async Task<HotfixStatusResponse> ActivateAsync(HotfixActivateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ActivateCoreAsync(request.Version, request.ExpectedCurrentVersion, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<HotfixStatusResponse> RollbackAsync(CancellationToken cancellationToken = default)
    {
        var previous = await _store.ReadPointerAsync("previous.txt", cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No previous hotfix version is available.");
        return await ActivateAsync(new HotfixActivateRequest(previous, null, "rollback"), cancellationToken).ConfigureAwait(false);
    }

    public async Task<HotfixStatusResponse> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _manager.ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "Hotfix reload failed.");
            }

            return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<HotfixStatusResponse> ActivateCoreAsync(
        string version,
        string? expectedCurrentVersion,
        CancellationToken cancellationToken)
    {
        var oldCurrent = await _store.ReadPointerAsync("current.txt", cancellationToken).ConfigureAwait(false);
        var oldPrevious = await _store.ReadPointerAsync("previous.txt", cancellationToken).ConfigureAwait(false);
        if (expectedCurrentVersion is not null && !StringComparer.Ordinal.Equals(oldCurrent, expectedCurrentVersion))
        {
            throw new InvalidOperationException("Hotfix current version changed before activation.");
        }

        var manifest = await _store.ReadManifestAsync(version, cancellationToken).ConfigureAwait(false);
        await _store.ValidateChecksumsAsync(version, cancellationToken).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(manifest.BuildTag, _options.BuildTag))
        {
            throw new InvalidOperationException("Hotfix package BuildTag does not match the running server BuildTag.");
        }

        var validationSource = new CurrentDirectoryHotfixAssemblySource(
            _store.VersionDirectory(version),
            manifest.Assembly);
        var validation = await _manager.ValidateAsync(validationSource, cancellationToken).ConfigureAwait(false);
        if (!validation.Succeeded)
        {
            throw new InvalidOperationException(validation.ErrorMessage ?? "Hotfix validation failed.");
        }

        try
        {
            await _store.WritePointerAsync("previous.txt", oldCurrent, cancellationToken).ConfigureAwait(false);
            await _store.WritePointerAsync("current.txt", version, cancellationToken).ConfigureAwait(false);

            var result = await _manager.ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "Hotfix reload failed.");
            }
        }
        catch
        {
            await _store.WritePointerAsync("current.txt", oldCurrent, CancellationToken.None).ConfigureAwait(false);
            await _store.WritePointerAsync("previous.txt", oldPrevious, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
    }
}
