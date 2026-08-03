using System.Reflection;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Rpc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixDispatchTests
{
    [Fact]
    public void Service_invoker_contract_requires_numeric_dispatch()
    {
        var methods = typeof(IHotfixServiceInvoker).GetMethods();

        Assert.Equal(3, methods.Length);
        Assert.All(methods, static method => Assert.True(method.IsAbstract));
        Assert.All(
            methods,
            static method => Assert.Equal(typeof(int), method.GetParameters()[0].ParameterType));
    }

    [Fact]
    public async Task Actor_lifecycle_dispatch_invokes_start_and_stop_methods()
    {
        ActorLifecycleDispatchFixture.Events.Clear();
        var scan = HotfixBehaviorScanner.Scan(
            typeof(ActorLifecycleDispatchFixture.RoomBehavior).Assembly,
            [typeof(ActorLifecycleDispatchFixture.RoomBehavior)]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var table = new HotfixDispatchTable(
            1,
            scan.Methods,
            scan.Services,
            scan.ActorMethods,
            scan.ActorLifecycles);
        Assert.True(table.TryResolveActorLifecycle(
            typeof(ActorLifecycleDispatchFixture.RoomActor),
            out var descriptor));
        using var services = new ServiceCollection()
            .AddSingleton(new ActorLifecycleDispatchFixture.Marker("runtime"))
            .BuildServiceProvider();
        table.ValidateModuleActivation(services);
        var invoker = new HotfixActorLifecycleInvoker();
        var actor = new ActorLifecycleDispatchFixture.RoomActor();
        var actorId = ActorId.From("room-1");

        await invoker.StartAsync(table, descriptor, actor, actorId, services, TestContext.Current.CancellationToken);
        await invoker.StopAsync(table, descriptor, actor, actorId, services, TestContext.Current.CancellationToken);

        Assert.Equal(["start:room-1:runtime", "stop:room-1:runtime"], ActorLifecycleDispatchFixture.Events);
    }

    [Fact]
    public async Task Actor_lifecycle_stop_invokes_method_even_when_token_is_already_canceled()
    {
        ActorLifecycleDispatchFixture.Events.Clear();
        var scan = HotfixBehaviorScanner.Scan(
            typeof(ActorLifecycleDispatchFixture.RoomBehavior).Assembly,
            [typeof(ActorLifecycleDispatchFixture.RoomBehavior)]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var descriptor = Assert.Single(scan.ActorLifecycles);
        var table = new HotfixDispatchTable(
            1,
            scan.Methods,
            scan.Services,
            scan.ActorMethods,
            scan.ActorLifecycles);
        using var services = new ServiceCollection()
            .AddSingleton(new ActorLifecycleDispatchFixture.Marker("runtime"))
            .BuildServiceProvider();
        table.ValidateModuleActivation(services);
        var invoker = new HotfixActorLifecycleInvoker();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await invoker.StopAsync(
            table,
            descriptor,
            new ActorLifecycleDispatchFixture.RoomActor(),
            ActorId.From("room-canceled"),
            services,
            cts.Token);

        Assert.Equal(["stop:room-canceled:runtime"], ActorLifecycleDispatchFixture.Events);
    }

    [Fact]
    public async Task InvokeActorAsync_dispatches_behavior_actor_api_method_by_method_key()
    {
        var fixture = TwoAssemblyHotfixFixture.Create(
            """
            using Lakona.Game.Server.Actors;

            namespace StableGame;

            public sealed class UserActor : Actor<string>
            {
            }

            public sealed record PingRequest(string Text);

            public sealed record PingReply(string Text);
            """,
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using StableGame;

            namespace HotfixGame;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask<PingReply> PingAsync(
                    UserActor self,
                    PingRequest request,
                    CancellationToken cancellationToken = default)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ValueTask<PingReply>(new PingReply(request.Text));
                }
            }
            """);
        var scan = HotfixBehaviorScanner.Scan(fixture.HotfixAssembly);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var descriptor = Assert.Single(scan.ActorMethods);
        var stableAssemblyName = fixture.StableAssembly.GetName().Name;
        Assert.Contains($"actor:StableGame.UserActor, {stableAssemblyName}", descriptor.MethodKey, StringComparison.Ordinal);
        Assert.Contains("|method:PingAsync|", descriptor.MethodKey, StringComparison.Ordinal);
        Assert.Contains($"|request:StableGame.PingRequest, {stableAssemblyName}", descriptor.MethodKey, StringComparison.Ordinal);
        Assert.Contains($"|result:StableGame.PingReply, {stableAssemblyName}", descriptor.MethodKey, StringComparison.Ordinal);
        var table = Activate(new HotfixDispatchTable(1, scan.Methods, scan.Services, scan.ActorMethods));
        var actor = Activator.CreateInstance(fixture.StableAssembly.GetType("StableGame.UserActor", throwOnError: true)!)!;
        var requestType = fixture.StableAssembly.GetType("StableGame.PingRequest", throwOnError: true)!;
        var request = Activator.CreateInstance(requestType, "hello")!;

        var result = await table.InvokeActorAsync(
            descriptor.MethodKey,
            actor,
            request,
            descriptor.ResultType,
            TestContext.Current.CancellationToken);

        var text = result!.GetType().GetProperty("Text")!.GetValue(result);
        Assert.Equal("hello", text);
    }

    [Fact]
    public async Task InvokeActorAsync_enters_timer_scope_for_actor_behavior()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(ActorTimerDispatchBehavior).Assembly,
            [typeof(ActorTimerDispatchBehavior)]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var descriptor = Assert.Single(scan.ActorMethods);
        var table = Activate(new HotfixDispatchTable(1, scan.Methods, scan.Services, scan.ActorMethods));
        var backend = new RecordingTimerBackend();
        using var runtime = CreateScopedRuntime(table, backend, publish: false);
        using var lease = runtime.Snapshot.AcquireLease();

        await table.InvokeActorAsync(
            descriptor.MethodKey,
            new ActorTimerDispatchTestActor(),
            1,
            expectedResultType: typeof(void),
            TestContext.Current.CancellationToken);

        Assert.Equal("actor-dispatch", backend.LastArgs?.Value);
    }

    public static class ActorLifecycleDispatchFixture
    {
        public static List<string> Events { get; } = [];

        public sealed class RoomActor : IActor
        {
        }

        public sealed record Marker(string Value);

        [HotfixBehaviorOf(typeof(RoomActor))]
        public sealed partial class RoomBehavior
        {
            [ActorStart]
            public ValueTask StartAsync(RoomActor self, ActorStartCall call)
            {
                _ = self;
                var marker = call.Services.GetRequiredService<Marker>();
                Events.Add($"start:{call.ActorId}:{marker.Value}");
                return default;
            }

            [ActorStop]
            public ValueTask StopAsync(RoomActor self, ActorStopCall call)
            {
                _ = self;
                var marker = call.Services.GetRequiredService<Marker>();
                Events.Add($"stop:{call.ActorId}:{marker.Value}");
                return default;
            }
        }
    }

    [Fact]
    public async Task InvokeActorAsync_returns_null_for_resultless_value_task_behavior()
    {
        var fixture = TwoAssemblyHotfixFixture.Create(
            """
            using Lakona.Game.Server.Actors;

            namespace StableGame;

            public sealed class UserActor : Actor<string>
            {
                public string? LastText { get; set; }
            }

            public sealed record PingRequest(string Text);
            """,
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using StableGame;

            namespace HotfixGame;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask RememberAsync(
                    UserActor self,
                    PingRequest request,
                    CancellationToken cancellationToken = default)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    self.LastText = request.Text;
                    return default;
                }
            }
            """);
        var scan = HotfixBehaviorScanner.Scan(fixture.HotfixAssembly);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var descriptor = Assert.Single(scan.ActorMethods);
        var stableAssemblyName = fixture.StableAssembly.GetName().Name;
        Assert.Contains($"actor:StableGame.UserActor, {stableAssemblyName}", descriptor.MethodKey, StringComparison.Ordinal);
        Assert.Contains("|method:RememberAsync|", descriptor.MethodKey, StringComparison.Ordinal);
        Assert.Contains($"|request:StableGame.PingRequest, {stableAssemblyName}", descriptor.MethodKey, StringComparison.Ordinal);
        Assert.Contains("|result:void", descriptor.MethodKey, StringComparison.Ordinal);
        var table = Activate(new HotfixDispatchTable(1, scan.Methods, scan.Services, scan.ActorMethods));
        var actor = Activator.CreateInstance(fixture.StableAssembly.GetType("StableGame.UserActor", throwOnError: true)!)!;
        var requestType = fixture.StableAssembly.GetType("StableGame.PingRequest", throwOnError: true)!;
        var request = Activator.CreateInstance(requestType, "remembered")!;

        var result = await table.InvokeActorAsync(
            descriptor.MethodKey,
            actor,
            request,
            typeof(void),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal("remembered", actor.GetType().GetProperty("LastText")!.GetValue(actor));
    }

    [Fact]
    public async Task InvokeActorAsync_rejects_wrong_request_type()
    {
        var fixture = CreatePingDispatchFixture();
        var scan = HotfixBehaviorScanner.Scan(fixture.HotfixAssembly);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var descriptor = Assert.Single(scan.ActorMethods);
        var table = new HotfixDispatchTable(1, scan.Methods, scan.Services, scan.ActorMethods);
        var actor = Activator.CreateInstance(fixture.StableAssembly.GetType("StableGame.UserActor", throwOnError: true)!)!;

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await table.InvokeActorAsync(
                descriptor.MethodKey,
                actor,
                "wrong",
                descriptor.ResultType,
                TestContext.Current.CancellationToken));

        Assert.Contains("request", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StableGame.PingRequest", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeActorAsync_rejects_wrong_actor_type()
    {
        var fixture = CreatePingDispatchFixture();
        var scan = HotfixBehaviorScanner.Scan(fixture.HotfixAssembly);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var descriptor = Assert.Single(scan.ActorMethods);
        var table = new HotfixDispatchTable(1, scan.Methods, scan.Services, scan.ActorMethods);
        var requestType = fixture.StableAssembly.GetType("StableGame.PingRequest", throwOnError: true)!;
        var request = Activator.CreateInstance(requestType, "hello")!;

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await table.InvokeActorAsync(
                descriptor.MethodKey,
                new object(),
                request,
                descriptor.ResultType,
                TestContext.Current.CancellationToken));

        Assert.Contains("actor", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StableGame.UserActor", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeActorAsync_rejects_unknown_method_key()
    {
        var fixture = CreatePingDispatchFixture();
        var scan = HotfixBehaviorScanner.Scan(fixture.HotfixAssembly);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var descriptor = Assert.Single(scan.ActorMethods);
        var stableAssemblyName = fixture.StableAssembly.GetName().Name;
        var table = new HotfixDispatchTable(1, scan.Methods, scan.Services, scan.ActorMethods);
        var actor = Activator.CreateInstance(fixture.StableAssembly.GetType("StableGame.UserActor", throwOnError: true)!)!;
        var requestType = fixture.StableAssembly.GetType("StableGame.PingRequest", throwOnError: true)!;
        var request = Activator.CreateInstance(requestType, "hello")!;

        var exception = await Assert.ThrowsAsync<HotfixMethodNotLoadedException>(async () =>
            await table.InvokeActorAsync(
                $"actor:StableGame.UserActor, {stableAssemblyName}|method:MissingAsync|request:StableGame.PingRequest, {stableAssemblyName}|result:StableGame.PingReply, {stableAssemblyName}",
                actor,
                request,
                descriptor.ResultType,
                TestContext.Current.CancellationToken));

        Assert.Contains("MissingAsync", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_table_public_methods_do_not_validate_actor_tick_declarations()
    {
        var legacyMethod = string.Concat("Validate", "Fea", "ture", "TickMethods");

        Assert.DoesNotContain(
            typeof(HotfixDispatchTable).GetMethods(),
            method => method.Name == legacyMethod);
    }

    [Fact]
    public async Task Stable_proxy_uses_replaced_hotfix_service_logic_on_next_call()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var proxy = new ChatServiceProxy(new HotfixServiceInvoker(), services);

        ReplaceDispatchWith(1, typeof(ChatServiceV1));
        Assert.Equal("v1:hello", await proxy.EchoAsync("hello"));

        ReplaceDispatchWith(2, typeof(ChatServiceV2));
        Assert.Equal("v2:hello", await proxy.EchoAsync("hello"));
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
            CreateCall(new TimerArgs("service"), runtime.Snapshot.Services),
            TestContext.Current.CancellationToken);

        Assert.Equal("service", backend.LastArgs?.Value);
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
            new HotfixLifecycleCall<TimerArgs>(
                new TimerArgs("lifecycle"),
                "test",
                runtime.Snapshot.Services,
                TestDispatchDependency<IActorRuntime>.Instance,
                TestDispatchDependency<ILakonaGameServer>.Instance),
            TestContext.Current.CancellationToken);

        Assert.Equal("lifecycle", backend.LastArgs?.Value);
    }

    [Fact]
    public async Task Timer_callback_dispatch_enters_timer_scope()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(TimerCallbackBehavior).Assembly,
            [typeof(TimerCallbackBehavior)]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var descriptor = Assert.Single(scan.TimerMethods);
        var table = new HotfixDispatchTable(
            1,
            scan.Methods,
            scan.Services,
            scan.ActorMethods,
            scan.ActorLifecycles,
            scan.TimerMethods);
        var backend = new RecordingTimerBackend();
        using var runtime = CreateScopedRuntime(table, backend);
        using var lease = runtime.Snapshot.AcquireLease();

        await table.InvokeTimerAsync(
            descriptor.MethodId,
            new TimerTick<TimerArgs>(
                TimerId.FromGuid(Guid.NewGuid()),
                new TimerArgs("timer-callback"),
                runtime.Snapshot.Services,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                TestContext.Current.CancellationToken));

        Assert.Equal("timer-callback", backend.LastArgs?.Value);
    }

    [Fact]
    public async Task LakonaTimer_from_TaskRun_after_scope_exit_throws()
    {
        EscapedTimerUse.Reset();
        var scan = HotfixBehaviorScanner.Scan(
            typeof(EscapedTimerCallbackBehavior).Assembly,
            [typeof(EscapedTimerCallbackBehavior)]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var descriptor = Assert.Single(scan.TimerMethods);
        var table = new HotfixDispatchTable(
            1,
            scan.Methods,
            scan.Services,
            scan.ActorMethods,
            scan.ActorLifecycles,
            scan.TimerMethods);
        var backend = new RecordingTimerBackend();
        using var runtime = CreateScopedRuntime(table, backend);
        using var lease = runtime.Snapshot.AcquireLease();

        await table.InvokeTimerAsync(
            descriptor.MethodId,
            new TimerTick<TimerArgs>(
                TimerId.FromGuid(Guid.NewGuid()),
                new TimerArgs("escaped"),
                runtime.Snapshot.Services,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                TestContext.Current.CancellationToken));

        EscapedTimerUse.Release();
        var exception = await EscapedTimerUse.WaitForExceptionAsync(TestContext.Current.CancellationToken);

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("active hotfix execution scope", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(backend.LastArgs);
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
        ConstructorInjectedDispatchService.ConstructorCount = 0;
        using var provider = new ServiceCollection()
            .AddSingleton(new DispatchInjectedDependency("injected"))
            .AddSingleton<ConstructorInjectedDispatchService>()
            .BuildServiceProvider();
        var table = CreateServiceTable(typeof(ConstructorInjectedDispatchService));
        table.ValidateModuleActivation(provider);

        Assert.Same(
            provider.GetRequiredService<ConstructorInjectedDispatchService>(),
            table.GetActivatedModule(typeof(ConstructorInjectedDispatchService)));

        var first = await table.InvokeServiceAsync<IConstructorInjectedDispatchContract, HotfixServiceCall<ConstructorInjectedDispatchRequest>, string>(
            11,
            CreateDispatchCall(provider));
        var second = await table.InvokeServiceAsync<IConstructorInjectedDispatchContract, HotfixServiceCall<ConstructorInjectedDispatchRequest>, string>(
            11,
            CreateDispatchCall(provider));

        Assert.Equal("injected", first);
        Assert.Equal("injected", second);
        Assert.Equal(1, ConstructorInjectedDispatchService.ConstructorCount);
    }

    [Fact]
    public async Task Invoke_service_disposes_idisposable_instance_when_generation_is_retired()
    {
        DisposableDispatchService.DisposeCount = 0;
        using var provider = new ServiceCollection().BuildServiceProvider();
        var table = CreateServiceTable(typeof(DisposableDispatchService));

        var result = await table.InvokeServiceAsync<IConstructorInjectedDispatchContract, HotfixServiceCall<ConstructorInjectedDispatchRequest>, string>(
            11,
            CreateDispatchCall(provider));

        Assert.Equal("disposed-sync", result);
        Assert.Equal(0, DisposableDispatchService.DisposeCount);

        await table.DisposeAsync();

        Assert.Equal(1, DisposableDispatchService.DisposeCount);
    }

    [Fact]
    public async Task Invoke_service_disposes_async_disposable_instance_when_generation_is_retired()
    {
        AsyncDisposableDispatchService.DisposeCount = 0;
        using var provider = new ServiceCollection().BuildServiceProvider();
        var table = CreateServiceTable(typeof(AsyncDisposableDispatchService));

        var result = await table.InvokeServiceAsync<IConstructorInjectedDispatchContract, HotfixServiceCall<ConstructorInjectedDispatchRequest>, string>(
            11,
            CreateDispatchCall(provider));

        Assert.Equal("disposed-async", result);
        Assert.Equal(0, AsyncDisposableDispatchService.DisposeCount);

        await table.DisposeAsync();

        Assert.Equal(1, AsyncDisposableDispatchService.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Module_activation_and_disposal_follow_canonical_type_order(bool reverseBindings)
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(ActivationOrderAlphaService).Assembly,
            [typeof(ActivationOrderZuluService), typeof(ActivationOrderAlphaService)],
            requiredServiceContracts:
            [
                typeof(IActivationOrderAlphaContract),
                typeof(IActivationOrderZuluContract)
            ]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var bindings = reverseBindings ? scan.Services.Reverse() : scan.Services;
        var table = new HotfixDispatchTable(1, scan.Methods, bindings);
        var events = new ModuleActivationOrderEvents();
        using var provider = new ServiceCollection()
            .AddSingleton(events)
            .BuildServiceProvider();

        table.ValidateModuleActivation(provider);

        Assert.Equal(["activate:alpha", "activate:zulu"], events.Items);

        await table.DisposeAsync();

        Assert.Equal(
            ["activate:alpha", "activate:zulu", "dispose:zulu", "dispose:alpha"],
            events.Items);
    }

    [Fact]
    public async Task Invoke_service_keeps_generation_instance_after_synchronous_method_exception()
    {
        ThrowingDisposableDispatchService.DisposeCount = 0;
        using var provider = new ServiceCollection().BuildServiceProvider();
        var table = CreateServiceTable(typeof(ThrowingDisposableDispatchService));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await table.InvokeServiceAsync<IConstructorInjectedDispatchContract, HotfixServiceCall<ConstructorInjectedDispatchRequest>, string>(
                11,
                CreateDispatchCall(provider)));

        Assert.Equal("dispatch failure", ex.Message);
        Assert.Equal(0, ThrowingDisposableDispatchService.DisposeCount);

        await table.DisposeAsync();

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
        return CreateServiceTable(serviceType, typeof(IConstructorInjectedDispatchContract));
    }

    private static HotfixDispatchTable Activate(HotfixDispatchTable table)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        table.ValidateModuleActivation(services);
        return table;
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
        table.ValidateModuleActivation(services);
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
            services,
            table,
            services,
            mainAssembly: null,
            loadContext: null,
            sourceVersion: null,
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired: null);
        return new ScopedRuntime(services, snapshot);
    }

    private static HotfixServiceCall<ConstructorInjectedDispatchRequest> CreateDispatchCall(IServiceProvider services)
    {
        return CreateCall(new ConstructorInjectedDispatchRequest(), services);
    }

    private static HotfixServiceCall<TRequest> CreateCall<TRequest>(TRequest request, IServiceProvider services)
    {
        return new HotfixServiceCall<TRequest>(
            request,
            "test",
            services,
            TestDispatchDependency<IActorRuntime>.Instance,
            TestDispatchDependency<ILakonaGameServer>.Instance);
    }

    private static TwoAssemblyHotfixFixture CreatePingDispatchFixture()
    {
        return TwoAssemblyHotfixFixture.Create(
            """
            using Lakona.Game.Server.Actors;

            namespace StableGame;

            public sealed class UserActor : Actor<string>
            {
            }

            public sealed record PingRequest(string Text);

            public sealed record PingReply(string Text);
            """,
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using StableGame;

            namespace HotfixGame;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask<PingReply> PingAsync(
                    UserActor self,
                    PingRequest request,
                    CancellationToken cancellationToken = default)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ValueTask<PingReply>(new PingReply(request.Text));
                }
            }
            """);
    }

    private sealed record TwoAssemblyHotfixFixture(Assembly StableAssembly, Assembly HotfixAssembly)
    {
        public static TwoAssemblyHotfixFixture Create(string stableSource, string hotfixSource)
        {
            var references = CreateDefaultReferences();
            var stableAssemblyName = "StableGame_" + Guid.NewGuid().ToString("N");
            var hotfixAssemblyName = "HotfixGame_" + Guid.NewGuid().ToString("N");
            var stableBytes = Compile(stableAssemblyName, stableSource, references);
            var stableAssembly = Assembly.Load(stableBytes);
            var hotfixReferences = references
                .Concat([MetadataReference.CreateFromImage(stableBytes)])
                .ToArray();
            var hotfixBytes = Compile(hotfixAssemblyName, hotfixSource, hotfixReferences);

            return new TwoAssemblyHotfixFixture(stableAssembly, Assembly.Load(hotfixBytes));
        }

        private static byte[] Compile(
            string assemblyName,
            string source,
            IReadOnlyList<MetadataReference> references)
        {
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            if (!emit.Success)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
            }

            return stream.ToArray();
        }

        private static MetadataReference[] CreateDefaultReferences()
        {
            return HotfixTestMetadataReferences.CreateDefaultReferences(
                typeof(Actor<>),
                typeof(HotfixBehaviorOfAttribute),
                typeof(ValueTask),
                typeof(CancellationToken));
        }
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
    private readonly IServiceProvider _services;

    public ChatServiceProxy(IHotfixServiceInvoker hotfix, IServiceProvider services)
    {
        _hotfix = hotfix;
        _services = services;
    }

    public ValueTask<string> EchoAsync(string text)
    {
        return _hotfix.InvokeAsync<IChatService, HotfixServiceCall<string>, string>(
            7,
            new HotfixServiceCall<string>(
                text,
                "test",
                _services,
                TestDispatchDependency<IActorRuntime>.Instance,
                TestDispatchDependency<ILakonaGameServer>.Instance));
    }
}

internal static class TestDispatchDependency<T>
    where T : class
{
    public static T Instance { get; } = DispatchProxy.Create<T, ThrowingDispatchProxy>();
}

internal class ThrowingDispatchProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        throw new InvalidOperationException($"Test dispatch dependency '{targetMethod?.Name}' must not be invoked.");
    }
}

[HotfixService(typeof(IChatService))]
public sealed class ChatServiceV1
{
    public ValueTask<string> EchoAsync(HotfixServiceCall<string> call)
    {
        return new ValueTask<string>("v1:" + call.Request);
    }
}

[HotfixService(typeof(IChatService))]
public sealed class ChatServiceV2
{
    public ValueTask<string> EchoAsync(HotfixServiceCall<string> call)
    {
        return new ValueTask<string>("v2:" + call.Request);
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
    public ValueTask<WrappedLoginReply> LoginAsync(
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
    public static int ConstructorCount;

    private readonly DispatchInjectedDependency _dependency;

    public ConstructorInjectedDispatchService(DispatchInjectedDependency dependency)
    {
        Interlocked.Increment(ref ConstructorCount);
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

public sealed class ModuleActivationOrderEvents
{
    public List<string> Items { get; } = [];
}

public interface IActivationOrderAlphaContract
{
    [RpcMethod(21)]
    ValueTask RunAsync(ConstructorInjectedDispatchRequest request);
}

public interface IActivationOrderZuluContract
{
    [RpcMethod(22)]
    ValueTask RunAsync(ConstructorInjectedDispatchRequest request);
}

[HotfixService(typeof(IActivationOrderAlphaContract))]
public sealed class ActivationOrderAlphaService : IDisposable
{
    private readonly ModuleActivationOrderEvents events;

    public ActivationOrderAlphaService(ModuleActivationOrderEvents events)
    {
        this.events = events;
        events.Items.Add("activate:alpha");
    }

    public ValueTask RunAsync(HotfixServiceCall<ConstructorInjectedDispatchRequest> call)
    {
        return default;
    }

    public void Dispose()
    {
        events.Items.Add("dispose:alpha");
    }
}

[HotfixService(typeof(IActivationOrderZuluContract))]
public sealed class ActivationOrderZuluService : IDisposable
{
    private readonly ModuleActivationOrderEvents events;

    public ActivationOrderZuluService(ModuleActivationOrderEvents events)
    {
        this.events = events;
        events.Items.Add("activate:zulu");
    }

    public ValueTask RunAsync(HotfixServiceCall<ConstructorInjectedDispatchRequest> call)
    {
        return default;
    }

    public void Dispose()
    {
        events.Items.Add("dispose:zulu");
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

public sealed class ActorTimerDispatchTestActor : Actor<string>;

[HotfixBehaviorOf(typeof(ActorTimerDispatchTestActor))]
public sealed partial class ActorTimerDispatchBehavior
{
    public async ValueTask StartAsync(ActorTimerDispatchTestActor self, int request, CancellationToken cancellationToken = default)
    {
        _ = self;
        _ = request;
        await LakonaTimer.CreateOnceTimerAsync(
            TestTimerEntries.HandleAsync,
            TimeSpan.Zero,
            new TimerArgs("actor-dispatch"),
            cancellationToken);
    }
}

public sealed class DispatchTestState
{
    public int Value { get; set; }
}

[HotfixBehaviorOf(typeof(DispatchTestState))]
public sealed partial class DispatchTestStateSystem
{
    public int Add(DispatchTestState self, int amount)
    {
        return self.Value + amount;
    }

    public int GetValue(DispatchTestState self)
    {
        return self.Value;
    }

    public void AddExp(DispatchTestState self, int amount)
    {
        self.Value += amount;
    }

    public ValueTask<int> AddAsync(
        DispatchTestState self,
        int amount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<int>(self.Value + amount);
    }

    public async ValueTask CreateTimerAsync(DispatchTestState self, TimerArgs args)
    {
        _ = self;
        await LakonaTimer.CreateOnceTimerAsync(
            TestTimerEntries.HandleAsync,
            TimeSpan.Zero,
            args).ConfigureAwait(false);
    }

    public ValueTask SetValueAsync(DispatchTestState self, int value)
    {
        self.Value = value;
        return default;
    }

    public ValueTask SetFallbackValueAsync(DispatchTestState self, int value)
    {
        self.Value = value + 100;
        return default;
    }
}

public sealed record TimerArgs(string Value);

public sealed class TimerCallbackTarget
{
}

[HotfixTimer]
public sealed partial class TimerCallbackBehavior
{
    public async ValueTask HandleAsync(TimerTick<TimerArgs> tick)
    {
        await LakonaTimer.CreateOnceTimerAsync(
            static (TimerCallbackBehavior callbacks) => callbacks.HandleAsync,
            TimeSpan.Zero,
            tick.Args,
            tick.CancellationToken).ConfigureAwait(false);
    }
}

[HotfixTimer]
public sealed partial class EscapedTimerCallbackBehavior
{
    public ValueTask HandleAsync(TimerTick<TimerArgs> tick)
    {
        _ = tick;
        EscapedTimerUse.Start();
        return default;
    }
}

public static class EscapedTimerUse
{
    private static readonly object Sync = new();
    private static TaskCompletionSource? release;
    private static TaskCompletionSource<Exception>? exception;

    public static void Start()
    {
        TaskCompletionSource releaseSource;
        lock (Sync)
        {
            release ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            exception ??= new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            releaseSource = release;
        }

        Task.Run(async () =>
        {
            try
            {
                await releaseSource.Task.ConfigureAwait(false);
                await LakonaTimer.CreateOnceTimerAsync(
                    TestTimerEntries.HandleAsync,
                    TimeSpan.Zero,
                    new TimerArgs("escaped-after-scope")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (Sync)
                {
                    exception?.TrySetResult(ex);
                }
            }
        });
    }

    public static void Release()
    {
        lock (Sync)
        {
            release?.TrySetResult();
        }
    }

    public static async Task<Exception> WaitForExceptionAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<Exception> source;
        lock (Sync)
        {
            exception ??= new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            source = exception;
        }

        return await source.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
    }

    public static void Reset()
    {
        lock (Sync)
        {
            release = null;
            exception = null;
        }
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
    public async ValueTask RunAsync(HotfixServiceCall<TimerArgs> call)
    {
        await LakonaTimer.CreateOnceTimerAsync(
            TestTimerEntries.HandleAsync,
            TimeSpan.Zero,
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
    public async ValueTask RunAsync(HotfixLifecycleCall<TimerArgs> call)
    {
        await LakonaTimer.CreateOnceTimerAsync(
            TestTimerEntries.HandleAsync,
            TimeSpan.Zero,
            call.Request!).ConfigureAwait(false);
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

    public ValueTask<TimerId> CreateOnceTimerAsync<TArgs>(
        IHotfixTimerEntryResolver runtimeContext,
        HotfixTimerEntry<TArgs> callback,
        TimeSpan dueTime,
        TArgs args,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (args is TimerArgs timerArgs)
        {
            LastArgs = timerArgs;
        }

        return new ValueTask<TimerId>(TimerId.FromGuid(Guid.NewGuid()));
    }

    public ValueTask<TimerId> CreatePeriodicTimerAsync<TArgs>(
        IHotfixTimerEntryResolver runtimeContext,
        HotfixTimerEntry<TArgs> callback,
        TimeSpan dueTime,
        TimeSpan period,
        TArgs args,
        CancellationToken cancellationToken)
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

internal static class TestTimerEntries
{
    public static HotfixTimerEntry<TimerArgs> HandleAsync { get; } = new(
        typeof(TimerCallbackBehavior).FullName!,
        nameof(TimerCallbackBehavior.HandleAsync),
        42UL);
}
