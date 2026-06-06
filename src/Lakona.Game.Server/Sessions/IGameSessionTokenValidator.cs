namespace Lakona.Game.Server.Sessions;

public interface IGameSessionTokenValidator
{
    ValueTask<SessionTokenValidationResult> ValidateAsync(
        GameSessionResumeRequest request,
        CancellationToken cancellationToken = default);
}

