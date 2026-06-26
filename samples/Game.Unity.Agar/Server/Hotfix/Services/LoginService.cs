using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
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
    private readonly UserActors _users;

    public LoginService(UserActors users)
    {
        _users = users;
    }

    public async ValueTask<LoginReply> LoginAsync(HotfixServiceCall<LoginRequest, IControlCallback> call)
    {
        var req = call.Request;
        var services = AgarServiceDependencies.From(call);
        var logger = services.CreateLogger<LoginService>();

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
            loginResult = await _users
                .Get(new UserId(account))
                .LoginAsync(new UserLoginRequest { Password = password, Reconnect = req.Reconnect })
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
            var registration = services.PlayerSessionRegistry.Get(loginResult.UserId);
            if (registration?.ControlSessionKey is not { } controlSession ||
                !string.Equals(registration.SessionToken, loginResult.SessionToken, StringComparison.Ordinal))
            {
                return new LoginReply
                {
                    Code = LoginResultCodes.ReconnectStateLost,
                    PlayerId = loginResult.UserId,
                    Account = account,
                    Message = "Server session state was lost. Start a new session instead of reconnecting."
                };
            }

            var resumeDecision = await call.GameServer
                .ResumeSessionAsync(
                    new GameSessionResumeRequest(controlSession, loginResult.SessionToken),
                    call.ConnectionId,
                    call.Callback)
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
            services.PlayerSessionRegistry.UpdateControlConnection(
                loginResult.UserId,
                loginResult.SessionToken,
                call.ConnectionId,
                sessionKey);
            await _users
                .Get(new UserId(loginResult.UserId))
                .ReconnectAsync(new PlayerSessionReconnectRequest
                    {
                        UserId = loginResult.UserId,
                        SessionToken = loginResult.SessionToken,
                        ConnectionId = call.ConnectionId,
                        ReconnectedAtUtc = DateTime.UtcNow,
                        ControlGateway = CloneGateway(services.RuntimeNodeIdentity.AdvertisedEndpoint)
                    })
                .ConfigureAwait(false);
        }
        else
        {
            sessionKey = await call.GameServer
                .StartSessionAsync(loginResult.UserId, call.ConnectionId, call.Callback)
                .ConfigureAwait(false);
            services.PlayerSessionRegistry.RegisterControl(
                loginResult.UserId,
                loginResult.SessionToken,
                call.ConnectionId,
                sessionKey);
            await _users
                .Get(new UserId(loginResult.UserId))
                .AttachAsync(new PlayerSessionAttachRequest
                    {
                        UserId = loginResult.UserId,
                        SessionToken = loginResult.SessionToken,
                        ConnectionId = call.ConnectionId,
                        AttachedAtUtc = DateTime.UtcNow,
                        ControlGateway = CloneGateway(services.RuntimeNodeIdentity.AdvertisedEndpoint)
                    })
                .ConfigureAwait(false);
        }

        await services.MatchmakingNotifier
            .ReplayPendingAsync(loginResult.UserId)
            .ConfigureAwait(false);

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
