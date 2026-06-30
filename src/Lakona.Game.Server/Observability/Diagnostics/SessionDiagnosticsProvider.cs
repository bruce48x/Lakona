using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class SessionDiagnosticsProvider : ILakonaDiagnosticsSnapshotProvider
{
    private readonly IGameSessionRegistry? _sessions;

    public SessionDiagnosticsProvider(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _sessions = services.GetService<IGameSessionRegistry>();
    }

    public string Name => "sessions";

    public ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<object>(_sessions?.GetDiagnosticsSnapshot() ?? new GameSessionDiagnosticsSnapshot(0, 0, 0, 0, 0, 0));
    }
}
