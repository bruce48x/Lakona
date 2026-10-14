using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Testing.Fixtures.App;

[ComponentState]
public sealed class CompositionProbeState : IDisposable
{
    public string Origin { get; init; } = "generated";
    public int DisposeCount { get; set; }
    public int Starts { get; set; }
    public int Stops { get; set; }
    public int StateDisposals { get; private set; }
    public void Dispose() => StateDisposals++;
}

public sealed class CompositionRegistrationProbe(string nodeId) : IDisposable
{
    public string NodeId { get; } = nodeId;
    public int Disposals { get; private set; }
    public void Dispose() => Disposals++;
}
