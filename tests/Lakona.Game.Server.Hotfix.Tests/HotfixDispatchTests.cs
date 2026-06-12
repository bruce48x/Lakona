using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixDispatchTests
{
    [Fact]
    public async Task Stable_proxy_uses_replaced_hotfix_service_logic_on_next_call()
    {
        var proxy = new ChatServiceProxy(new HotfixServiceInvoker());

        ReplaceDispatchWith(1, typeof(ChatServiceV1));
        Assert.Equal("v1:hello", await proxy.EchoAsync("hello"));

        ReplaceDispatchWith(2, typeof(ChatServiceV2));
        Assert.Equal("v2:hello", await proxy.EchoAsync("hello"));
    }

    [Fact]
    public void Invoke_calls_loaded_static_extension_method()
    {
        var scan = HotfixBehaviorScanner.Scan(typeof(DispatchTestStateSystem).Assembly);
        HotfixDispatch.Replace(new HotfixDispatchTable(1, scan.Methods));
        var state = new DispatchTestState { Value = 5 };

        var result = HotfixDispatch.Invoke<DispatchTestState, int, int>(
            "Add",
            state,
            7);

        Assert.Equal(12, result);
    }

    [Fact]
    public void Invoke_calls_loaded_static_extension_method_with_state_only_delegate()
    {
        var scan = HotfixBehaviorScanner.Scan(typeof(DispatchTestStateSystem).Assembly);
        HotfixDispatch.Replace(new HotfixDispatchTable(1, scan.Methods));
        var state = new DispatchTestState { Value = 5 };

        var result = HotfixDispatch.Invoke<DispatchTestState, int>(
            "GetValue",
            state);

        Assert.Equal(5, result);
    }

    [Fact]
    public void Invoke_calls_loaded_void_static_extension_method()
    {
        var scan = HotfixBehaviorScanner.Scan(typeof(DispatchTestStateSystem).Assembly);
        HotfixDispatch.Replace(new HotfixDispatchTable(1, scan.Methods));
        var state = new DispatchTestState { Value = 5 };

        HotfixDispatch.Invoke(
            "AddExp",
            state,
            [typeof(int)],
            [7]);

        Assert.Equal(12, state.Value);
    }

    [Fact]
    public void Resolve_throws_specific_exception_when_hotfix_method_is_not_loaded()
    {
        var table = new HotfixDispatchTable(1, Array.Empty<HotfixMethodBinding>());
        var key = HotfixDispatch.CreateKey<DispatchTestState, int>("GetValue");

        Assert.Throws<HotfixMethodNotLoadedException>(() => table.Resolve(key));
    }

    private static void ReplaceDispatchWith(long version, Type serviceType)
    {
        var scan = HotfixBehaviorScanner.Scan(serviceType.Assembly, [serviceType]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        HotfixDispatch.Replace(new HotfixDispatchTable(version, scan.Methods, scan.Services));
    }
}

public interface IChatService
{
    ValueTask<string> EchoAsync(string text);
}

public sealed class ChatServiceProxy : IChatService
{
    private readonly IHotfixServiceInvoker _hotfix;

    public ChatServiceProxy(IHotfixServiceInvoker hotfix)
    {
        _hotfix = hotfix;
    }

    public ValueTask<string> EchoAsync(string text)
    {
        return _hotfix.InvokeAsync<IChatService, string, string>(
            nameof(EchoAsync),
            text);
    }
}

[HotfixService(typeof(IChatService))]
public sealed class ChatServiceV1
{
    public ValueTask<string> EchoAsync(string text)
    {
        return new ValueTask<string>("v1:" + text);
    }
}

[HotfixService(typeof(IChatService))]
public sealed class ChatServiceV2
{
    public ValueTask<string> EchoAsync(string text)
    {
        return new ValueTask<string>("v2:" + text);
    }
}

public sealed class DispatchTestState
{
    public int Value { get; set; }
}

[HotfixBehaviorOf(typeof(DispatchTestState))]
public static class DispatchTestStateSystem
{
    public static int Add(this DispatchTestState self, int amount)
    {
        return self.Value + amount;
    }

    public static int GetValue(this DispatchTestState self)
    {
        return self.Value;
    }

    public static void AddExp(this DispatchTestState self, int amount)
    {
        self.Value += amount;
    }
}
