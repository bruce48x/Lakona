using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixDispatchTests
{
    [Fact]
    public void Invoke_calls_loaded_static_extension_method()
    {
        var scan = HotfixSystemScanner.Scan(typeof(DispatchTestStateSystem).Assembly);
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
        var scan = HotfixSystemScanner.Scan(typeof(DispatchTestStateSystem).Assembly);
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
        var scan = HotfixSystemScanner.Scan(typeof(DispatchTestStateSystem).Assembly);
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
}

public sealed class DispatchTestState
{
    public int Value { get; set; }
}

[HotfixSystemOf(typeof(DispatchTestState))]
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
