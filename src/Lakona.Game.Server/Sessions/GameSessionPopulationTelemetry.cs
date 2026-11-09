using System.Diagnostics.Metrics;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionPopulationTelemetry : IHostedService, IDisposable
{
    private readonly IGameSessionRegistry sessions;
    private readonly TimeProvider timeProvider;
    private readonly Lock snapshotGate = new();
    private GameSessionDiagnosticsSnapshot? snapshot;
    private long snapshotTimestamp;
    private readonly Meter meter = new(
        LakonaGameServerTelemetry.SessionMeterName,
        typeof(GameSessionPopulationTelemetry).Assembly.GetName().Version?.ToString());

    public GameSessionPopulationTelemetry(IGameSessionRegistry sessions, TimeProvider timeProvider)
    {
        this.sessions = sessions;
        this.timeProvider = timeProvider;
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

    internal GameSessionDiagnosticsSnapshot GetSnapshot()
    {
        lock (snapshotGate)
        {
            if (snapshot is not null &&
                timeProvider.GetElapsedTime(snapshotTimestamp, timeProvider.GetTimestamp()) < TimeSpan.FromSeconds(1))
            {
                return snapshot;
            }

            // Publish only a successful refresh. A failed scan remains retryable;
            // a slow scan gets a full reuse window after completion.
            var refreshed = sessions.GetDiagnosticsSnapshot();
            var timestamp = timeProvider.GetTimestamp();
            snapshot = refreshed;
            snapshotTimestamp = timestamp;
            return refreshed;
        }
    }

    private void CreateGauge(
        string name,
        Func<GameSessionDiagnosticsSnapshot, int> select,
        string description)
    {
        meter.CreateObservableGauge(
            name,
            () => (long)select(GetSnapshot()),
            unit: "{session}",
            description: description);
    }
}
