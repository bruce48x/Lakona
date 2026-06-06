using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionResumeService : IGameSessionResumeService
{
    private readonly IGameSessionDirectory _directory;
    private readonly IServiceProvider _services;

    public GameSessionResumeService(
        IGameSessionDirectory directory,
        IServiceProvider services)
    {
        _directory = directory;
        _services = services;
    }

    public async ValueTask<SessionResumeDecision> TryResumeAsync(
        GameSessionResumeRequest request,
        CancellationToken cancellationToken = default)
    {
        var directoryDecision = await _directory
            .TryResumeAsync(request.Session, cancellationToken)
            .ConfigureAwait(false);

        if (directoryDecision.Status != SessionResumeStatus.Resumed)
        {
            return directoryDecision;
        }

        var tokenValidator = _services.GetService<IGameSessionTokenValidator>();
        if (tokenValidator is not null)
        {
            var tokenResult = await tokenValidator
                .ValidateAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (tokenResult.Status == SessionTokenValidationStatus.Unauthorized)
            {
                return SessionResumeDecision.Unauthorized(tokenResult.Reason);
            }
        }

        var stateProbe = _services.GetService<IAuthoritativeSessionStateProbe>();
        if (stateProbe is null)
        {
            return directoryDecision;
        }

        var stateResult = await stateProbe
            .ProbeAsync(request.Session, cancellationToken)
            .ConfigureAwait(false);

        return stateResult.Status switch
        {
            AuthoritativeSessionStateStatus.Compatible => directoryDecision,
            AuthoritativeSessionStateStatus.RefreshRequired => SessionResumeDecision.StateRefreshRequired(
                request.Session,
                stateResult.Reason),
            AuthoritativeSessionStateStatus.Missing => SessionResumeDecision.StateLost(stateResult.Reason),
            _ => directoryDecision
        };
    }
}
