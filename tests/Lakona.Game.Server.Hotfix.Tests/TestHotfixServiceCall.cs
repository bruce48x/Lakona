namespace Lakona.Game.Server.Hotfix;

public class HotfixServiceCall<TRequest>
{
    public TRequest? Request { get; }
}

public sealed class HotfixServiceCall<TRequest, TCallback> : HotfixServiceCall<TRequest>
    where TCallback : class
{
    public TCallback? Callback { get; }
}
