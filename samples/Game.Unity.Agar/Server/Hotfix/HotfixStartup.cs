using Server.App.Routing;
using Server.App.Leaderboard;
using Server.App.Matchmaking;
using Server.App.Users;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Rpc.Server;
using Microsoft.Extensions.DependencyInjection;
using Shared.Interfaces;

namespace Server.Hotfix;

[HotfixStartup]
public static class HotfixStartup
{
    [HotfixConfigureServices]
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<RpcServiceExceptionHandler>(MapServiceException);
    }

    private static object? MapServiceException(RpcServiceExceptionContext context)
    {
        const int code = AgarResultCodes.ServerInternalError;
        const string message = "Internal server error.";
        if (context.ResponseType == typeof(LoginReply))
            return new LoginReply { Code = code, Message = message };
        if (context.ResponseType == typeof(RealtimeAttachReply))
            return new RealtimeAttachReply { Code = code, Message = message };
        if (context.ResponseType == typeof(LeaderboardReply))
            return new LeaderboardReply { Code = code, Message = message };
        return null;
    }

    [HotfixConfigureActors]
    public static void ConfigureActors(ActorHostBuilder actors)
    {
        // 默认算法
        actors.RegisterStartup<MatchmakingActor, MatchmakingQueueId>();
        // 自定义算法
        actors.RegisterStartup<LeaderboardActor, LeaderboardId>(static context =>
            SelectStartupStableHash(context.Candidates, context.Key.Value));
        actors.RegisterPlacement<UserActor, UserId>(static context =>
            SelectStableHash(context.Candidates, context.Key.Value));
    }

    private static ActorHostCandidate SelectStableHash(
        IReadOnlyList<ActorHostCandidate> candidates,
        string key)
    {
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("No actor host candidates are available.");
        }

        return candidates[(int)(ComputeStableHash(key) % (uint)candidates.Count)];
    }

    private static StartupActorCandidate SelectStartupStableHash(
        IReadOnlyList<StartupActorCandidate> candidates,
        string key)
    {
        if (candidates.Count == 0) throw new InvalidOperationException("No startup actor candidates are available.");
        return candidates[(int)(ComputeStableHash(key) % (uint)candidates.Count)];
    }

    private static uint ComputeStableHash(string value)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            var hash = offsetBasis;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= prime;
            }

            return hash;
        }
    }
}
