using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
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
        var controlGateway = ResolveLocalControlGateway(call.Services);

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
            var snapshot = await _users
                .Get(new UserId(loginResult.UserId))
                .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(snapshot.ControlSessionId) ||
                snapshot.ControlSessionGeneration <= 0 ||
                !string.Equals(snapshot.SessionToken, loginResult.SessionToken, StringComparison.Ordinal))
            {
                return new LoginReply
                {
                    Code = LoginResultCodes.ReconnectStateLost,
                    PlayerId = loginResult.UserId,
                    Account = account,
                    Message = "Server session state was lost. Start a new session instead of reconnecting."
                };
            }

            var controlSession = new GameSessionKey(
                loginResult.UserId,
                snapshot.ControlSessionId,
                snapshot.ControlSessionGeneration);
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
            await _users
                .Get(new UserId(loginResult.UserId))
                .ReconnectAsync(new PlayerSessionReconnectRequest
                    {
                        UserId = loginResult.UserId,
                        SessionToken = loginResult.SessionToken,
                        ConnectionId = call.ConnectionId,
                        ControlSessionId = sessionKey.SessionId,
                        ControlSessionGeneration = sessionKey.Generation,
                        ReconnectedAtUtc = DateTime.UtcNow,
                        ControlGateway = CloneGateway(controlGateway)
                    })
                .ConfigureAwait(false);
        }
        else
        {
            sessionKey = await call.GameServer
                .StartSessionAsync(loginResult.UserId, call.ConnectionId, call.Callback)
                .ConfigureAwait(false);
            await _users
                .Get(new UserId(loginResult.UserId))
                .AttachAsync(new PlayerSessionAttachRequest
                    {
                        UserId = loginResult.UserId,
                        SessionToken = loginResult.SessionToken,
                        ConnectionId = call.ConnectionId,
                        ControlSessionId = sessionKey.SessionId,
                        ControlSessionGeneration = sessionKey.Generation,
                        AttachedAtUtc = DateTime.UtcNow,
                        ControlGateway = CloneGateway(controlGateway)
                    })
                .ConfigureAwait(false);
        }

        await services.MatchmakingNotifier
            .ReplayPendingAsync(sessionKey)
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

    private static GatewayEndpointDescriptor ResolveLocalControlGateway(IServiceProvider services)
    {
        var runtime = services.GetRequiredService<LakonaGameRuntimeOptions>();
        var node = services.GetRequiredService<LocalActorNodeIdentity>().NodeId.Value;
        var endpoint = runtime.Endpoints.FirstOrDefault(static endpoint =>
            endpoint.RpcServices.Any(service =>
                string.Equals(service, "login", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(service, "player", StringComparison.OrdinalIgnoreCase)));
        if (endpoint is null)
        {
            return new GatewayEndpointDescriptor { InstanceId = node };
        }

        var uri = new Uri(endpoint.ToAdvertisedEndpoint(), UriKind.Absolute);
        return new GatewayEndpointDescriptor
        {
            InstanceId = node,
            Transport = endpoint.Transport,
            Host = uri.Host,
            Port = uri.Port,
            Path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath
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
