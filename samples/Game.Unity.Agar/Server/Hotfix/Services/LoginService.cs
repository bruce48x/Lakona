using Server.App.State.Contracts;
using Server.App.State.Contracts.Users;
using Server.App.State.Users;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
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

        var sessionKey = await call.GameServer
            .StartSessionAsync(account, call.ConnectionId)
            .ConfigureAwait(false);
        UserLoginResult loginResult;
        try
        {
            loginResult = await LoginUserAsync(
                    call.Services,
                    account,
                    new UserLoginAndAttachRequest
                    {
                        Password = password,
                        ConnectionId = call.ConnectionId,
                        ControlSessionId = sessionKey.SessionId,
                        ControlSessionGeneration = sessionKey.Generation,
                        AttachedAtUtc = DateTime.UtcNow,
                        ControlGateway = CloneGateway(controlGateway)
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
        UserLoginAndAttachRequest request,
        CancellationToken cancellationToken)
    {
        var userId = new UserId(account);
        await CreateUserActorOnStateStoreAsync(services, account).ConfigureAwait(false);
        return await _users
            .Route(userId)
            .CallAsync(UserBehavior.LoginAndAttachAsync, request, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask RollbackLoginSessionAsync(
        HotfixServiceCall<LoginRequest, ILoginCallback> call,
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
