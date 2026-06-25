using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Game.Server.Hotfix;
using Lakona.Rpc.Core;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task InvokeValueTaskAsync_result_uses_scanned_value_task_result_key()
    {
        var scan = HotfixBehaviorScanner.Scan(typeof(DispatchTestStateSystem).Assembly);
        HotfixDispatch.Replace(new HotfixDispatchTable(1, scan.Methods));
        var state = new DispatchTestState { Value = 5 };

        var result = await HotfixDispatch.InvokeValueTaskAsync<int>(
            typeof(DispatchTestState),
            "AddAsync",
            state,
            [typeof(int), typeof(CancellationToken)],
            [7, CancellationToken.None]);

        Assert.Equal(12, result);
    }

    [Fact]
    public void Resolve_throws_specific_exception_when_hotfix_method_is_not_loaded()
    {
        var table = new HotfixDispatchTable(1, Array.Empty<HotfixMethodBinding>());
        var key = HotfixDispatch.CreateKey<DispatchTestState, int>("GetValue");

        Assert.Throws<HotfixMethodNotLoadedException>(() => table.Resolve(key));
    }

    [Fact]
    public void Scanner_accepts_service_method_with_hotfix_call_wrapper()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(WrappedLoginService).Assembly,
            [typeof(WrappedLoginService)]);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var binding = Assert.Single(scan.Services);
        Assert.Equal(
            HotfixDispatch.CreateServiceKey<IWrappedLoginService, WrappedLoginReply>(
                9,
                typeof(HotfixServiceCall<WrappedLoginRequest, IWrappedLoginCallback>)),
            binding.Key);
    }

    [Fact]
    public void Hotfix_service_call_exposes_activation_context()
    {
        Assert.True(typeof(IHotfixCallContext).IsAssignableFrom(typeof(HotfixServiceCall<>)));
        Assert.True(typeof(IHotfixCallContext).IsAssignableFrom(typeof(HotfixServiceCall<,>)));
    }

    [Fact]
    public void Hotfix_lifecycle_call_exposes_activation_context()
    {
        Assert.True(typeof(IHotfixCallContext).IsAssignableFrom(typeof(HotfixLifecycleCall<>)));
    }

    [Fact]
    public async Task Invoke_service_uses_constructor_injection_from_call_context_services()
    {
        using var provider = new ServiceCollection()
            .AddSingleton(new DispatchInjectedDependency("injected"))
            .BuildServiceProvider();
        var table = CreateServiceTable(typeof(ConstructorInjectedDispatchService));

        var result = await table.InvokeServiceAsync<IConstructorInjectedDispatchContract, HotfixServiceCall<ConstructorInjectedDispatchRequest>, string>(
            11,
            CreateDispatchCall(provider));

        Assert.Equal("injected", result);
    }

    [Fact]
    public async Task Invoke_service_disposes_idisposable_instance_after_successful_value_task()
    {
        DisposableDispatchService.DisposeCount = 0;
        using var provider = new ServiceCollection().BuildServiceProvider();
        var table = CreateServiceTable(typeof(DisposableDispatchService));

        var result = await table.InvokeServiceAsync<IConstructorInjectedDispatchContract, HotfixServiceCall<ConstructorInjectedDispatchRequest>, string>(
            11,
            CreateDispatchCall(provider));

        Assert.Equal("disposed-sync", result);
        Assert.Equal(1, DisposableDispatchService.DisposeCount);
    }

    [Fact]
    public async Task Invoke_service_disposes_async_disposable_instance_after_async_value_task()
    {
        AsyncDisposableDispatchService.DisposeCount = 0;
        using var provider = new ServiceCollection().BuildServiceProvider();
        var table = CreateServiceTable(typeof(AsyncDisposableDispatchService));

        var result = await table.InvokeServiceAsync<IConstructorInjectedDispatchContract, HotfixServiceCall<ConstructorInjectedDispatchRequest>, string>(
            11,
            CreateDispatchCall(provider));

        Assert.Equal("disposed-async", result);
        Assert.Equal(1, AsyncDisposableDispatchService.DisposeCount);
    }

    [Fact]
    public async Task Invoke_service_disposes_instance_after_synchronous_method_exception()
    {
        ThrowingDisposableDispatchService.DisposeCount = 0;
        using var provider = new ServiceCollection().BuildServiceProvider();
        var table = CreateServiceTable(typeof(ThrowingDisposableDispatchService));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await table.InvokeServiceAsync<IConstructorInjectedDispatchContract, HotfixServiceCall<ConstructorInjectedDispatchRequest>, string>(
                11,
                CreateDispatchCall(provider)));

        Assert.Equal("dispatch failure", ex.Message);
        Assert.Equal(1, ThrowingDisposableDispatchService.DisposeCount);
    }

    private static void ReplaceDispatchWith(long version, Type serviceType)
    {
        var scan = HotfixBehaviorScanner.Scan(serviceType.Assembly, [serviceType]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        HotfixDispatch.Replace(new HotfixDispatchTable(version, scan.Methods, scan.Services));
    }

    private static HotfixDispatchTable CreateServiceTable(Type serviceType)
    {
        var scan = HotfixBehaviorScanner.Scan(
            serviceType.Assembly,
            [serviceType],
            requiredServiceContracts: [typeof(IConstructorInjectedDispatchContract)]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        return new HotfixDispatchTable(1, scan.Methods, scan.Services);
    }

    private static HotfixServiceCall<ConstructorInjectedDispatchRequest> CreateDispatchCall(IServiceProvider services)
    {
        return new HotfixServiceCall<ConstructorInjectedDispatchRequest>(
            new ConstructorInjectedDispatchRequest(),
            services);
    }
}

public interface IChatService
{
    [RpcMethod(7)]
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
            7,
            text);
    }
}

