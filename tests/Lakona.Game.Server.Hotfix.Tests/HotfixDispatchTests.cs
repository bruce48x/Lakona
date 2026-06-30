using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
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
    public async Task Actor_behavior_dispatch_enters_timer_scope()
    {
        var scan = HotfixBehaviorScanner.Scan(typeof(DispatchTestStateSystem).Assembly);
        var table = new HotfixDispatchTable(1, scan.Methods);
        var backend = new RecordingTimerBackend();
        HotfixDispatch.Replace(new HotfixDispatchTable(0, Array.Empty<HotfixMethodBinding>()));
        using var runtime = CreateScopedRuntime(table, backend, publish: false);
        using var lease = runtime.Snapshot.AcquireLease();

        await HotfixDispatch.InvokeValueTaskAsync(
            typeof(DispatchTestState),
            nameof(DispatchTestStateSystem.CreateTimerAsync),
            new DispatchTestState(),
            [typeof(TimerArgs)],
            [new TimerArgs("actor")]);

        Assert.Equal("actor", backend.LastArgs?.Value);
    }

    [Fact]
    public async Task Rpc_service_dispatch_enters_timer_scope()
    {
        var table = CreateServiceTable(typeof(TimerDispatchService), typeof(ITimerDispatchContract));
        var backend = new RecordingTimerBackend();
        HotfixDispatch.Replace(new HotfixDispatchTable(0, Array.Empty<HotfixMethodBinding>()));
        using var runtime = CreateScopedRuntime(table, backend, publish: false);
        using var lease = runtime.Snapshot.AcquireLease();

        await runtime.Snapshot.Invoker.InvokeAsync<ITimerDispatchContract, HotfixServiceCall<TimerArgs>>(
            21,
            new HotfixServiceCall<TimerArgs>(new TimerArgs("service"), runtime.Snapshot.Services),
            TestContext.Current.CancellationToken);

        Assert.Equal("service", backend.LastArgs?.Value);
    }

    [Fact]
    public async Task Feature_command_dispatch_enters_timer_scope()
    {
        var table = new HotfixDispatchTable(
            1,
            Array.Empty<HotfixMethodBinding>(),
            Array.Empty<HotfixServiceMethodBinding>(),
            [CreateFeatureDeclaration(typeof(TimerCommandFeature), "ExecuteAsync")]);
        var backend = new RecordingTimerBackend();
        using var runtime = CreateScopedRuntime(
            table,
            backend,
            featureCommands: new HotfixFeatureCommandInvoker(table));
        using var lease = runtime.Snapshot.AcquireLease();

        Assert.True(runtime.Snapshot.FeatureCommands.TryResolve("commands", FeatureCommandId.From(101), out var descriptor));
        await runtime.Snapshot.FeatureCommands.InvokeAsync(
            descriptor,
            new DispatchCommand("feature-command"),
            NewFeatureMessage("commands", "101"),
            runtime.Snapshot.Services,
            TestContext.Current.CancellationToken);

        Assert.Equal("feature-command", backend.LastArgs?.Value);
    }

    [Fact]
    public async Task Lifecycle_dispatch_enters_timer_scope()
    {
        var table = CreateServiceTable(typeof(TimerLifecycleService), typeof(ITimerLifecycleContract));
        var backend = new RecordingTimerBackend();
        using var runtime = CreateScopedRuntime(table, backend);
        using var lease = runtime.Snapshot.AcquireLease();

        await runtime.Snapshot.Invoker.InvokeAsync<ITimerLifecycleContract, HotfixLifecycleCall<TimerArgs>>(
            31,
            new HotfixLifecycleCall<TimerArgs>(new TimerArgs("lifecycle"), runtime.Snapshot.Services),
            TestContext.Current.CancellationToken);

        Assert.Equal("lifecycle", backend.LastArgs?.Value);
    }

    [Fact]
    public async Task Timer_callback_dispatch_enters_timer_scope()
    {
        var method = typeof(TimerCallbackBehavior).GetMethod(nameof(TimerCallbackBehavior.HandleAsync))!;
        var table = new HotfixDispatchTable(
            1,
            [new HotfixMethodBinding(
                HotfixDispatch.CreateKey(
                    typeof(TimerCallbackTarget),
                    nameof(TimerCallbackBehavior.HandleAsync),
                    typeof(ValueTask),
                    [typeof(TimerTick<TimerArgs>)]),
                method,
                typeof(TimerCallbackTarget),
                typeof(ValueTask),
                [typeof(TimerTick<TimerArgs>)])]);
        var backend = new RecordingTimerBackend();
        using var runtime = CreateScopedRuntime(table, backend);
        using var lease = runtime.Snapshot.AcquireLease();

        await HotfixDispatch.InvokeValueTaskAsync(
            typeof(TimerCallbackTarget),
            nameof(TimerCallbackBehavior.HandleAsync),
            new TimerCallbackTarget(),
            [typeof(TimerTick<TimerArgs>)],
            [new TimerTick<TimerArgs>(
                TimerId.FromGuid(Guid.NewGuid()),
                new TimerArgs("timer-callback"),
                runtime.Snapshot.Services,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                TestContext.Current.CancellationToken)]);

        Assert.Equal("timer-callback", backend.LastArgs?.Value);
    }

    [Fact]
    public async Task Scoped_dispatch_table_is_used_without_timer_backend()
    {
        var method = typeof(DispatchTestStateSystem).GetMethod(nameof(DispatchTestStateSystem.SetValueAsync))!;
        var scopedTable = new HotfixDispatchTable(
            7,
            [new HotfixMethodBinding(
                HotfixDispatch.CreateKey(
                    typeof(DispatchTestState),
                    nameof(DispatchTestStateSystem.SetValueAsync),
                    typeof(ValueTask),
                    [typeof(int)]),
                method,
                typeof(DispatchTestState),
                typeof(ValueTask),
                [typeof(int)])]);
        HotfixDispatch.Replace(new HotfixDispatchTable(0, Array.Empty<HotfixMethodBinding>()));
        using var runtime = CreateScopedRuntime(scopedTable, backend: null, publish: false);
        using var lease = runtime.Snapshot.AcquireLease();
        var state = new DispatchTestState();

        await HotfixDispatch.InvokeValueTaskAsync(
            typeof(DispatchTestState),
            nameof(DispatchTestStateSystem.SetValueAsync),
            state,
            [typeof(int)],
            [23]);

        Assert.Equal(23, state.Value);
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

    [Fact]
    public async Task FeatureCommandDispatchActivatesFeatureWithConstructorDiAndDisposesAfterAwait()
    {
        DispatchFeature.DisposeCount = 0;
        var services = new ServiceCollection()
            .AddSingleton(new FeatureDependency("runtime"))
            .BuildServiceProvider();
        var table = new HotfixDispatchTable(
            1,
            Array.Empty<HotfixMethodBinding>(),
            Array.Empty<HotfixServiceMethodBinding>(),
            [CreateFeatureDeclaration(typeof(DispatchFeature), "ExecuteAsync")]);
        var invoker = new HotfixFeatureCommandInvoker(table);

        Assert.True(invoker.TryResolve("commands", FeatureCommandId.From(101), out var descriptor));
        var reply = await invoker.InvokeAsync(
            descriptor,
            new DispatchCommand("room-1"),
            NewFeatureMessage("commands", "101"),
            services,
            TestContext.Current.CancellationToken);

        var typed = Assert.IsType<DispatchReply>(reply);
        Assert.Equal("runtime:room-1", typed.Value);
        Assert.Equal(1, DispatchFeature.DisposeCount);
    }

    [Fact]
    public void FeatureCommandDispatchRejectsDuplicateFeatureCommandIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new HotfixDispatchTable(
            1,
            Array.Empty<HotfixMethodBinding>(),
            Array.Empty<HotfixServiceMethodBinding>(),
            [
                CreateFeatureDeclaration(typeof(DispatchFeature), "ExecuteAsync"),
                CreateFeatureDeclaration(typeof(DispatchFeature), "ExecuteAsync")
            ]));

        Assert.Contains("Duplicate hotfix feature command", exception.Message);
    }

    [Fact]
    public void FeatureCommandDispatchValidatesMethodShape()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new HotfixDispatchTable(
            1,
            Array.Empty<HotfixMethodBinding>(),
            Array.Empty<HotfixServiceMethodBinding>(),
            [CreateFeatureDeclaration(typeof(InvalidDispatchFeature), "ExecuteAsync")]));

        Assert.Contains("Hotfix feature command", exception.Message);
        Assert.Contains("ValueTask", exception.Message);
    }

    [Fact]
    public void FeatureCommandDispatchRejectsOpenGenericMethodShape()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new HotfixDispatchTable(
            1,
            Array.Empty<HotfixMethodBinding>(),
            Array.Empty<HotfixServiceMethodBinding>(),
            [CreateFeatureDeclaration(typeof(GenericDispatchFeature), "ExecuteAsync")]));

        Assert.Contains("Hotfix feature command", exception.Message);
        Assert.Contains("ValueTask", exception.Message);
    }

    [Fact]
    public void FeatureTickValidationRejectsMissingTickMethod()
    {
        var table = new HotfixDispatchTable(1, Array.Empty<HotfixMethodBinding>());

        var exception = Assert.Throws<HotfixMethodNotLoadedException>(() =>
            table.ValidateFeatureTickMethods([
                CreateTickFeatureDeclaration(typeof(TickDispatchActor), "MissingTickAsync")
            ]));

        Assert.Contains("MissingTickAsync", exception.Message);
        Assert.Contains("is not loaded", exception.Message);
    }

    [Fact]
    public void FeatureTickValidationRejectsMalformedTickMethod()
    {
        var method = typeof(MalformedTickBehavior).GetMethod(nameof(MalformedTickBehavior.TickAsync))!;
        var binding = new HotfixMethodBinding(
            HotfixDispatch.CreateKey(
                typeof(TickDispatchActor),
                nameof(MalformedTickBehavior.TickAsync),
                typeof(ValueTask),
                [typeof(HotfixActorTick)]),
            method,
            typeof(TickDispatchActor),
            typeof(ValueTask),
            [typeof(HotfixActorTick)]);
        var table = new HotfixDispatchTable(1, [binding]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            table.ValidateFeatureTickMethods([
                CreateTickFeatureDeclaration(typeof(TickDispatchActor), nameof(MalformedTickBehavior.TickAsync))
            ]));

        Assert.Contains("Hotfix tick method", exception.Message);
        Assert.Contains("HotfixActorTick", exception.Message);
    }

    [Fact]
    public async Task FeatureCommandDispatchUnwrapsSynchronousExceptionAndDisposesFeature()
    {
        ThrowingDispatchFeature.DisposeCount = 0;
        var table = CreateFeatureCommandTable(typeof(ThrowingDispatchFeature));
        var invoker = new HotfixFeatureCommandInvoker(table);
        Assert.True(invoker.TryResolve("commands", FeatureCommandId.From(101), out var descriptor));
        using var services = new ServiceCollection().BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await invoker.InvokeAsync(
                descriptor,
                new DispatchCommand("room-1"),
                NewFeatureMessage("commands", "101"),
                services,
                TestContext.Current.CancellationToken));

        Assert.Equal("feature dispatch failure", exception.Message);
        Assert.Equal(1, ThrowingDispatchFeature.DisposeCount);
    }

    [Fact]
    public async Task FeatureCommandDispatchDisposesFeatureAfterAsyncFailure()
    {
        AsyncFailingDispatchFeature.DisposeCount = 0;
        var table = CreateFeatureCommandTable(typeof(AsyncFailingDispatchFeature));
        var invoker = new HotfixFeatureCommandInvoker(table);
        Assert.True(invoker.TryResolve("commands", FeatureCommandId.From(101), out var descriptor));
        using var services = new ServiceCollection().BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await invoker.InvokeAsync(
                descriptor,
                new DispatchCommand("room-1"),
                NewFeatureMessage("commands", "101"),
                services,
                TestContext.Current.CancellationToken));

        Assert.Equal("async feature dispatch failure", exception.Message);
        Assert.Equal(1, AsyncFailingDispatchFeature.DisposeCount);
    }

    [Fact]
    public async Task FeatureCommandDispatchDisposesAsyncDisposableFeatureAfterCompletion()
    {
        AsyncDisposableDispatchFeature.DisposeCount = 0;
        var table = CreateFeatureCommandTable(typeof(AsyncDisposableDispatchFeature));
        var invoker = new HotfixFeatureCommandInvoker(table);
        Assert.True(invoker.TryResolve("commands", FeatureCommandId.From(101), out var descriptor));
        using var services = new ServiceCollection().BuildServiceProvider();

        var reply = await invoker.InvokeAsync(
            descriptor,
            new DispatchCommand("room-1"),
            NewFeatureMessage("commands", "101"),
            services,
            TestContext.Current.CancellationToken);

        var typed = Assert.IsType<DispatchReply>(reply);
        Assert.Equal("disposed-async", typed.Value);
        Assert.Equal(1, AsyncDisposableDispatchFeature.DisposeCount);
    }

    [Fact]
    public async Task FeatureCommandDispatchStaticMethodDoesNotRequireConstructorActivation()
    {
        var table = CreateFeatureCommandTable(typeof(StaticDispatchFeature));
        var invoker = new HotfixFeatureCommandInvoker(table);
        Assert.True(invoker.TryResolve("commands", FeatureCommandId.From(101), out var descriptor));
        using var services = new ServiceCollection().BuildServiceProvider();

        table.ValidateFeatureCommandActivation(services);
        var reply = await invoker.InvokeAsync(
            descriptor,
            new DispatchCommand("room-1"),
            NewFeatureMessage("commands", "101"),
            services,
            TestContext.Current.CancellationToken);

        var typed = Assert.IsType<DispatchReply>(reply);
        Assert.Equal("static:room-1", typed.Value);
    }

    [Fact]
    public void FeatureCommandDispatchValidatesFeatureCommandActivation()
    {
        ActivationCountingDispatchFeature.ActivationCount = 0;
        ActivationCountingDispatchFeature.DisposeCount = 0;
        using var validServices = new ServiceCollection()
            .AddSingleton(new FeatureDependency("runtime"))
            .BuildServiceProvider();
        using var emptyServices = new ServiceCollection().BuildServiceProvider();

        CreateFeatureCommandTable(typeof(DispatchFeature))
            .ValidateFeatureCommandActivation(validServices);

        var missingDependencyTable = CreateFeatureCommandTable(typeof(MissingDependencyDispatchFeature));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            missingDependencyTable.ValidateFeatureCommandActivation(emptyServices));
        Assert.Contains("constructor activation failed", exception.Message);

        var multiCommandTable = new HotfixDispatchTable(
            1,
            Array.Empty<HotfixMethodBinding>(),
            Array.Empty<HotfixServiceMethodBinding>(),
            [CreateFeatureDeclaration(
                typeof(ActivationCountingDispatchFeature),
                [
                    new HotfixFeatureCommandDeclaration(typeof(DispatchCommand), typeof(DispatchReply), 101, "ExecuteAsync"),
                    new HotfixFeatureCommandDeclaration(typeof(DispatchCommand), typeof(DispatchReply), 102, "ExecuteAsync")
                ])]);

        multiCommandTable.ValidateFeatureCommandActivation(emptyServices);

        Assert.Equal(1, ActivationCountingDispatchFeature.ActivationCount);
        Assert.Equal(1, ActivationCountingDispatchFeature.DisposeCount);
    }

    [Fact]
    public void FeatureCommandDispatchResolvesFeatureNameCaseInsensitively()
    {
        var table = CreateFeatureCommandTable(typeof(DispatchFeature));
        var invoker = new HotfixFeatureCommandInvoker(table);

        Assert.True(invoker.TryResolve("COMMANDS", FeatureCommandId.From(101), out var descriptor));
        Assert.Equal("commands", descriptor.FeatureName);
    }

    private static void ReplaceDispatchWith(long version, Type serviceType)
    {
        var scan = HotfixBehaviorScanner.Scan(serviceType.Assembly, [serviceType]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        HotfixDispatch.Replace(new HotfixDispatchTable(version, scan.Methods, scan.Services));
    }

    private static HotfixDispatchTable CreateServiceTable(Type serviceType)
    {
        return CreateServiceTable(serviceType, typeof(IConstructorInjectedDispatchContract));
    }

    private static HotfixDispatchTable CreateServiceTable(Type serviceType, Type contractType)
    {
        var scan = HotfixBehaviorScanner.Scan(
            serviceType.Assembly,
            [serviceType],
            requiredServiceContracts: [contractType]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        return new HotfixDispatchTable(1, scan.Methods, scan.Services);
    }

    private static ScopedRuntime CreateScopedRuntime(
        HotfixDispatchTable table,
        RecordingTimerBackend? backend,
        IHotfixFeatureCommandInvoker? featureCommands = null,
        bool publish = true)
    {
        if (publish)
        {
            HotfixDispatch.Replace(table);
        }

        var serviceBuilder = new ServiceCollection();
        if (backend is not null)
        {
            serviceBuilder.AddSingleton<ILakonaTimerBackend>(backend);
        }

        var services = serviceBuilder.BuildServiceProvider();
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
            featureCommands ?? EmptyHotfixFeatureCommandInvoker.Instance,
            services,
            table,
            services,
            mainAssembly: null,
            loadContext: null,
            sourceVersion: null,
            sourceKind: null,
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired: null);
        return new ScopedRuntime(services, snapshot);
    }

    private static HotfixServiceCall<ConstructorInjectedDispatchRequest> CreateDispatchCall(IServiceProvider services)
    {
        return new HotfixServiceCall<ConstructorInjectedDispatchRequest>(
            new ConstructorInjectedDispatchRequest(),
            services);
    }

    private static HotfixDispatchTable CreateFeatureCommandTable(Type featureType)
    {
        return new HotfixDispatchTable(
            1,
            Array.Empty<HotfixMethodBinding>(),
            Array.Empty<HotfixServiceMethodBinding>(),
            [CreateFeatureDeclaration(featureType, "ExecuteAsync")]);
    }

    private static HotfixFeatureDeclaration CreateFeatureDeclaration(Type featureType, string methodName)
    {
        return CreateFeatureDeclaration(
            featureType,
            [new HotfixFeatureCommandDeclaration(typeof(DispatchCommand), typeof(DispatchReply), 101, methodName)]);
    }

    private static HotfixFeatureDeclaration CreateFeatureDeclaration(
        Type featureType,
        IReadOnlyList<HotfixFeatureCommandDeclaration> commands)
    {
        return new HotfixFeatureDeclaration(
            "commands",
            featureType,
            true,
            new Dictionary<string, string>(StringComparer.Ordinal),
            Array.Empty<HotfixLocalActorDeclaration>(),
            Array.Empty<HotfixActorTickDeclaration>(),
            commands,
            Array.Empty<ServiceDescriptor>());
    }

    private static HotfixFeatureDeclaration CreateTickFeatureDeclaration(Type actorType, string methodName)
    {
        return new HotfixFeatureDeclaration(
            "tick-feature",
            typeof(DispatchFeature),
            Discoverable: true,
            new Dictionary<string, string>(),
            [],
            [new HotfixActorTickDeclaration(
                HotfixActorTickMode.FixedActor,
                actorType,
                "default",
                methodName,
                TimeSpan.FromMilliseconds(250),
                TickBacklogPolicy.Coalesce)],
            [],
            []);
    }

    private static FeatureMessageRequest NewFeatureMessage(string feature, string kind)
    {
        return new FeatureMessageRequest(
            new FeatureName(feature),
            kind,
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("data-1"),
            "corr-1");
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

public sealed class FeatureDependency
{
    public FeatureDependency(string value)
    {
        Value = value;
    }

    public string Value { get; }
}

public sealed class DispatchFeature : HotfixGameFeature, IDisposable
{
    private readonly FeatureDependency _dependency;

    public DispatchFeature(FeatureDependency dependency)
    {
        _dependency = dependency;
    }

    public static int DisposeCount { get; set; }

    public ValueTask<DispatchReply> ExecuteAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        return new ValueTask<DispatchReply>(new DispatchReply($"{_dependency.Value}:{call.Request.RoomId}"));
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}

public sealed class InvalidDispatchFeature : HotfixGameFeature
{
    public ValueTask ExecuteAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        return default;
    }
}

public sealed class GenericDispatchFeature : HotfixGameFeature
{
    public ValueTask<DispatchReply> ExecuteAsync<T>(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        return new ValueTask<DispatchReply>(new DispatchReply(typeof(T).Name));
    }
}

public sealed class ThrowingDispatchFeature : HotfixGameFeature, IDisposable
{
    public static int DisposeCount { get; set; }

    public ValueTask<DispatchReply> ExecuteAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        throw new InvalidOperationException("feature dispatch failure");
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}

public sealed class AsyncFailingDispatchFeature : HotfixGameFeature, IDisposable
{
    public static int DisposeCount { get; set; }

    public async ValueTask<DispatchReply> ExecuteAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        await Task.Yield();
        throw new InvalidOperationException("async feature dispatch failure");
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}

public sealed class AsyncDisposableDispatchFeature : HotfixGameFeature, IAsyncDisposable
{
    public static int DisposeCount { get; set; }

    public async ValueTask<DispatchReply> ExecuteAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        await Task.Yield();
        return new DispatchReply("disposed-async");
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return default;
    }
}

