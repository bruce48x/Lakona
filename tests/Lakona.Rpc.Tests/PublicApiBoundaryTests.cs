using System.ComponentModel;
using System.Reflection;
using Lakona.Rpc.Server;

namespace Lakona.Rpc.Tests;

public class PublicApiBoundaryTests
{
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
        yield return [typeof(RpcServiceRegistry)];
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
