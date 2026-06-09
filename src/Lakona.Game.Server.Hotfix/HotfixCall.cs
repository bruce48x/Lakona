using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix;

/// <summary>
/// Type-safe wrapper around <see cref="HotfixDispatch"/>.
/// Generates compile-time checked generic dispatch calls so callers only
/// supply the method name as a string — parameter and return types are
/// verified by the compiler.
/// </summary>
/// <typeparam name="TState">The <c>[HotfixState]</c> type the hotfix methods operate on.</typeparam>
public static class HotfixCall<TState>
{
    /// <summary>
    /// Invoke a hotfix method with one argument and a return value.
    /// </summary>
    public static TResult WithArg<TArg, TResult>(string methodName, TState state, TArg arg)
    {
        ArgumentNullException.ThrowIfNull(state);
        return HotfixDispatch.Invoke<TState, TArg, TResult>(methodName, state, arg);
    }

    /// <summary>
    /// Invoke a hotfix method with no arguments and a return value.
    /// </summary>
    public static TResult WithoutArg<TResult>(string methodName, TState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return HotfixDispatch.Invoke<TState, TResult>(methodName, state);
    }
}