public sealed class StaticDispatchFeature : HotfixGameFeature
{
    private StaticDispatchFeature(MissingFeatureDependency dependency)
    {
    }

    public static ValueTask<DispatchReply> ExecuteAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        return new ValueTask<DispatchReply>(new DispatchReply($"static:{call.Request.RoomId}"));
    }
}

public sealed class MissingDependencyDispatchFeature : HotfixGameFeature
{
    public MissingDependencyDispatchFeature(MissingFeatureDependency dependency)
    {
    }

    public ValueTask<DispatchReply> ExecuteAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        return new ValueTask<DispatchReply>(new DispatchReply("missing"));
    }
}

public sealed class MissingFeatureDependency
{
}

public sealed class ActivationCountingDispatchFeature : HotfixGameFeature, IDisposable
{
    public ActivationCountingDispatchFeature()
    {
        ActivationCount++;
    }

    public static int ActivationCount { get; set; }

    public static int DisposeCount { get; set; }

    public ValueTask<DispatchReply> ExecuteAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        return new ValueTask<DispatchReply>(new DispatchReply("counted"));
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}

public sealed class TickDispatchActor
{
}

public static class MalformedTickBehavior
{
    public static ValueTask TickAsync(TickDispatchActor actor)
    {
        _ = actor;
        return default;
    }
}

