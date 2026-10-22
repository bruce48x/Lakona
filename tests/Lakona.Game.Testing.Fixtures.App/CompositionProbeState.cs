using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Testing.Fixtures.App;

[ComponentState]
public sealed class CompositionProbeState : IDisposable
{
    public int TimerTicks;
    public TaskCompletionSource FirstTimerTick { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Lakona.Game.Cluster.ClusterMembershipSnapshot? StopMembership { get; set; }
    public Lakona.Game.Cluster.Membership.MembershipTableSnapshot? StopTable { get; set; }
    public string Origin { get; init; } = "generated";
    public int DisposeCount { get; set; }
    public int Starts { get; set; }
    public int Stops { get; set; }
    public int StateDisposals { get; private set; }
    public List<string> LifecycleEvents { get; } = [];
    public void Dispose() { LifecycleEvents.Add("state-dispose"); StateDisposals++; }
}

public sealed class CompositionRegistrationProbe(string nodeId) : IDisposable
{
    public string NodeId { get; } = nodeId;
    public int Disposals { get; private set; }
    public void Dispose() => Disposals++;
}
