namespace Lakona.Game.Server.Sessions;

public sealed record SessionTokenValidationResult(
    SessionTokenValidationStatus Status,
    string? Reason = null)
{
    public static SessionTokenValidationResult Valid()
    {
        return new SessionTokenValidationResult(SessionTokenValidationStatus.Valid);
    }

    public static SessionTokenValidationResult Unauthorized(string? reason = null)
    {
        return new SessionTokenValidationResult(SessionTokenValidationStatus.Unauthorized, reason);
    }
}

