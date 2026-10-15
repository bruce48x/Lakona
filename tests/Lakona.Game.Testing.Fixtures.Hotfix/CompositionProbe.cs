using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Testing.Fixtures.App;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Testing.Fixtures.Hotfix;

[HotfixComponent]
public sealed class CompositionProbe : IAsyncDisposable
{
    [ComponentConfigureServices]
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton(_ => new CompositionRegistrationProbe(configuration["Lakona:Node:Id"]!));

    private readonly CompositionProbeState state;

    public CompositionProbe(CompositionProbeState state, Action<CompositionProbeState>? observe = null)
    {
        this.state = state;
        observe?.Invoke(state);
    }

    [ComponentStart]
    public ValueTask StartAsync(CancellationToken token) { state.Starts++; return default; }

    [ComponentStop]
    public ValueTask StopAsync(CancellationToken token) { state.LifecycleEvents.Add("component-stop"); state.Stops++; return default; }

    public void RecordActorStop() => state.LifecycleEvents.Add("actor-stop");

    public string Origin => state.Origin;
    public int DisposeCount => state.DisposeCount;

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        state.LifecycleEvents.Add("generation-dispose");
        state.DisposeCount++;
    }
}
