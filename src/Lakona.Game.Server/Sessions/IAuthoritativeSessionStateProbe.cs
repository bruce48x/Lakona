using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public interface IAuthoritativeSessionStateProbe
{
    ValueTask<AuthoritativeSessionStateProbeResult> ProbeAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);
}
