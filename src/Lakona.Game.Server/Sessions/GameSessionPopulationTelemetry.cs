using System.Diagnostics.Metrics;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionPopulationTelemetry : IHostedService, IDisposable
{
    private readonly IGameSessionRegistry sessions;
    private readonly Meter meter = new(
        LakonaGameServerTelemetry.SessionMeterName,
        typeof(GameSessionPopulationTelemetry).Assembly.GetName().Version?.ToString());

    public GameSessionPopulationTelemetry(IGameSessionRegistry sessions)
    {
        this.sessions = sessions;
        CreateGauge("lakona.game.session.total", static snapshot => snapshot.TotalSessions, "Sessions retained by the local registry.");
        CreateGauge("lakona.game.session.active", static snapshot => snapshot.ActiveSessions, "Active local game sessions.");
        CreateGauge("lakona.game.session.connection.active", static snapshot => snapshot.ActiveConnections, "Active connections attached to local sessions.");
        CreateGauge("lakona.game.session.disconnected", static snapshot => snapshot.DisconnectedSessions, "Disconnected local game sessions.");
        CreateGauge("lakona.game.session.terminated", static snapshot => snapshot.TerminatedSessions, "Terminated sessions retained by the local registry.");
        CreateGauge("lakona.game.session.resumable", static snapshot => snapshot.ResumableSessions, "Local game sessions eligible for resume.");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => meter.Dispose();

    private void CreateGauge(
        string name,
        Func<GameSessionDiagnosticsSnapshot, int> select,
        string description)
    {
        meter.CreateObservableGauge(
            name,
            () => (long)select(sessions.GetDiagnosticsSnapshot()),
            unit: "{session}",
            description: description);
    }
}