[FeatureCommand(101)]
public sealed record DispatchCommand(string RoomId);

public sealed record DispatchReply(string Value);

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

    public static async ValueTask CreateTimerAsync(this DispatchTestState self, TimerArgs args)
    {
        _ = self;
        await LakonaTimer.CreateOnceTimerAsync<TimerCallbackTarget, TimerArgs>(
            TimeSpan.Zero,
            nameof(TimerCallbackBehavior.HandleAsync),
            args).ConfigureAwait(false);
    }

    public static ValueTask SetValueAsync(this DispatchTestState self, int value)
    {
        self.Value = value;
        return default;
    }
}

public sealed record TimerArgs(string Value);

public sealed class TimerCallbackTarget
{
}

public static class TimerCallbackBehavior
{
    public static async ValueTask HandleAsync(this TimerCallbackTarget target, TimerTick<TimerArgs> tick)
    {
        _ = target;
        await LakonaTimer.CreateOnceTimerAsync<TimerCallbackTarget, TimerArgs>(
            TimeSpan.Zero,
            nameof(HandleAsync),
            tick.Args,
            tick.CancellationToken).ConfigureAwait(false);
    }
}

public interface ITimerDispatchContract
{
    [RpcMethod(21)]
    ValueTask RunAsync(TimerArgs request);
}

