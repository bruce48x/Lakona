using System.Reflection;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Rpc.Serializer.MemoryPack;
using Server.Hotfix.State.Users;
using Xunit;

public sealed class RemoteActorPayloadMemoryPackTests
{
    [Fact]
    public void RemoteActorPayload_roots_are_serializable_by_MemoryPack()
    {
        var serializer = new RpcRemoteActorSerializer(new MemoryPackRpcSerializer());
        var payloadTypes = DiscoverPayloadRootTypes();

        Assert.NotEmpty(payloadTypes);

        foreach (var payloadType in payloadTypes)
        {
            var instance = Activator.CreateInstance(payloadType);
            serializer.Serialize(instance, payloadType);
        }
    }

    private static Type[] DiscoverPayloadRootTypes()
    {
        return typeof(UserBehavior).Assembly
            .GetTypes()
            .Select(type => new
            {
                BehaviorType = type,
                Attribute = type.GetCustomAttribute<HotfixBehaviorOfAttribute>()
            })
            .Where(item => item.Attribute is not null && item.BehaviorType.IsPublic)
            .SelectMany(item => item.BehaviorType
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttribute<ActorStartAttribute>() is null
                    && method.GetCustomAttribute<ActorStopAttribute>() is null)
                .SelectMany(method => DiscoverMethodPayloadRoots(method, item.Attribute!.ActorType)))
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<Type> DiscoverMethodPayloadRoots(MethodInfo method, Type actorType)
    {
        foreach (var parameter in method.GetParameters())
        {
            if (parameter.ParameterType != actorType && parameter.ParameterType != typeof(CancellationToken))
            {
                yield return parameter.ParameterType;
            }
        }

        if (method.ReturnType.IsGenericType
            && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            yield return method.ReturnType.GetGenericArguments()[0];
        }
    }
}
