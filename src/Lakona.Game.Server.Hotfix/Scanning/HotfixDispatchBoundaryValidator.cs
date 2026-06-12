using System.Reflection;
using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;

namespace Lakona.Game.Server.Hotfix.Scanning;

internal static class HotfixDispatchBoundaryValidator
{
    public static IReadOnlyList<string> Validate(
        HotfixAssemblyLoadContext hotfixContext,
        IEnumerable<HotfixMethodBinding> methods,
        IEnumerable<HotfixServiceMethodBinding> services)
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

        return diagnostics;
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
