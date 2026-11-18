using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Rpc.Tests;

public sealed class RpcMethodNameTests
{
    [Fact]
    public void Registered_names_are_reused_and_unknown_ids_fall_back_without_registration()
    {
        var registry = new RpcServiceRegistry();
        registry.RegisterSingleton(1, new object(), "LoginService")
            .Register<string, string>(2, (_, value, _) => new ValueTask<string>(value), "LoginAsync");
        var expected = registry.ResolveDisplayName(1, 2);
        Assert.Equal("LoginService.LoginAsync", expected);
        Assert.Same(expected, registry.ResolveDisplayName(1, 2));
        Assert.Equal("123:456", registry.ResolveDisplayName(123, 456));
        Assert.False(registry.TryGetDescriptor(123, 456, out _));
    }

    [Fact]
    public void Named_descriptor_preserves_wire_identity_and_name_reads_do_not_allocate()
    {
        var method = new RpcMethod<string, string>(1, 2, "LoginService", "LoginAsync");
        Assert.Equal(1, method.ServiceId);
        Assert.Equal(2, method.MethodId);
        Assert.Null(new RpcMethod<string, string>(1, 2).DisplayName);
        var registry = new RpcServiceRegistry();
        registry.RegisterSingleton(1, new object(), "LoginService")
            .Register<string, string>(2, (_, value, _) => new ValueTask<string>(value), "LoginAsync");
        var expected = registry.ResolveDisplayName(1, 2);
        for (var i = 0; i < 1000; i++) _ = registry.ResolveDisplayName(1, 2);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var same = true;
        for (var i = 0; i < 10000; i++)
            same &= ReferenceEquals(expected, registry.ResolveDisplayName(1, 2)) && ReferenceEquals(method.DisplayName, method.DisplayName);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(same);
        Assert.Equal(0, allocated);
    }
}
