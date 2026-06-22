using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Sessions;
using Agar.Sample.State.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;
using Server.Hotfix.State.Sessions;
using Server.Hotfix.State.Users;
using Shared.Interfaces;
using System.Security.Cryptography;

namespace Server.Hotfix.Services;

[HotfixService(typeof(ILoginService))]
public sealed class LoginService
{
    public async ValueTask<LoginReply> LoginAsync(HotfixServiceCall<LoginRequest> call)
    {
        var req = call.Request;
        var services = AgarServiceDependencies.From(call);
        var logger = services.CreateLogger<LoginService>();
        var localActors = call.Actors;

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

        UserLoginResult loginResult;
        try
        {
            loginResult = await localActors
                .AskAsync<UserActor, UserLoginResult>(
                    UserId(account),
                    (actor, _) => actor.LoginAsync(password, req.Reconnect))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Login rejected for account {Account}.", account);
            return new LoginReply { Code = LoginResultCodes.Rejected, Message = "Login rejected." };
        }

        GameSessionKey sessionKey;
        if (req.Reconnect)
        {
            var resumeDecision = await services.SessionDirectory
                .ResumeControlAsync(loginResult.UserId, loginResult.SessionToken, call.ConnectionId)
                .ConfigureAwait(false);
            if (resumeDecision.Status != SessionResumeStatus.Resumed || resumeDecision.Session is null)
            {
                return new LoginReply
                {
                    Code = LoginResultCodes.ReconnectStateLost,
                    PlayerId = loginResult.UserId,
                    Account = account,
                    Message = string.IsNullOrWhiteSpace(resumeDecision.Reason)
                        ? "Server session state was lost. Start a new session instead of reconnecting."
                        : resumeDecision.Reason
                };
            }

            sessionKey = resumeDecision.Session.Value;
            await localActors
                .AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
                    SessionId(loginResult.UserId),
                    (actor, _) => actor.ReconnectAsync(new PlayerSessionReconnectRequest
                    {
                        UserId = loginResult.UserId,
                        SessionToken = loginResult.SessionToken,
                        ConnectionId = call.ConnectionId,
                        ReconnectedAtUtc = DateTime.UtcNow,
                        ControlGateway = CloneGateway(services.GatewayNodeIdentity.AdvertisedEndpoint)
                    }))
                .ConfigureAwait(false);
        }
        else
        {
            sessionKey = await services.SessionDirectory
                .RegisterNewControlAsync(loginResult.UserId, loginResult.SessionToken, call.ConnectionId)
                .ConfigureAwait(false);
            await localActors
                .AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
                    SessionId(loginResult.UserId),
                    (actor, _) => actor.AttachAsync(new PlayerSessionAttachRequest
                    {
                        UserId = loginResult.UserId,
                        SessionToken = loginResult.SessionToken,
                        ConnectionId = call.ConnectionId,
                        AttachedAtUtc = DateTime.UtcNow,
                        ControlGateway = CloneGateway(services.GatewayNodeIdentity.AdvertisedEndpoint)
                    }))
                .ConfigureAwait(false);
            await services.ReliablePushOutbox.AckAsync(loginResult.UserId, long.MaxValue).ConfigureAwait(false);
        }

        return new LoginReply
        {
            Code = LoginResultCodes.Ok,
            Token = loginResult.SessionToken,
            PlayerId = loginResult.UserId,
            WinCount = loginResult.WinCount,
            VictoryPoints = loginResult.VictoryPoints,
            Account = account,
            Password = req.GuestLogin ? password : string.Empty,
            SessionId = sessionKey.SessionId,
            SessionGeneration = sessionKey.Generation
        };
    }

    private static ActorId UserId(string userId) => ActorId.From(userId);

    private static ActorId SessionId(string userId) => ActorId.From($"session:{userId}");

    private static GatewayEndpointDescriptor CloneGateway(GatewayEndpointDescriptor gateway)
    {
        return new GatewayEndpointDescriptor
        {
            InstanceId = gateway.InstanceId,
            Transport = gateway.Transport,
            Host = gateway.Host,
            Port = gateway.Port,
            Path = gateway.Path
        };
    }

    private static string CreateGuestAccount()
    {
        return $"guest-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(6).ToLowerInvariant()}";
    }

    private static string CreateGuestPassword()
    {
        return RandomNumberGenerator.GetHexString(16).ToLowerInvariant();
    }
}
