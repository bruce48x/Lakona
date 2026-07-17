using Server.App.State.Contracts;
using Server.App.State.Contracts.Users;
using Server.App.State.Users;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;
using Server.App.Generated;
using Server.Hotfix.State.Users;
using Shared.Interfaces;
using System.Security.Cryptography;

namespace Server.Hotfix.Services;

[HotfixService(typeof(ILoginService))]
public sealed class LoginService
{
    private readonly ActorAccess _actors;
    private readonly ILogger<LoginService> _logger;

    public LoginService(
        ActorAccess actors,
        ILogger<LoginService> logger)
    {
        _actors = actors;
        _logger = logger;
    }

    public async ValueTask<LoginReply> LoginAsync(LoginServiceCall<LoginRequest> call)
    {
        var req = call.Request;
        var account = req.Account;
        var password = req.Password;
        if (req.GuestLogin)
        {
            account = CreateGuestAccount();
            password = CreateGuestPassword();
        }

        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
        {
            return new LoginReply { Code = LoginResultCodes.InvalidRequest, Message = "Login request is incomplete." };
        }

        var sessionKey = await call.GameServer
            .StartSessionAsync(account, call.ConnectionId)
            .ConfigureAwait(false);
        UserLoginResult loginResult;
        try
        {
            loginResult = await LoginUserAsync(
                    account,
                    new UserLoginAndAttachRequest
                    {
                        Password = password,
                        ConnectionId = call.ConnectionId,
                        ControlSessionId = sessionKey.SessionId,
                        ControlSessionGeneration = sessionKey.Generation
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            await RollbackLoginSessionAsync(call, sessionKey).ConfigureAwait(false);
            throw;
        }

        return new LoginReply
        {
            Code = LoginResultCodes.Ok,
            Token = loginResult.SessionToken,
            PlayerId = loginResult.UserId,
            WinCount = loginResult.WinCount,
            VictoryPoints = loginResult.VictoryPoints,
            Account = account,
            Password = req.GuestLogin ? password : string.Empty
        };
    }

    private static string CreateGuestAccount()
    {
        return $"guest-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}";
    }

    private static string CreateGuestPassword()
    {
        return RandomNumberGenerator.GetHexString(16).ToLowerInvariant();
    }

    private async ValueTask<UserLoginResult> LoginUserAsync(
        string account,
        UserLoginAndAttachRequest request,
        CancellationToken cancellationToken)
    {
        var userId = new UserId(account);
        var result = await _actors
            .Place<UserActor>(userId)
            .EnsureAsync(cancellationToken)
            .ConfigureAwait(false);
        _logger.LogDebug(
            "Ensured user actor {UserId} on node {NodeId}.",
            account,
            result.Owner.Value);
        return await _actors
            .Route<UserActor>(userId)
            .CallAsync(static behavior => behavior.LoginAndAttachAsync, request, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask RollbackLoginSessionAsync(
        LoginServiceCall<LoginRequest> call,
        GameSessionKey sessionKey)
    {
        try
        {
            await call.GameServer
                .TerminateSessionAsync(
                    sessionKey,
                    SessionTerminationReason.Application,
                    "Login did not complete.",
                    new SessionTerminationOptions
                    {
                        NotifyTimeout = TimeSpan.Zero,
                        KeepTerminalStateForResume = false
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to roll back login session {SessionId}.", sessionKey.SessionId);
        }
    }
}
