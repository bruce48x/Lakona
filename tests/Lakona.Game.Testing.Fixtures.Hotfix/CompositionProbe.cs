using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Testing.Fixtures.App;

namespace Lakona.Game.Testing.Fixtures.Hotfix;

[HotfixComponent]
public sealed class CompositionProbe : IAsyncDisposable
{
    private readonly CompositionProbeState state;

    public CompositionProbe(CompositionProbeState state) => this.state = state;

    public string Origin => state.Origin;
    public int DisposeCount => state.DisposeCount;

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        state.DisposeCount++;
    }
}
