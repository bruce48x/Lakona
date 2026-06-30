using Lakona.Game.Server.Hotfix;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class HotfixDiagnosticsProvider : ILakonaDiagnosticsSnapshotProvider
{
    private readonly IHotfixManager? _manager;

    public HotfixDiagnosticsProvider(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _manager = services.GetService<IHotfixManager>();
    }

    public string Name => "hotfix";

    public ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_manager is null)
        {
            return new ValueTask<object>(new HotfixDiagnosticsSnapshot(
                Status: "unavailable",
                LoadedVersion: null,
                LoadedAtUtc: null,
                DispatchTableVersion: 0,
                MethodCount: 0,
                FeatureCount: 0,
                LastReloadStatus: null,
                LastFailureExceptionType: null));
        }

        var current = _manager.Current;
        return new ValueTask<object>(new HotfixDiagnosticsSnapshot(
            Status: "available",
            LoadedVersion: current.Version,
            LoadedAtUtc: current.LoadedAtUtc,
            DispatchTableVersion: current.DispatchTableVersion,
            MethodCount: current.Methods.Count,
            FeatureCount: current.Features.Count,
            LastReloadStatus: current.LastReloadStatus?.ToString(),
            LastFailureExceptionType: current.LastFailureExceptionType));
    }

    private sealed record HotfixDiagnosticsSnapshot(
        string Status,
        string? LoadedVersion,
        DateTimeOffset? LoadedAtUtc,
        long DispatchTableVersion,
        int MethodCount,
        int FeatureCount,
        string? LastReloadStatus,
        string? LastFailureExceptionType);
}
