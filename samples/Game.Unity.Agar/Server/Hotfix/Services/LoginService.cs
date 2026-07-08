using Server.App.State.Contracts;
using Server.App.State.Contracts.Sessions;
using Server.App.State.Contracts.Users;
using Server.App.State.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.State.Users;
using Shared.Interfaces;
using System.Security.Cryptography;

namespace Server.Hotfix.Services;

[HotfixService(typeof(ILoginService))]
public sealed class LoginService
{
    private readonly UserActors _users;
    private readonly LakonaGameRuntimeOptions _runtime;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ILogger<LoginService> _logger;

    public LoginService(
        UserActors users,
        LakonaGameRuntimeOptions runtime,
        LocalActorNodeIdentity localNode,
        ILogger<LoginService> logger)
    {
        _users = users;
        _runtime = runtime;
        _localNode = localNode;
        _logger = logger;
    }

    public async ValueTask<LoginReply> LoginAsync(HotfixServiceCall<LoginRequest, ILoginCallback> call)
    {
        var req = call.Request;
        var controlGateway = ResolveLocalControlGateway();

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

        var loginRequest = new UserLoginRequest { Password = password, Reconnect = req.Reconnect };
        var loginResult = await LoginUserAsync(call.Services, account, loginRequest, CancellationToken.None).ConfigureAwait(false);

        GameSessionKey sessionKey;
        if (req.Reconnect)
        {
            var snapshot = await _users
                .Route(new UserId(loginResult.UserId))
                .CallAsync(
                    UserBehavior.GetSnapshotAsync,
                    new PlayerSessionSnapshotRequest(),
                    CancellationToken.None)
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
                .Route(new UserId(loginResult.UserId))
                .CallAsync(
                    UserBehavior.ReconnectAsync,
                    new PlayerSessionReconnectRequest
                    {
                        UserId = loginResult.UserId,
                        SessionToken = loginResult.SessionToken,
                        ConnectionId = call.ConnectionId,
                        ControlSessionId = sessionKey.SessionId,
                        ControlSessionGeneration = sessionKey.Generation,
                        ReconnectedAtUtc = DateTime.UtcNow,
                        ControlGateway = CloneGateway(controlGateway)
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        else
        {
            sessionKey = await call.GameServer
                .StartSessionAsync(loginResult.UserId, call.ConnectionId, call.Callback)
                .ConfigureAwait(false);
            await _users
                .Route(new UserId(loginResult.UserId))
                .CallAsync(
                    UserBehavior.AttachAsync,
                    new PlayerSessionAttachRequest
                    {
                        UserId = loginResult.UserId,
                        SessionToken = loginResult.SessionToken,
                        ConnectionId = call.ConnectionId,
                        ControlSessionId = sessionKey.SessionId,
                        ControlSessionGeneration = sessionKey.Generation,
                        AttachedAtUtc = DateTime.UtcNow,
                        ControlGateway = CloneGateway(controlGateway)
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
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

    private GatewayEndpointDescriptor ResolveLocalControlGateway()
    {
        var node = _localNode.NodeId.Value;
        var endpoint = _runtime.Endpoints.FirstOrDefault(static endpoint =>
            endpoint.RpcServices.Any(service =>
                string.Equals(service, "login", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(service, "player", StringComparison.OrdinalIgnoreCase)));
        if (endpoint is null)
        {
            return new GatewayEndpointDescriptor { InstanceId = node };
        }

        var uri = new System.Uri(endpoint.ToAdvertisedEndpoint(), System.UriKind.Absolute);
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
        return $"guest-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}";
    }

    private static string CreateGuestPassword()
    {
        return RandomNumberGenerator.GetHexString(16).ToLowerInvariant();
    }

    private async ValueTask<UserLoginResult> LoginUserAsync(
        IServiceProvider services,
        string account,
        UserLoginRequest request,
        CancellationToken cancellationToken)
    {
        var userId = new UserId(account);
        await CreateUserActorOnStateStoreAsync(services, account).ConfigureAwait(false);
        return await _users
            .Route(userId)
            .CallAsync(UserBehavior.LoginAsync, request, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask CreateUserActorOnStateStoreAsync(
        IServiceProvider services,
        string account)
    {
        _ = services;
        var result = await _users
            .Place(new UserId(account))
            .EnsureAsync()
            .ConfigureAwait(false);
        _logger.LogDebug(
            "Ensured user actor {UserId} on node {NodeId}.",
            account,
            result.Owner.Value);
    }
}
