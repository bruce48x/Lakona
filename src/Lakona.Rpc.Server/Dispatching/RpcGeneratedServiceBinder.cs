using System.Reflection;

namespace Lakona.Rpc.Server;

public static class RpcGeneratedServiceBinder
{
    public static void BindFromAssembly(Assembly assembly, RpcServiceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(registry);

        var bindMethods = ResolveBindMethods(assembly).ToArray();
        if (bindMethods.Length == 0)
        {
            throw new InvalidOperationException(
                $"No generated RPC service binder was found in assembly '{assembly.GetName().Name}'.");
        }

        foreach (var bindMethod in bindMethods)
            bindMethod.Invoke(null, [registry]);
    }

    private static IEnumerable<MethodInfo> ResolveBindMethods(Assembly assembly)
    {
        var attributedBinders = assembly
            .GetCustomAttributes<RpcGeneratedServicesBinderAttribute>()
            .Select(attribute => attribute.BinderType)
            .Distinct();

        var binderTypes = attributedBinders.Any()
            ? attributedBinders
            : assembly.GetTypes()
                .Where(static type => type.IsClass && type.IsAbstract && type.IsSealed && type.Name == "AllServicesBinder");

        foreach (var binderType in binderTypes)
        {
            var bindMethod = binderType.GetMethod(
                "BindAll",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                [typeof(RpcServiceRegistry)],
                modifiers: null);

            if (bindMethod is null)
            {
                throw new InvalidOperationException(
                    $"Generated binder type '{binderType.FullName}' does not define BindAll(RpcServiceRegistry).");
            }

            yield return bindMethod;
        }
    }
}
