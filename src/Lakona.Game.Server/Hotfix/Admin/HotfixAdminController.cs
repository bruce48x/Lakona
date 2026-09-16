using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Lakona.Game.Server.HotfixAdmin;

public sealed class HotfixAdminController
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly HotfixAdminOptions _options;
    private readonly HotfixVersionStore _store;
    private readonly IHotfixManager _manager;
    private readonly ILogger<HotfixAdminController> _logger;
    private HotfixAdminDiagnostic? _lastOperationFailure;

    public HotfixAdminController(
        HotfixAdminOptions options,
        HotfixVersionStore store,
        IHotfixManager manager,
        ILogger<HotfixAdminController>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _logger = logger ?? NullLogger<HotfixAdminController>.Instance;
    }

    public async Task<HotfixStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var current = await _store.ReadPointerAsync("current.txt", cancellationToken).ConfigureAwait(false);
        var previous = await _store.ReadPointerAsync("previous.txt", cancellationToken).ConfigureAwait(false);
        var snapshot = _manager.Current;
        return new HotfixStatusResponse(
            _options.DebugWatcher,
            current,
            previous,
            snapshot.Version,
            snapshot.DispatchTableVersion,
            snapshot.Methods.Count,
            snapshot.LastReloadStatus?.ToString(),
            snapshot.LastFailureMessage,
            _options.BuildTag)
        {
            LastOperationFailure = Volatile.Read(ref _lastOperationFailure)
        };
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
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = await _store.ReadPointerAsync("previous.txt", cancellationToken).ConfigureAwait(false)
                ?? throw Failure("HOTFIX_NO_PREVIOUS_VERSION", "request", null,
                    "No previous hotfix version is available.", "Install and activate a known-good version explicitly.");
            return await ActivateCoreAsync(previous, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<HotfixStatusResponse> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _manager.ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw ResultFailure(result, "reload", result.RequestedVersion);
            }

            Volatile.Write(ref _lastOperationFailure, null);
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
            throw Failure("HOTFIX_CURRENT_VERSION_CHANGED", "request", version,
                $"Hotfix current version changed before activation: expected '{expectedCurrentVersion}', found '{oldCurrent}'.",
                "Read hotfix status and retry with the current version after checking concurrent deployment activity.");
        }

        HotfixPackageManifest manifest;
        try
        {
            manifest = await _store.ReadManifestAsync(version, cancellationToken).ConfigureAwait(false);
            await _store.ValidateChecksumsAsync(version, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException or IOException or UnauthorizedAccessException)
        {
            throw Failure("HOTFIX_PACKAGE_INVALID", "package", version, exception.Message,
                "Reinstall the original hotfix package; verify its manifest, checksums, READY marker and file permissions.");
        }
        if (!StringComparer.Ordinal.Equals(manifest.BuildTag, _options.BuildTag))
        {
            throw Failure("HOTFIX_BUILD_TAG_MISMATCH", "package", version,
                $"Hotfix package BuildTag '{manifest.BuildTag}' does not match running server BuildTag '{_options.BuildTag}'.",
                "Build the hotfix against this server's stable release, or deploy the matching full server package.");
        }

        var validationSource = new CurrentDirectoryHotfixAssemblySource(
            _store.VersionDirectory(version),
            manifest.Assembly);
        var validation = await _manager.ValidateAsync(validationSource, cancellationToken).ConfigureAwait(false);
        if (!validation.Succeeded)
        {
            throw ResultFailure(validation, "validation", version);
        }

        try
        {
            await _store.WritePointerAsync("previous.txt", oldCurrent, cancellationToken).ConfigureAwait(false);
            await _store.WritePointerAsync("current.txt", version, cancellationToken).ConfigureAwait(false);

            var result = await _manager.ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw ResultFailure(result, "reload", version);
            }
        }
        catch
        {
            await _store.WritePointerAsync("current.txt", oldCurrent, CancellationToken.None).ConfigureAwait(false);
            await _store.WritePointerAsync("previous.txt", oldPrevious, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        Volatile.Write(ref _lastOperationFailure, null);
        return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    internal HotfixAdminException InvalidRequest() => Failure("HOTFIX_INVALID_REQUEST", "request", null,
        "A JSON request body with a hotfix version is required.", "Provide a JSON object with a non-empty version field.");

    private HotfixAdminException ResultFailure(HotfixReloadResult result, string stage, string? candidate) =>
        Failure(stage == "validation" ? "HOTFIX_VALIDATION_FAILED" : "HOTFIX_RELOAD_FAILED", stage,
            candidate, result.ErrorMessage ?? $"Hotfix {stage} failed.",
            stage == "validation"
                ? "Fix the reported declarations, constructor dependencies or configuration, rebuild the candidate and retry activation."
                : "Check the reported loading or publication failure and server logs; fix the candidate or its runtime prerequisites before retrying.",
            result.Diagnostics);

    private HotfixAdminException Failure(string code, string stage, string? candidate, string message,
        string remediation, IReadOnlyList<string>? diagnostics = null)
    {
        var current = _manager.Current;
        var diagnostic = new HotfixAdminDiagnostic(code, stage, candidate, message, remediation,
            Guid.NewGuid().ToString("N"), current.Version, current.DispatchTableVersion,
            Array.AsReadOnly(diagnostics?.ToArray() ?? []));
        Volatile.Write(ref _lastOperationFailure, diagnostic);
        _logger.LogWarning("Hotfix operation failed: {Code}, stage {Stage}, candidate {CandidateVersion}, correlation {CorrelationId}: {Message}",
            code, stage, candidate, diagnostic.CorrelationId, message);
        return new HotfixAdminException(diagnostic);
    }
}
