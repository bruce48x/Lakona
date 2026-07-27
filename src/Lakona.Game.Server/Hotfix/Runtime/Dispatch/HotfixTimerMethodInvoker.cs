using System.Reflection;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;

namespace Lakona.Game.Server.Hotfix.Dispatch;

internal interface IHotfixTimerMethodInvoker
{
    ValueTask InvokeAsync(object callback, object tick);
}

internal static class HotfixTimerMethodInvoker
{
    private static readonly MethodInfo CreateMethod = typeof(HotfixTimerMethodInvoker)
        .GetMethod(nameof(CreateCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static IHotfixTimerMethodInvoker Create(Type callbackType, Type argsType, MethodInfo method)
    {
        return (IHotfixTimerMethodInvoker)CreateMethod
            .MakeGenericMethod(callbackType, argsType)
            .Invoke(null, [method])!;
    }

    private static IHotfixTimerMethodInvoker CreateCore<TCallback, TArgs>(MethodInfo method)
    {
        return new Invoker<TCallback, TArgs>(
            (Func<TCallback, TimerTick<TArgs>, ValueTask>)method.CreateDelegate(
                typeof(Func<TCallback, TimerTick<TArgs>, ValueTask>)));
    }

    private sealed class Invoker<TCallback, TArgs>(
        Func<TCallback, TimerTick<TArgs>, ValueTask> invoker) : IHotfixTimerMethodInvoker
    {
        public ValueTask InvokeAsync(object callback, object tick)
        {
            return invoker((TCallback)callback, (TimerTick<TArgs>)tick);
        }
    }
}
