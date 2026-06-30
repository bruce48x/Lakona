using System.Reflection;

namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record HotfixFeatureLifecycleDeclaration(MethodInfo? StartMethod, MethodInfo? StopMethod)
{
    public static HotfixFeatureLifecycleDeclaration Empty { get; } = new(null, null);

    public static HotfixFeatureLifecycleDeclaration FromFeatureType(Type featureType)
    {
        ArgumentNullException.ThrowIfNull(featureType);
        RejectPublicOnReload(featureType);
        var start = ResolveOptionalLifecycleMethod(
            featureType,
            "StartAsync",
            typeof(HotfixFeatureStartCall));
        var stop = ResolveOptionalLifecycleMethod(
            featureType,
            "StopAsync",
            typeof(HotfixFeatureStopCall));
        return start is null && stop is null ? Empty : new HotfixFeatureLifecycleDeclaration(start, stop);
    }

    private static MethodInfo? ResolveOptionalLifecycleMethod(
        Type featureType,
        string methodName,
        Type callType)
    {
        var matches = featureType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        var method = matches.SingleOrDefault(method =>
        {
            var parameters = method.GetParameters();
            return method.IsStatic &&
                !method.ContainsGenericParameters &&
                method.ReturnType == typeof(ValueTask) &&
                parameters.Length == 1 &&
                parameters[0].ParameterType == callType;
        });
        if (method is null)
        {
            throw new InvalidOperationException(
                $"Hotfix feature '{featureType.FullName}' lifecycle hook '{methodName}' must declare public static ValueTask {methodName}({callType.Name} call).");
        }

        return method;
    }

    private static void RejectPublicOnReload(Type featureType)
    {
        if (featureType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Any(static method => string.Equals(method.Name, "OnReload", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Hotfix feature '{featureType.FullName}' declares public OnReload, which is not supported.");
        }
    }
}
