using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Http;

/// <summary>
/// Generation-pinned context passed to a Hotfix Application HTTP handler.
/// </summary>
public readonly struct LakonaHttpCall : IHotfixServiceCall<LakonaHttpRequest>
{
    public LakonaHttpCall(
        LakonaHttpRequest request,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        CancellationToken = cancellationToken;
    }

    public LakonaHttpRequest Request { get; }

    public IServiceProvider Services { get; }

    public CancellationToken CancellationToken { get; }

    public IActorRuntime Actors => Services.GetRequiredService<IActorRuntime>();

    public ILakonaGameServer GameServer => Services.GetRequiredService<ILakonaGameServer>();
}
