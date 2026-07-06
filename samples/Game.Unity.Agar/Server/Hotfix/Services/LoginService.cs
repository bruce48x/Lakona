using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Users;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.State.Users;
using Shared.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace Server.Hotfix.Services;

[HotfixService(typeof(ILoginService))]
public sealed class LoginService
{
    private readonly ActorHosting _actorHosting;
    private readonly IClusterNodeDiscovery _clusterDiscovery;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly UserActors _users;
    private readonly ILogger<LoginService> _logger;
    private readonly LakonaGameRuntimeOptions _runtime;

    public LoginService(
        UserActors users,
        LakonaGameRuntimeOptions runtime,
        LocalActorNodeIdentity localNode,
        ActorHosting actorHosting,
        IClusterNodeDiscovery clusterDiscovery,
        ILogger<LoginService> logger)
    {
        _users = users;
        _runtime = runtime;
        _localNode = localNode;
        _actorHosting = actorHosting;
        _clusterDiscovery = clusterDiscovery;
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
        var loginResult = await LoginUserAsync(call.Services, account, loginRequest).ConfigureAwait(false);

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

        await HotfixNotificationServices
            .GetMatchmakingNotifier(call.Services)
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
        return $"guest-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}";
    }

    private static string CreateGuestPassword()
    {
        return RandomNumberGenerator.GetHexString(16).ToLowerInvariant();
    }

    private async ValueTask<UserLoginResult> LoginUserAsync(
        IServiceProvider services,
        string account,
        UserLoginRequest request)
    {
        var userId = new UserId(account);
        await CreateUserActorOnStateStoreAsync(services, account).ConfigureAwait(false);
        return await _users
            .Get(userId)
            .LoginAsync(request)
            .ConfigureAwait(false);
    }

    private async ValueTask CreateUserActorOnStateStoreAsync(
        IServiceProvider services,
        string account)
    {
        var owner = await SelectStateStoreOwnerAsync(account).ConfigureAwait(false);
        var ownerNode = owner.Node;

        await SendCreateUserActorAsync(services, owner, account).ConfigureAwait(false);
        _logger.LogDebug(
            "Requested user actor {UserId} creation on state-store node {NodeId}.",
            account,
            ownerNode.Value);
    }

    private async ValueTask<ClusterNodeDescriptor> SelectStateStoreOwnerAsync(string userId)
    {
        var candidates = new List<ClusterNodeDescriptor>();
        var discovered = await _clusterDiscovery
            .ListAsync(new FeatureName(StateStoreUserActorPlacement.FeatureName))
            .ConfigureAwait(false);
        candidates.AddRange(discovered.Where(static node =>
            node.State == NodeState.Ready &&
            node.Features.Any(static feature => string.Equals(
                feature.Name,
                StateStoreUserActorPlacement.FeatureName,
                StringComparison.OrdinalIgnoreCase))));

        if (candidates.Count == 0 && LocalNodeCanOwnStateStore(_runtime))
        {
            candidates.Add(new ClusterNodeDescriptor(
                _localNode.NodeId,
                NodeState.Ready,
                new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal),
                [new NodeFeatureDescriptor(StateStoreUserActorPlacement.FeatureName)]));
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("No ready state-store node is available for user actor placement.");
        }

        var ordered = candidates
            .OrderBy(static node => node.Node.Value, StringComparer.Ordinal)
            .ToArray();
        return ordered[SelectOwnerIndex(userId, ordered.Length)];
    }

    private static bool LocalNodeCanOwnStateStore(LakonaGameRuntimeOptions runtime)
    {
        return runtime.Feature is null ||
            runtime.Feature.Any(static feature => string.Equals(
                feature,
                StateStoreUserActorPlacement.FeatureName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static int SelectOwnerIndex(string userId, int count)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userId));
        var value = 0UL;
        for (var index = 0; index < sizeof(ulong); index++)
        {
            value = (value << 8) | hash[index];
        }

        return (int)(value % (ulong)count);
    }

    private async ValueTask SendCreateUserActorAsync(
        IServiceProvider services,
        ClusterNodeDescriptor owner,
        string userId)
    {
        var featureCommands = services.GetRequiredService<IFeatureCommandClient>();
        var reply = await featureCommands
            .SendToNodeAsync<CreateUserActorRequest, CreateActorReply>(
                owner,
                StateStoreUserActorPlacement.FeatureName,
                new CreateUserActorRequest { UserId = userId })
            .ConfigureAwait(false);
        if (!reply.Succeeded)
        {
            throw new InvalidOperationException(
                $"State-store node {owner.Node.Value} rejected user actor creation for '{userId}'. {reply.Message}");
        }
    }
}
