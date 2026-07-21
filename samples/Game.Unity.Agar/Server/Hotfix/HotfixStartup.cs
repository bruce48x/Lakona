using Server.App.State.Contracts;
using Server.App.State.Contracts.Leaderboard;
using Server.App.State.Contracts.Matchmaking;
using Server.App.State.Contracts.Users;
using Server.App.State.Leaderboard;
using Server.App.State.Matchmaking;
using Server.App.State.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix;

[HotfixStartup]
public static class HotfixStartup
{
    [HotfixConfigureActors]
    public static void ConfigureActors(ActorHostBuilder actors)
    {
        actors.RegisterStartup<MatchmakingActor, MatchmakingQueueId>(static context =>
            SelectStartupStableHash(context.Candidates, context.Key.Value));
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
