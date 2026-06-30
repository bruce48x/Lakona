namespace Lakona.Game.Server.Sessions;

public sealed record GameSessionDiagnosticsSnapshot(
    int TotalSessions,
    int ActiveSessions,
    int ActiveConnections,
    int DisconnectedSessions,
    int TerminatedSessions,
    int ResumableSessions);
