using Lakona.Game.ProcessTests.App;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.ProcessTests.Hotfix;

[HotfixBehaviorOf(typeof(ProbeActor))]
public sealed partial class ProbeBehavior(LocalActorNodeIdentity node)
{
    [ActorStart]
    public ValueTask StartAsync(ProbeActor self, ActorStartCall call)
    {
        ProcessEvidence.Write("actor-start", Describe(self));
        return default;
    }

    public async ValueTask<ProbeReply> ExecuteAsync(ProbeActor self, ProbeRequest request)
    {
        var identity = Describe(self, request.RequestId);
        ProcessEvidence.Write("execute-enter", identity);
        if (request.Hold) await ProcessEvidence.WaitAsync("call", CancellationToken.None);
        self.Count++;
        // Keep concurrent submissions observable instead of relying only on replies.
        await Task.Delay(2);
        ProcessEvidence.Write("execute-exit", identity);
        return new ProbeReply
        {
            Node = node.NodeId.Value, Incarnation = node.Reference!.Incarnation.Value.ToString("N"),
            Activation = self.Context.ActivationId.ToString(), RequestId = request.RequestId, Count = self.Count
        };
    }

    [ActorStop]
    public async ValueTask StopAsync(ProbeActor self, ActorStopCall call)
    {
        ProcessEvidence.Write("stop-enter", Describe(self));
        // Deliberately let a replacement be created before the real catalog
        // releases this activation. The production cleanup path remains intact.
        if (File.Exists(Path.Combine(ProcessEvidence.Root, "hold-stop")))
            await ProcessEvidence.WaitAsync("stop", call.CancellationToken);
        ProcessEvidence.Write("stop-exit", Describe(self));
    }

    private object Describe(ProbeActor self, string? request = null) => new
    {
        Node = node.NodeId.Value, Incarnation = node.Reference!.Incarnation.Value.ToString("N"),
        Activation = self.Context.ActivationId.ToString(), RequestId = request
    };
}

[HotfixStartup]
public static class ProbeStartup
{
    [HotfixConfigureActors]
    public static void Configure(ActorHostBuilder actors) =>
        actors.RegisterPlacement<ProbeActor, ProbeId>(static context =>
            context.Candidates.OrderBy(candidate => candidate.NodeId, StringComparer.Ordinal).First());
}
