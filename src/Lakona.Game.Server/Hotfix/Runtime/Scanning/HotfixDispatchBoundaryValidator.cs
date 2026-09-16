using System.Reflection;
using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Timers;

namespace Lakona.Game.Server.Hotfix.Scanning;

internal static class HotfixDispatchBoundaryValidator
{
    public static IReadOnlyList<string> Validate(
        HotfixAssemblyLoadContext hotfixContext,
        IEnumerable<HotfixMethodBinding> methods,
        IEnumerable<HotfixServiceMethodBinding> services,
        IEnumerable<HotfixTimerMethodDescriptor> timers)
    {
        var diagnostics = new List<string>();
        foreach (var binding in methods)
        {
            ValidateType(hotfixContext, binding.StateType, binding.Key.ToString(), diagnostics);
            ValidateType(hotfixContext, binding.ReturnType, binding.Key.ToString(), diagnostics);
            foreach (var parameterType in binding.ParameterTypes)
            {
                ValidateType(hotfixContext, parameterType, binding.Key.ToString(), diagnostics);
            }
        }

        foreach (var binding in services)
        {
            ValidateType(hotfixContext, binding.ContractType, binding.Key, diagnostics);
            ValidateType(hotfixContext, binding.ReturnType, binding.Key, diagnostics);
            foreach (var parameterType in binding.ParameterTypes)
            {
                ValidateType(hotfixContext, parameterType, binding.Key, diagnostics);
            }
        }

        foreach (var timer in timers)
        {
            try
            {
                ValidateStableTimerType(hotfixContext, timer.ArgsType, new HashSet<Type>());
                LakonaTimerArgsSerializer.ValidateArgsType(timer.ArgsType);
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
            {
                diagnostics.Add($"Actor timer '{timer.CallbackType.FullName}.{timer.MethodName}' has invalid args '{timer.ArgsType.FullName}': {exception.Message}");
            }
        }

        return diagnostics;
    }

    private static void ValidateStableTimerType(AssemblyLoadContext context, Type type, HashSet<Type> visited, int depth = 0)
    {
        if (!visited.Add(type)) return;
        if (depth > 32) throw new NotSupportedException("Timer args declared type nesting exceeds 32 levels; simplify the DTO.");
        if (ReferenceEquals(AssemblyLoadContext.GetLoadContext(type.Assembly), context))
            throw new InvalidOperationException($"Timer args type '{type.FullName}' belongs to the Hotfix load context. Move timer DTOs to Server.App or another shared stable assembly.");
        if (type.HasElementType)
        {
            ValidateStableTimerType(context, type.GetElementType()!, visited, depth + 1);
            return;
        }
        foreach (var argument in type.GenericTypeArguments)
            ValidateStableTimerType(context, argument, visited, depth + 1);
        // Framework containers are checked through their type arguments; DTO state is public properties.
        if (type.Namespace is { } ns && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal))) return;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0))
            ValidateStableTimerType(context, property.PropertyType, visited, depth + 1);
    }

    private static void ValidateType(
        AssemblyLoadContext hotfixContext,
        Type type,
        string methodKey,
        List<string> diagnostics)
    {
        if (type == typeof(void) || type.Assembly == typeof(object).Assembly)
        {
            return;
        }

        var context = AssemblyLoadContext.GetLoadContext(type.Assembly);
        if (ReferenceEquals(context, hotfixContext))
        {
            diagnostics.Add(
                $"Hotfix method '{methodKey}' uses boundary type '{type.FullName}' from the hotfix AssemblyLoadContext. The type must resolve from a shared AssemblyLoadContext.");
        }
    }
}
