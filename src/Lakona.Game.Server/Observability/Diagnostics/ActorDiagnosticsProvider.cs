using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class ActorDiagnosticsProvider : ILakonaDiagnosticsSnapshotProvider
{
    private readonly IActorRuntime? _runtime;

    public ActorDiagnosticsProvider(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _runtime = services.GetService<IActorRuntime>();
    }

    public string Name => "actors";

    public ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<object>(_runtime?.GetDiagnosticsSnapshot() ?? new ActorRuntimeDiagnosticsSnapshot([]));
    }
}
