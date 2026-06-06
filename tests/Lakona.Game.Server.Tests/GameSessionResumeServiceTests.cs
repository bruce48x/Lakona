using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionResumeServiceTests
{
    [Fact]
    public async Task TryResumeReturnsUnauthorizedWhenTokenValidatorRejects()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServerSessions();
        services.AddSingleton<IGameSessionTokenValidator>(
            new DelegateTokenValidator(_ => ValueTask.FromResult(SessionTokenValidationResult.Unauthorized("bad token"))));
        using var provider = services.BuildServiceProvider();
        var directory = provider.GetRequiredService<IGameSessionDirectory>();
        var resumeService = provider.GetRequiredService<IGameSessionResumeService>();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        var decision = await resumeService.TryResumeAsync(
            new GameSessionResumeRequest(session, "token"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SessionResumeStatus.Unauthorized, decision.Status);
    }

    [Fact]
    public async Task TryResumeReturnsRefreshRequiredWhenAuthoritativeProbeRequiresRefresh()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServerSessions();
        services.AddSingleton<IAuthoritativeSessionStateProbe>(
            new DelegateStateProbe(_ => ValueTask.FromResult(AuthoritativeSessionStateProbeResult.RefreshRequired("snapshot changed"))));
        using var provider = services.BuildServiceProvider();
        var directory = provider.GetRequiredService<IGameSessionDirectory>();
        var resumeService = provider.GetRequiredService<IGameSessionResumeService>();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        var decision = await resumeService.TryResumeAsync(
            new GameSessionResumeRequest(session),
            TestContext.Current.CancellationToken);

        Assert.Equal(SessionResumeStatus.StateRefreshRequired, decision.Status);
        Assert.Equal(session, decision.Session);
    }

    [Fact]
    public async Task TryResumeReturnsStateLostWhenAuthoritativeProbeIsMissing()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServerSessions();
        services.AddSingleton<IAuthoritativeSessionStateProbe>(
            new DelegateStateProbe(_ => ValueTask.FromResult(AuthoritativeSessionStateProbeResult.Missing("gone"))));
        using var provider = services.BuildServiceProvider();
        var directory = provider.GetRequiredService<IGameSessionDirectory>();
        var resumeService = provider.GetRequiredService<IGameSessionResumeService>();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        var decision = await resumeService.TryResumeAsync(
            new GameSessionResumeRequest(session),
            TestContext.Current.CancellationToken);

        Assert.Equal(SessionResumeStatus.StateLost, decision.Status);
    }

    private sealed class DelegateTokenValidator : IGameSessionTokenValidator
    {
        private readonly Func<GameSessionResumeRequest, ValueTask<SessionTokenValidationResult>> _validate;

        public DelegateTokenValidator(Func<GameSessionResumeRequest, ValueTask<SessionTokenValidationResult>> validate)
        {
            _validate = validate;
        }

        public ValueTask<SessionTokenValidationResult> ValidateAsync(
            GameSessionResumeRequest request,
            CancellationToken cancellationToken = default)
        {
            return _validate(request);
        }
    }

    private sealed class DelegateStateProbe : IAuthoritativeSessionStateProbe
    {
        private readonly Func<GameSessionKey, ValueTask<AuthoritativeSessionStateProbeResult>> _probe;

        public DelegateStateProbe(Func<GameSessionKey, ValueTask<AuthoritativeSessionStateProbeResult>> probe)
        {
            _probe = probe;
        }

        public ValueTask<AuthoritativeSessionStateProbeResult> ProbeAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
        {
            return _probe(session);
        }
    }
}