[HotfixService(typeof(IChatService))]
public sealed class ChatServiceV1
{
    public static ValueTask<string> EchoAsync(string text)
    {
        return new ValueTask<string>("v1:" + text);
    }
}

[HotfixService(typeof(IChatService))]
public sealed class ChatServiceV2
{
    public static ValueTask<string> EchoAsync(string text)
    {
        return new ValueTask<string>("v2:" + text);
    }
}

[RpcService(101, NotificationContract = typeof(IWrappedLoginCallback))]
public interface IWrappedLoginService
{
    [RpcMethod(9)]
    ValueTask<WrappedLoginReply> LoginAsync(WrappedLoginRequest request);
}

public interface IWrappedLoginCallback
{
}

public sealed class WrappedLoginRequest
{
}

public sealed class WrappedLoginReply
{
}

[HotfixService(typeof(IWrappedLoginService))]
public sealed class WrappedLoginService
{
    public static ValueTask<WrappedLoginReply> LoginAsync(
        HotfixServiceCall<WrappedLoginRequest, IWrappedLoginCallback> call)
    {
        return new ValueTask<WrappedLoginReply>(new WrappedLoginReply());
    }
}

public sealed class DispatchInjectedDependency
{
    public DispatchInjectedDependency(string value)
    {
        Value = value;
    }

    public string Value { get; }
}

public sealed class ConstructorInjectedDispatchRequest
{
}

public interface IConstructorInjectedDispatchContract
{
    [RpcMethod(11)]
    ValueTask<string> RunAsync(ConstructorInjectedDispatchRequest request);
}

[HotfixService(typeof(IConstructorInjectedDispatchContract))]
public sealed class ConstructorInjectedDispatchService
{
    private readonly DispatchInjectedDependency _dependency;

    public ConstructorInjectedDispatchService(DispatchInjectedDependency dependency)
    {
        _dependency = dependency;
    }

    public ValueTask<string> RunAsync(HotfixServiceCall<ConstructorInjectedDispatchRequest> call)
    {
        return new ValueTask<string>(_dependency.Value);
    }
}

[HotfixService(typeof(IConstructorInjectedDispatchContract))]
public sealed class DisposableDispatchService : IDisposable
{
    public static int DisposeCount;

    public ValueTask<string> RunAsync(HotfixServiceCall<ConstructorInjectedDispatchRequest> call)
    {
        return new ValueTask<string>("disposed-sync");
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}

[HotfixService(typeof(IConstructorInjectedDispatchContract))]
public sealed class ThrowingDisposableDispatchService : IDisposable
{
    public static int DisposeCount;

    public ValueTask<string> RunAsync(HotfixServiceCall<ConstructorInjectedDispatchRequest> call)
    {
        throw new InvalidOperationException("dispatch failure");
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}

[HotfixService(typeof(IConstructorInjectedDispatchContract))]
public sealed class AsyncDisposableDispatchService : IAsyncDisposable
{
    public static int DisposeCount;

    public async ValueTask<string> RunAsync(HotfixServiceCall<ConstructorInjectedDispatchRequest> call)
    {
        await Task.Yield();
        return "disposed-async";
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return default;
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

    public static ValueTask<int> AddAsync(
        this DispatchTestState self,
        int amount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<int>(self.Value + amount);
    }
}
