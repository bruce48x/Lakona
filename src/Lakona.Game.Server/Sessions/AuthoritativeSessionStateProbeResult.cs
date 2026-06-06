namespace Lakona.Game.Server.Sessions;

public sealed record AuthoritativeSessionStateProbeResult(
    AuthoritativeSessionStateStatus Status,
    string? Reason = null)
{
    public static AuthoritativeSessionStateProbeResult Compatible()
    {
        return new AuthoritativeSessionStateProbeResult(AuthoritativeSessionStateStatus.Compatible);
    }

    public static AuthoritativeSessionStateProbeResult RefreshRequired(string? reason = null)
    {
        return new AuthoritativeSessionStateProbeResult(AuthoritativeSessionStateStatus.RefreshRequired, reason);
    }

    public static AuthoritativeSessionStateProbeResult Missing(string? reason = null)
    {
        return new AuthoritativeSessionStateProbeResult(AuthoritativeSessionStateStatus.Missing, reason);
    }
}

