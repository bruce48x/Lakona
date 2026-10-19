using FrameworkBenchmark.Lakona.Contracts;
using FrameworkBenchmark.Lakona.Server;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace FrameworkBenchmark.Lakona.Hotfix;

[HotfixStartup]
public static class HotfixStartup
{
    [HotfixConfigureActors]
    public static void ConfigureActors(ActorHostBuilder actors) => actors.RegisterPlacement<BenchmarkActor, string>(context =>
        context.Candidates.Single(candidate => candidate.NodeId == BenchmarkRouting.Owner(context.Key)));
}
