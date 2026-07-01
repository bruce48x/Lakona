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
        var loginRequest = new UserLoginRequest { Password = password, Reconnect = req.Reconnect };
        try
        {
            loginResult = await LoginUserAsync(account, loginRequest, call.Services, logger).ConfigureAwait(false);
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

    private async ValueTask<UserLoginResult> LoginUserAsync(
        string account,
        UserLoginRequest request,
        IServiceProvider services,
        ILogger logger)
    {
        var userId = new UserId(account);
        try
        {
            return await _users
                .Get(userId)
                .LoginAsync(request)
                .ConfigureAwait(false);
        }
        catch (ActorNotFoundException)
        {
            await EnsureUserActorAsync(account, services, logger).ConfigureAwait(false);
            return await _users
                .Get(userId)
                .LoginAsync(request)
                .ConfigureAwait(false);
        }
    }

    private static async ValueTask EnsureUserActorAsync(
        string account,
        IServiceProvider services,
        ILogger logger)
    {
        var actorId = ActorId.From(account);
        var owner = await SelectStateStoreOwnerAsync(account, services).ConfigureAwait(false);
        var ownerNode = owner.Node;
        var directory = services.GetRequiredService<IActorDirectory>();
        var directoryCache = services.GetRequiredService<IActorDirectoryCache>();

        var registerStatus = await directory.RegisterAsync(actorId, ownerNode).ConfigureAwait(false);
        var registeredHere = registerStatus == ActorDirectoryRegisterStatus.Registered;
        if (registerStatus == ActorDirectoryRegisterStatus.Conflict)
        {
            directoryCache.Remove(actorId);
            logger.LogDebug(
                "User actor {UserId} was created concurrently on another node; retrying through ActorDirectory.",
                account);
            return;
        }

        try
        {
            if (ownerNode == services.GetRequiredService<LocalActorNodeIdentity>().NodeId)
            {
                await EnsureLocalUserActorAsync(actorId, account, ownerNode, services, logger).ConfigureAwait(false);
            }
            else
            {
                await SendEnsureUserActorAsync(owner, account, services).ConfigureAwait(false);
                directoryCache.Set(actorId, ownerNode);
                logger.LogDebug(
                    "Requested user actor {UserId} creation on state-store node {NodeId}.",
                    account,
                    ownerNode.Value);
            }
        }
        catch
        {
            if (registeredHere)
            {
                await directory.UnregisterAsync(actorId, ownerNode).ConfigureAwait(false);
            }

            directoryCache.Remove(actorId);
            throw;
        }
    }

    private static async ValueTask<ClusterNodeDescriptor> SelectStateStoreOwnerAsync(
        string userId,
        IServiceProvider services)
    {
        var candidates = new List<ClusterNodeDescriptor>();
        if (services.GetService<IClusterNodeDiscovery>() is IClusterNodeDiscovery discovery)
        {
            var discovered = await discovery
                .ListAsync(new FeatureName(StateStoreUserActorPlacement.FeatureName))
                .ConfigureAwait(false);
            candidates.AddRange(discovered.Where(static node =>
                node.State == NodeState.Ready &&
                node.Features.Any(static feature => string.Equals(
                    feature.Name,
                    StateStoreUserActorPlacement.FeatureName,
                    StringComparison.OrdinalIgnoreCase))));
        }

        if (candidates.Count == 0 && LocalNodeCanOwnStateStore(services.GetRequiredService<LakonaGameRuntimeOptions>()))
        {
            var localNode = services.GetRequiredService<LocalActorNodeIdentity>().NodeId;
            candidates.Add(new ClusterNodeDescriptor(
                localNode,
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

    private static async ValueTask SendEnsureUserActorAsync(
        ClusterNodeDescriptor owner,
        string userId,
        IServiceProvider services)
    {
        var client = services.GetRequiredService<IFeatureCommandClient>();
        var reply = await client.SendToNodeAsync<EnsureUserActorRequest, EnsureActorReply>(
            owner,
            StateStoreUserActorPlacement.FeatureName,
            new EnsureUserActorRequest { UserId = userId }).ConfigureAwait(false);
        if (!reply.Succeeded)
        {
            throw new InvalidOperationException(
                $"State-store node {owner.Node.Value} rejected user actor creation for '{userId}'. {reply.Message}");
        }
    }

    private static async ValueTask EnsureLocalUserActorAsync(
        ActorId actorId,
        string account,
        NodeId localNode,
        IServiceProvider services,
        ILogger logger)
    {
        await services
            .GetRequiredService<ActorHosting>()
            .EnsureAsync<UserActor>(actorId)
            .ConfigureAwait(false);

        services.GetRequiredService<IActorDirectoryCache>().Set(actorId, localNode);
        logger.LogDebug("Created local user actor {UserId} on node {NodeId}.", account, localNode.Value);
    }
}
