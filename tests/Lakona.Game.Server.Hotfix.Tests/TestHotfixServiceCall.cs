using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

public class HotfixServiceCall<TRequest> : IHotfixCallContext
{
    public HotfixServiceCall(TRequest? request, IServiceProvider services)
    {
        Request = request;
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public TRequest? Request { get; }

    public IServiceProvider Services { get; }

    public Lakona.Game.Server.Sessions.GameSessionItems CurrentSessionItems { get; } =
        Lakona.Game.Server.Sessions.GameSessionItems.Empty;
}

public sealed class HotfixServiceCall<TRequest, TCallback> : HotfixServiceCall<TRequest>
    where TCallback : class
{
    public HotfixServiceCall(TRequest? request, TCallback? callback, IServiceProvider services)
        : base(request, services)
    {
        Callback = callback;
    }

    public TCallback? Callback { get; }
}

public sealed class HotfixLifecycleCall<TRequest> : HotfixServiceCall<TRequest>
{
    public HotfixLifecycleCall(TRequest? request, IServiceProvider services)
        : base(request, services)
    {
    }
}