[HotfixService(typeof(ITimerDispatchContract))]
public sealed class TimerDispatchService
{
    public static async ValueTask RunAsync(HotfixServiceCall<TimerArgs> call)
    {
        await LakonaTimer.CreateOnceTimerAsync<TimerCallbackTarget, TimerArgs>(
            TimeSpan.Zero,
            nameof(TimerCallbackBehavior.HandleAsync),
            call.Request!).ConfigureAwait(false);
    }
}

public interface ITimerLifecycleContract
{
    [RpcMethod(31)]
    ValueTask RunAsync(TimerArgs request);
}

[HotfixLifecycle(typeof(ITimerLifecycleContract))]
public sealed class TimerLifecycleService
{
    public static async ValueTask RunAsync(HotfixLifecycleCall<TimerArgs> call)
    {
        await LakonaTimer.CreateOnceTimerAsync<TimerCallbackTarget, TimerArgs>(
            TimeSpan.Zero,
            nameof(TimerCallbackBehavior.HandleAsync),
            call.Request!).ConfigureAwait(false);
    }
}

public sealed class TimerCommandFeature : HotfixGameFeature
{
    public static ValueTask<DispatchReply> ExecuteAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        return CreateAsync(call);
    }

    private static async ValueTask<DispatchReply> CreateAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        await LakonaTimer.CreateOnceTimerAsync<TimerCallbackTarget, TimerArgs>(
            TimeSpan.Zero,
            nameof(TimerCallbackBehavior.HandleAsync),
            new TimerArgs(call.Request.RoomId),
            call.CancellationToken).ConfigureAwait(false);
        return new DispatchReply(call.Request.RoomId);
    }
}

internal sealed class ScopedRuntime : IDisposable
{
    private readonly ServiceProvider _services;

    public ScopedRuntime(ServiceProvider services, HotfixRuntimeSnapshot snapshot)
    {
        _services = services;
        Snapshot = snapshot;
    }

    public HotfixRuntimeSnapshot Snapshot { get; }

    public void Dispose()
    {
        _services.Dispose();
    }
}

internal sealed class RecordingTimerBackend : ILakonaTimerBackend
{
    public TimerArgs? LastArgs { get; private set; }

    public ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken)
        where TCallback : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (args is TimerArgs timerArgs)
        {
            LastArgs = timerArgs;
        }

        return new ValueTask<TimerId>(TimerId.FromGuid(Guid.NewGuid()));
    }

    public ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        TimeSpan period,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken)
        where TCallback : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (args is TimerArgs timerArgs)
        {
            LastArgs = timerArgs;
        }

        return new ValueTask<TimerId>(TimerId.FromGuid(Guid.NewGuid()));
    }

    public ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return default;
    }
}
