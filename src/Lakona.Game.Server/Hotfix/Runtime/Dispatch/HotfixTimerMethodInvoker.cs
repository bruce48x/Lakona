using System.Reflection;
using Lakona.Game.Server.Hotfix.Timers;

namespace Lakona.Game.Server.Hotfix.Dispatch;

internal interface IHotfixTimerMethodInvoker
{
    ValueTask InvokeAsync(object callback, object tick, object? actor);
}

internal static class HotfixTimerMethodInvoker
{
    public static IHotfixTimerMethodInvoker Create(Type callbackType, Type argsType, MethodInfo method, Type actorType) =>
        (IHotfixTimerMethodInvoker)typeof(HotfixTimerMethodInvoker)
            .GetMethod(nameof(CreateActorCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(callbackType, actorType, argsType).Invoke(null, [method])!;

    private static IHotfixTimerMethodInvoker CreateActorCore<TCallback, TActor, TArgs>(MethodInfo method) =>
        new ActorInvoker<TCallback, TActor, TArgs>(
            (Func<TCallback, TActor, TimerTick<TArgs>, ValueTask>)method.CreateDelegate(
                typeof(Func<TCallback, TActor, TimerTick<TArgs>, ValueTask>)));

    private sealed class ActorInvoker<TCallback, TActor, TArgs>(
        Func<TCallback, TActor, TimerTick<TArgs>, ValueTask> invoker) : IHotfixTimerMethodInvoker
    {
        public ValueTask InvokeAsync(object callback, object tick, object? actor) =>
            invoker((TCallback)callback, (TActor)actor!, (TimerTick<TArgs>)tick);
    }
}
