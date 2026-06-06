using System.Reflection;
using System.Runtime.CompilerServices;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix.Scanning;

public static class HotfixSystemScanner
{
    public static HotfixSystemScanResult Scan(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var methods = new List<HotfixMethodBinding>();
        var diagnostics = new List<string>();
        var keys = new HashSet<HotfixMethodKey>();

        foreach (var assembly in assemblies)
        {
            Type[] assemblyTypes;
            try
            {
                assemblyTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                diagnostics.Add($"Could not load all types from hotfix assembly '{assembly.FullName}': {exception.Message}");
                foreach (var loaderException in exception.LoaderExceptions.Where(static item => item is not null))
                {
                    diagnostics.Add(loaderException!.Message);
                }

                continue;
            }

            foreach (var type in assemblyTypes)
            {
                var system = type.GetCustomAttribute<HotfixSystemOfAttribute>();
                if (system is null)
                {
                    continue;
                }

                if (!type.IsAbstract || !type.IsSealed)
                {
                    diagnostics.Add($"Hotfix system '{type.FullName}' must be a static class.");
                    continue;
                }

                ScanSystemType(type, system.StateType, methods, diagnostics, keys);
            }
        }

        return new HotfixSystemScanResult(methods, diagnostics);
    }

    private static void ScanSystemType(
        Type systemType,
        Type stateType,
        List<HotfixMethodBinding> methods,
        List<string> diagnostics,
        HashSet<HotfixMethodKey> keys)
    {
        foreach (var method in systemType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!method.IsDefined(typeof(ExtensionAttribute), inherit: false))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType != stateType)
            {
                diagnostics.Add($"Hotfix method '{systemType.FullName}.{method.Name}' must start with 'this {stateType.FullName} self'.");
                continue;
            }

            if (method.ContainsGenericParameters)
            {
                diagnostics.Add($"Hotfix method '{systemType.FullName}.{method.Name}' must not be generic.");
                continue;
            }

            if (method.ReturnType.ContainsGenericParameters
                || parameters.Any(static parameter => parameter.ParameterType.ContainsGenericParameters))
            {
                diagnostics.Add($"Hotfix method '{systemType.FullName}.{method.Name}' must not use open generic return or parameter types.");
                continue;
            }

            if (parameters.Any(static parameter => parameter.IsOut || parameter.ParameterType.IsByRef || parameter.ParameterType.IsPointer))
            {
                diagnostics.Add($"Hotfix method '{systemType.FullName}.{method.Name}' must not use by-ref, out, or pointer parameter types.");
                continue;
            }

            var argumentTypes = parameters.Skip(1).Select(static parameter => parameter.ParameterType).ToArray();
            var key = new HotfixMethodKey(
                stateType.FullName ?? stateType.Name,
                method.Name,
                method.ReturnType.FullName ?? method.ReturnType.Name,
                argumentTypes.Select(static type => type.FullName ?? type.Name).ToArray());

            if (!keys.Add(key))
            {
                diagnostics.Add($"Duplicate hotfix method key '{key}'.");
                continue;
            }

            methods.Add(new HotfixMethodBinding(key, method, stateType, method.ReturnType, argumentTypes));
        }
    }
}
