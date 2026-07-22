using System.ComponentModel;
using System.Reflection;
using Lakona.Rpc.Server;

namespace Lakona.Rpc.Tests;

public class PublicApiBoundaryTests
{
    [Fact]
    public void SessionRuntimeTypes_AreAssemblyInternal()
    {
        Assert.False(typeof(RpcSession).IsPublic);
        Assert.False(typeof(RpcHandler).IsPublic);
        Assert.False(typeof(RpcSessionHandler).IsPublic);
    }

    [Fact]
    public void ServiceRegistry_PublicMethods_DoNotExposeSessionRuntime()
    {
        var publicMethods = typeof(RpcServiceRegistry)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(publicMethods, method => method.ReturnType == typeof(RpcSession));
        Assert.DoesNotContain(
            publicMethods,
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(RpcSession)));
    }

    [Theory]
    [MemberData(nameof(HiddenRuntimeSupportTypes))]
    public void RuntimeSupportTypes_AreHiddenFromNormalIntelliSense(Type type)
    {
        AssertEditorBrowsableNever(type);
    }

    [Theory]
    [MemberData(nameof(HiddenRuntimeSupportMembers))]
    public void RuntimeSupportMembers_AreHiddenFromNormalIntelliSense(MemberInfo member)
    {
        AssertEditorBrowsableNever(member);
    }

    public static IEnumerable<object[]> HiddenRuntimeSupportTypes()
    {
        yield return [typeof(RpcSession)];
        yield return [typeof(RpcHandler)];
        yield return [typeof(RpcSessionHandler)];
        yield return [typeof(RpcRawHandler)];
        yield return [typeof(RpcServiceRegistry)];
        yield return [typeof(RpcServiceRegistration<>)];
        yield return [typeof(RpcMethodDescriptor)];
        yield return [typeof(RpcNotificationChannel)];
        yield return [typeof(RpcRawResult)];
    }

    public static IEnumerable<object[]> HiddenRuntimeSupportMembers()
    {
        yield return [typeof(RpcServerHostBuilder).GetProperty(nameof(RpcServerHostBuilder.ServiceRegistry))!];
        yield return [typeof(RpcServerHostBuilder).GetMethod(
            nameof(RpcServerHostBuilder.ConfigureServices),
            [typeof(Action<RpcServiceRegistry>)])!];
    }

    private static void AssertEditorBrowsableNever(MemberInfo member)
    {
        var attribute = member.GetCustomAttribute<EditorBrowsableAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(EditorBrowsableState.Never, attribute!.State);
    }
}
