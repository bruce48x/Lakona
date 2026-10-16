using System.Reflection;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

[assembly: AssemblyMetadata("LakonaProjectRole", "ServerApp")]

namespace Lakona.Game.Server.Hotfix.Tests;

[ComponentState]
public sealed class ComponentProbeState : IDisposable
{
    public List<string> Events { get; } = [];
    public int Disposals;
    public ComponentRollbackProbe Rollback { get; } = new();
    public void Dispose() => Disposals++;
}

public sealed class ComponentRegistrationResource : IDisposable
{
    public int Disposals;
    public void Dispose() => Disposals++;
}

public sealed class ComponentRollbackProbe
{
    public CancellationToken SlowToken, RemainingToken;
    public bool CanceledOnEntry;
}

public sealed class HotfixComponentLifecycleTests
{
    [Fact]
    public async Task State_registered_resource_is_root_owned_across_validation_reload_and_final_stop()
    {
        var fixture = new Fixture();
        var resource = fixture.Root.GetRequiredService<ComponentRegistrationResource>();
        try
        {
            fixture.Write("v1");
            Assert.True((await fixture.Manager.ValidateAsync(TestContext.Current.CancellationToken)).Succeeded);
            Assert.Equal(0, resource.Disposals);
            Assert.True((await fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
            Assert.Same(resource, ((IHotfixRuntimeAccessor)fixture.Manager).Current.Services.GetRequiredService<ComponentRegistrationResource>());
            fixture.Write("v2");
            Assert.True((await fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
            Assert.Same(resource, ((IHotfixRuntimeAccessor)fixture.Manager).Current.Services.GetRequiredService<ComponentRegistrationResource>());
            await fixture.Manager.DisposeAsync();
            Assert.Equal(0, resource.Disposals);
        }
        finally { fixture.Dispose(); }
        Assert.Equal(1, resource.Disposals);
    }

    [Fact]
    public async Task Validate_has_no_start_and_reload_retains_state_until_latest_stop_after_old_leases()
    {
        using var fixture = new Fixture();
        var state = fixture.Root.GetRequiredService<ComponentProbeState>();
        await using var manager = fixture.Manager;
        fixture.Write("v1");
        Assert.True((await manager.ValidateAsync(TestContext.Current.CancellationToken)).Succeeded);
        Assert.Empty(state.Events);
        Assert.True((await manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
        var first = ((IHotfixRuntimeAccessor)manager).Current;
        var firstInstance = first.Services.GetRequiredService(first.Services.GetRequiredService<HotfixComponentCatalog>().Ordered[0].Type);
        using var oldLease = await Task.Run(() => first.AcquireLease(), TestContext.Current.CancellationToken);
        fixture.Write("v2");
        Assert.True((await manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
        var latest = ((IHotfixRuntimeAccessor)manager).Current;
        var component = latest.Services.GetRequiredService<HotfixComponentCatalog>().Ordered[0];
        Assert.NotSame(firstInstance, latest.Services.GetRequiredService(component.Type));
        Assert.Same(state, latest.Services.GetRequiredService<ComponentProbeState>());
        Assert.Equal(["start:v1"], state.Events);
        var stopping = manager.DisposeAsync().AsTask();
        Assert.False(stopping.IsCompleted);
        Assert.Equal(0, state.Disposals);
        oldLease.Dispose();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(["start:v1", "stop:v2"], state.Events);
        Assert.Equal(0, state.Disposals);
        await fixture.Root.DisposeAsync();
        Assert.Equal(1, state.Disposals);
    }

    [Theory]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("state")]
    [InlineData("order")]
    public async Task Incompatible_candidate_keeps_old_publication_and_does_not_start(string change)
    {
        using var fixture = new Fixture();
        fixture.Write("v1", second: true);
        await using var manager = fixture.Manager;
        Assert.True((await manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
        var previous = ((IHotfixRuntimeAccessor)manager).Current;
        fixture.Write("v2", second: change != "removed", extra: change == "added", noState: change == "state", reversed: change == "order");
        var rejected = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.False(rejected.Succeeded);
        Assert.Same(previous, ((IHotfixRuntimeAccessor)manager).Current);
        Assert.Equal(["start:v1", "start:B"], fixture.Root.GetRequiredService<ComponentProbeState>().Events);
    }

    [Fact]
    public async Task Startup_failure_rolls_back_only_completed_components_and_preserves_original_error()
    {
        using var fixture = new Fixture();
        fixture.Write("v1", second: true, failStart: true, failStop: true);
        await using var manager = fixture.Manager;
        var failed = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.False(failed.Succeeded);
        Assert.Contains("start failed", failed.ErrorMessage);
        Assert.Equal(["start:v1", "start:B", "stop:v1"], fixture.Root.GetRequiredService<ComponentProbeState>().Events);
        Assert.Contains(failed.Diagnostics, message => message.Contains("stop", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reverse_stop_continues_after_first_failure_and_reports_all_errors(bool alsoFailDependency)
    {
        using var fixture = new Fixture();
        fixture.Write("v1", second: true, failStop: alsoFailDependency, failSecondStop: true);
        var resource = fixture.Root.GetRequiredService<ComponentRegistrationResource>();
        await using var manager = fixture.Manager;
        Assert.True((await manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
        var error = await Assert.ThrowsAsync<AggregateException>(() => manager.DisposeAsync().AsTask());
        Assert.Contains("B stop failed", error.ToString());
        Assert.Equal(alsoFailDependency ? 2 : 1, error.Flatten().InnerExceptions.Count);
        await manager.DisposeAsync();
        Assert.Equal(["start:v1", "start:B", "stop:B", "stop:v1"], fixture.Root.GetRequiredService<ComponentProbeState>().Events);
        Assert.Equal(0, resource.Disposals);
        await fixture.Root.DisposeAsync();
        Assert.Equal(1, resource.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rollback_deadline_is_shared_and_expiry_does_not_skip_remaining_components(bool cancelStartup)
    {
        using var fixture = new Fixture();
        var probe = fixture.Root.GetRequiredService<ComponentProbeState>().Rollback;
        fixture.Root.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout = TimeSpan.FromMilliseconds(100);
        fixture.Compile("""
            [HotfixComponent] public sealed class A(ComponentProbeState state)
            {
                public void Record(string item) => state.Events.Add(item);
                public void RecordSlowToken(CancellationToken token)
                { state.Rollback.SlowToken = token; state.Rollback.CanceledOnEntry = token.IsCancellationRequested; }
                [ComponentStart] public ValueTask Begin(CancellationToken token) { Record("start:A"); return default; }
                [ComponentStop] public ValueTask End(CancellationToken token)
                { state.Rollback.RemainingToken = token; Record("stop:A"); return default; }
            }
            [HotfixComponent] public sealed class B(A a)
            {
                [ComponentStart] public ValueTask Begin(CancellationToken token) { a.Record("start:B"); return default; }
                [ComponentStop] public async ValueTask End(CancellationToken token)
                {
                    a.Record("stop:B"); a.RecordSlowToken(token);
                    if (token.CanBeCanceled) await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
            }
            """ + $$"""
            [HotfixComponent] public sealed class C(B b, CancellationTokenSource startup)
            {
                [ComponentStart] public ValueTask Begin(CancellationToken token)
                {
                    {{(cancelStartup ? "startup.Cancel(); throw new OperationCanceledException(\"startup canceled\", token);" : "throw new InvalidOperationException(\"start failed\");")}}
                }
                [ComponentStop] public ValueTask End(CancellationToken token) => throw new Exception("failed component must not stop");
            }
            """, "services.AddSingleton<A>(); services.AddSingleton<B>(); services.AddSingleton<C>();");
        var reload = fixture.Manager.ReloadAsync(fixture.Cancellation.Token).AsTask();
        var failed = await reload.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(failed.Succeeded);
        Assert.Contains(cancelStartup ? "startup canceled" : "start failed", failed.ErrorMessage);
        Assert.Equal(typeof(AggregateException).FullName, failed.ExceptionType);
        Assert.Contains(failed.Diagnostics, message => message.Contains("Component 'B' failed to stop"));
        Assert.True(probe.SlowToken.CanBeCanceled);
        Assert.False(probe.CanceledOnEntry);
        Assert.NotEqual(fixture.Cancellation.Token, probe.SlowToken);
        Assert.Equal(probe.SlowToken, probe.RemainingToken);
        Assert.True(probe.RemainingToken.IsCancellationRequested);
        var state = fixture.Root.GetRequiredService<ComponentProbeState>();
        await fixture.Manager.DisposeAsync();
        Assert.Equal(["start:A", "start:B", "stop:B", "stop:A"], state.Events);
        await fixture.Root.DisposeAsync();
        Assert.Equal(1, state.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_activation_failure_only_rolls_back_components_on_initial_start(bool afterPublication)
    {
        var participant = new ControlledPublicationParticipant();
        using var fixture = new Fixture(participants: [participant]);
        fixture.Write("v1", second: true);
        var resource = fixture.Root.GetRequiredService<ComponentRegistrationResource>();
        var state = fixture.Root.GetRequiredService<ComponentProbeState>();
        if (afterPublication)
            Assert.True((await fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
        var previous = ((IHotfixRuntimeAccessor)fixture.Manager).Current;
        participant.FailActivation = true;
        fixture.Write("v2", second: true);
        var failed = await fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.False(failed.Succeeded);
        Assert.Contains("publication activation failed", failed.ErrorMessage);
        Assert.Same(previous, ((IHotfixRuntimeAccessor)fixture.Manager).Current);
        Assert.Equal(1, participant.Rollbacks);
        Assert.Equal(afterPublication ? ["start:v1", "start:B"] : ["start:v2", "start:B", "stop:B", "stop:v2"], state.Events);
        Assert.Equal(0, resource.Disposals);
        await fixture.Manager.DisposeAsync();
        Assert.Equal(afterPublication ? ["start:v1", "start:B", "stop:B", "stop:v1"] : ["start:v2", "start:B", "stop:B", "stop:v2"], state.Events);
        await fixture.Root.DisposeAsync();
        Assert.Equal(1, resource.Disposals);
    }

    [Fact]
    public async Task Stopping_waits_for_in_progress_publication_and_rejects_queued_and_new_reloads()
    {
        var participant = new ControlledPublicationParticipant();
        using var fixture = new Fixture(participants: [participant]);
        fixture.Write("v1", second: true);
        Assert.True((await fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
        var previous = ((IHotfixRuntimeAccessor)fixture.Manager).Current;
        participant.BlockActivation = true;
        fixture.Write("v2", second: true);
        var reload = fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken).AsTask();
        try
        {
            await participant.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var queued = fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken).AsTask();
            var freezing = fixture.Manager.BeginStoppingAsync();
            Assert.False(freezing.IsCompleted);
            Assert.False(queued.IsCompleted);
            Assert.Same(previous, ((IHotfixRuntimeAccessor)fixture.Manager).Current);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken).AsTask());
            participant.Release.TrySetResult();
            Assert.True((await reload.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Succeeded);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
            await freezing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.NotSame(previous, ((IHotfixRuntimeAccessor)fixture.Manager).Current);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken).AsTask());
            await fixture.Manager.DisposeAsync();
            Assert.Equal(["start:v1", "start:B", "stop:B", "stop:v2"], fixture.Root.GetRequiredService<ComponentProbeState>().Events);
        }
        finally
        {
            participant.Release.TrySetResult();
            await reload;
        }
    }

    [Theory]
    [InlineData("public Task Run(CancellationToken token) => Task.CompletedTask;")]
    [InlineData("private ValueTask Run(CancellationToken token) => default;")]
    [InlineData("public static ValueTask Run(CancellationToken token) => default;")]
    [InlineData("public ValueTask Run(CancellationToken token = default) => default;")]
    [InlineData("public ValueTask Run(ref CancellationToken token) => default;")]
    [InlineData("public ValueTask Run<T>(CancellationToken token) => default;")]
    public async Task Runtime_rejects_invalid_hook_before_activation(string method)
    {
        using var fixture = new Fixture();
        fixture.Compile("[HotfixComponent] public sealed class A { [ComponentStart] " + method + " }", "services.AddSingleton<A>();");
        await using var manager = fixture.Manager;
        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Contains("ValueTask(CancellationToken)", result.ErrorMessage);
    }

    [Fact]
    public async Task Role_filter_precedes_construction_but_missing_local_dependency_is_rejected()
    {
        using var fixture = new Fixture(roles: ["gateway"]);
        fixture.Compile("""
            [HotfixComponent, NodeRole("data")] public sealed class Data
            { public Data() => throw new Exception("must not construct"); }
            [HotfixComponent] public sealed class All { }
            """, "services.AddSingleton<Data>(); services.AddSingleton<All>();");
        await using var manager = fixture.Manager;
        Assert.True((await manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
        fixture.Compile("""
            [HotfixComponent, NodeRole("data")] public sealed class Data { }
            [HotfixComponent] public sealed class All(Data data) { }
            """, "services.AddSingleton<Data>(); services.AddSingleton<All>();");
        var failure = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.False(failure.Succeeded);
        Assert.Contains("disabled", failure.ErrorMessage);
    }

    [Fact]
    public async Task Diamond_order_includes_hookless_intermediate_and_reverses_actual_activation()
    {
        using var fixture = new Fixture();
        fixture.Compile("""
            [HotfixComponent] public sealed class A(ComponentProbeState state)
            {
                public void Record(string item) => state.Events.Add(item);
                [ComponentStart] public ValueTask Begin(CancellationToken token) { Record("start:A"); return default; }
                [ComponentStop] public ValueTask End(CancellationToken token) { Record("stop:A"); return default; }
            }
            [HotfixComponent] public sealed class B(A a) { }
            [HotfixComponent] public sealed class C(A a)
            {
                [ComponentStart] public ValueTask Begin(CancellationToken token) { a.Record("start:C"); return default; }
                [ComponentStop] public ValueTask End(CancellationToken token) { a.Record("stop:C"); return default; }
            }
            [HotfixComponent] public sealed class D(A a, B b, C c)
            {
                [ComponentStart] public ValueTask Begin(CancellationToken token) { a.Record("start:D"); return default; }
                [ComponentStop] public ValueTask End(CancellationToken token) { a.Record("stop:D"); return default; }
            }
            """, "services.AddSingleton<A>(); services.AddSingleton<B>(); services.AddSingleton<C>(); services.AddSingleton<D>();");
        var result = await fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var state = fixture.Root.GetRequiredService<ComponentProbeState>();
        await fixture.Manager.DisposeAsync();
        Assert.Equal(["start:A", "start:C", "start:D", "stop:D", "stop:C", "stop:A"], state.Events);
    }

    [Theory]
    [InlineData("[HotfixComponent] public sealed class A(ComponentRegistrationResource resource) {}",
        "services.AddSingleton<A>();", "custom ServerApp dependencies")]
    [InlineData("[HotfixComponent] public sealed class A(Func<ComponentRegistrationResource> resource) {}",
        "services.AddSingleton<A>();", "custom ServerApp dependencies")]
    [InlineData("[HotfixComponent] public sealed class A(object resource) {}",
        "services.AddSingleton<A>();", "object and unmarked App")]
    [InlineData("[HotfixComponent] public sealed class A(ComponentProbeState first, ComponentProbeState second) {}",
        "services.AddSingleton<A>();", "at most one state")]
    [InlineData("[HotfixComponent] public sealed class A(ComponentProbeState[] states) {}",
        "services.AddSingleton<A>();", "state collections and wrappers")]
    [InlineData("[HotfixComponent] public sealed class A(System.Collections.Generic.IEnumerable<ComponentProbeState> states) {}",
        "services.AddSingleton<A>();", "state collections and wrappers")]
    [InlineData("[HotfixComponent] public sealed class A(Func<ComponentProbeState> state) {}",
        "services.AddSingleton<A>();", "state collections and wrappers")]
    [InlineData("[HotfixComponent] public sealed class A(Lazy<System.Collections.Generic.IEnumerable<ComponentProbeState[]>> state) {}",
        "services.AddSingleton<A>();", "state collections and wrappers")]
    [InlineData("[HotfixComponent] public sealed class A(ComponentProbeState state) { private readonly ComponentProbeState _a = state, _b = state; }",
        "services.AddSingleton<A>();", "at most one field")]
    [InlineData("[HotfixComponent] public sealed class A(ComponentProbeState state) { private readonly ComponentProbeState _a = state; private ComponentProbeState B { get; } = state; }",
        "services.AddSingleton<A>();", "at most one field")]
    [InlineData("[HotfixComponent] public sealed class A(ComponentProbeState state) { private readonly ComponentProbeState _a = state; public void Read() => state.Events.Add(\"read\"); }",
        "services.AddSingleton<A>();", "at most one field")]
    [InlineData("[HotfixComponent] public sealed class A(ComponentProbeState state) { private readonly ComponentProbeState[] _states = [state]; }",
        "services.AddSingleton<A>();", "without state collections or wrappers")]
    [InlineData("[HotfixComponent] public sealed class A { private readonly ComponentProbeState _state; }",
        "services.AddSingleton<A>();", "directly injected concrete ComponentState")]
    [InlineData("[HotfixComponent] public sealed class A {} public sealed class Consumer(Func<ComponentProbeState> state) {}",
        "services.AddSingleton<A>(); services.AddSingleton<Consumer>();", "ComponentState through collections or wrappers")]
    [InlineData("[HotfixComponent] public sealed class A(ComponentProbeState state) {} [HotfixComponent] public sealed class B(ComponentProbeState state) {}",
        "services.AddSingleton<A>(); services.AddSingleton<B>();", "multiple owners")]
    [InlineData("[HotfixComponent] public sealed class A(ComponentProbeState state) {}",
        "services.AddSingleton<A>(); services.AddSingleton<ComponentProbeState>();", "cannot be registered")]
    [InlineData("[HotfixComponent] public sealed class A {}",
        "services.AddSingleton<A>(); services.AddSingleton<Func<A>>(_ => () => new A());", "cannot be overridden or aliased")]
    [InlineData("[HotfixComponent] public sealed class A {}",
        "services.AddSingleton<A>(); services.AddSingleton<A[]>(Array.Empty<A>());", "cannot be overridden or aliased")]
    [InlineData("[HotfixComponent] public sealed class A { [ComponentStart] [ComponentStop] public ValueTask Run(CancellationToken token) => default; }",
        "services.AddSingleton<A>();", "both lifecycle")]
    [InlineData("[HotfixComponent] public sealed class A { [ComponentStart] public ValueTask One(CancellationToken token) => default; [ComponentStart] public ValueTask Two(CancellationToken token) => default; }",
        "services.AddSingleton<A>();", "duplicate")]
    public async Task Invalid_ownership_and_duplicate_hooks_fail_before_start(string source, string registrations, string error)
    {
        using var fixture = new Fixture();
        fixture.Compile(source, registrations);
        var result = await fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Contains(error, result.ErrorMessage);
        Assert.Empty(fixture.Root.GetRequiredService<ComponentProbeState>().Events);
    }

    [Fact]
    public async Task State_cannot_have_another_owner_even_on_a_different_NodeRole()
    {
        using var fixture = new Fixture(roles: ["gateway"]);
        fixture.Compile("""
            [NodeRole("gateway")] [HotfixComponent] public sealed class A(ComponentProbeState state) { }
            [NodeRole("data")] [HotfixComponent] public sealed class B(ComponentProbeState state) { }
            """, "services.AddSingleton<A>(); services.AddSingleton<B>();");
        var result = await fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Contains("multiple owners", result.ErrorMessage);
        Assert.Empty(fixture.Root.GetRequiredService<ComponentProbeState>().Events);
    }

    [Theory]
    [InlineData("private readonly ComponentProbeState _state = state; private ComponentProbeState View => _state;", "View")]
    [InlineData("private ComponentProbeState Stored { get; } = state;", "Stored")]
    [InlineData("", "state")]
    public async Task Single_State_storage_and_stable_resource_wrappers_load_successfully(string storage, string access)
    {
        using var fixture = new Fixture(configure: services =>
        {
            services.AddLogging();
            services.AddSingleton<System.Net.Http.HttpClient>();
            services.AddSingleton<Func<System.Net.Http.HttpClient>>(provider => provider.GetRequiredService<System.Net.Http.HttpClient>);
            services.AddSingleton(provider => new Lazy<System.Net.Http.HttpClient>(provider.GetRequiredService<System.Net.Http.HttpClient>));
        });
        fixture.Compile($$"""
            [HotfixComponent] public sealed class A(ComponentProbeState state,
                System.Collections.Generic.IEnumerable<System.Net.Http.HttpClient> resources,
                Func<System.Net.Http.HttpClient> factory, Lazy<System.Net.Http.HttpClient> lazy,
                Microsoft.Extensions.Logging.ILogger<ComponentProbeState> logger)
            {
                {{storage}}
                [ComponentStart] public ValueTask Begin(CancellationToken token)
                {
                    if (!System.Linq.Enumerable.Any(resources) || !ReferenceEquals(factory(), lazy.Value)) throw new Exception("missing resources");
                    {{access}}.Events.Add("started");
                    return default;
                }
            }
            """, "services.AddSingleton<A>();");
        var result = await fixture.Manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(["started"], fixture.Root.GetRequiredService<ComponentProbeState>().Events);
    }

    [Fact]
    public async Task Canceled_start_uses_independent_token_for_successful_dependency_rollback()
    {
        using var fixture = new Fixture();
        fixture.Compile("""
            [HotfixComponent] public sealed class A(ComponentProbeState state)
            {
                [ComponentStart] public ValueTask Begin(CancellationToken token) { state.Events.Add("started"); return default; }
                [ComponentStop] public ValueTask End(CancellationToken token) { token.ThrowIfCancellationRequested(); state.Events.Add("rolled-back"); return default; }
            }
            [HotfixComponent] public sealed class B(A a, CancellationTokenSource cancellation, Microsoft.Extensions.Options.IOptions<Microsoft.Extensions.Hosting.HostOptions> options)
            {
                [ComponentStart] public ValueTask Begin(CancellationToken token) { options.Value.ShutdownTimeout = TimeSpan.MaxValue; cancellation.Cancel(); token.ThrowIfCancellationRequested(); return default; }
            }
            """, "services.AddSingleton<A>(); services.AddSingleton<B>();");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Manager.ReloadAsync(fixture.Cancellation.Token).AsTask());
        Assert.Equal(["started", "rolled-back"], fixture.Root.GetRequiredService<ComponentProbeState>().Events);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "LakonaComponents", Guid.NewGuid().ToString("N"));
        public ServiceProvider Root { get; }
        public HotfixManager Manager { get; }
        public CancellationTokenSource Cancellation { get; } = new();

        public Fixture(string[]? roles = null, Action<IServiceCollection>? configure = null,
            IEnumerable<IHotfixRuntimePublicationParticipant>? participants = null)
        {
            Directory.CreateDirectory(directory);
            var services = new ServiceCollection();
            services.AddSingleton(Cancellation);
            services.AddSingleton<ComponentRegistrationResource>();
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Hosting.HostOptions()));
            ComponentStateRegistration.Register(services, [typeof(ComponentProbeState).Assembly]);
            if (roles is not null) services.AddSingleton(new NodeRoleCatalog(roles, []));
            configure?.Invoke(services);
            Root = services.BuildServiceProvider();
            Manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(directory, "Components.dll"),
                [typeof(ComponentProbeState).Assembly.GetName().Name!], rootServices: Root, participants: participants);
        }

        public void Write(string version, bool second = false, bool extra = false, bool noState = false,
            bool reversed = false, bool failStart = false, bool failStop = false, bool failSecondStop = false)
        {
            var source = $$"""
                [HotfixComponent] public sealed class A({{(noState ? "" : "ComponentProbeState state")}}{{(reversed ? ", B b" : "")}})
                {
                    [ComponentStart] public ValueTask Begin(CancellationToken token) { {{(noState ? "" : $"state.Events.Add(\"start:{version}\");")}} return default; }
                    [ComponentStop] public ValueTask End(CancellationToken token) { {{(noState ? "" : $"state.Events.Add(\"stop:{version}\");")}} {{(failStop ? "throw new Exception(\"stop failed\");" : "return default;")}} }
                    public void Record(string item) { {{(noState ? "" : "state.Events.Add(item);")}} }
                }
                """;
            if (second) source += $$"""
                [HotfixComponent] public sealed class B({{(reversed ? "" : "A a")}})
                {
                    [ComponentStart] public ValueTask Begin(CancellationToken token) { {{(reversed ? "" : "a.Record(\"start:B\");")}} {{(failStart ? "throw new Exception(\"start failed\");" : "return default;")}} }
                    [ComponentStop] public ValueTask End(CancellationToken token) { {{(reversed ? "" : "a.Record(\"stop:B\");")}} {{(failSecondStop ? "throw new Exception(\"B stop failed\");" : "return default;")}} }
                }
                """;
            if (extra) source += "[HotfixComponent] public sealed class C { }";
            Compile(source, "services.AddSingleton<A>();" + (second ? "services.AddSingleton<B>();" : "") + (extra ? "services.AddSingleton<C>();" : ""));
        }

        public void Compile(string declarations, string registrations)
        {
            var source = """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using Lakona.Game.Server;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix;
                using Lakona.Game.Server.Hotfix.Tests;
                using Microsoft.Extensions.DependencyInjection;
                """ + declarations + $$"""
                public sealed class Registration : IHotfixGeneratedServiceRegistration
                { public void Register(IServiceCollection services) { {{registrations}} } }
                """;
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Append(typeof(ComponentProbeState).Assembly.Location).Append(typeof(HotfixManager).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("Components", [CSharpSyntaxTree.ParseText(source)], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var result = compilation.Emit(Path.Combine(directory, "Components.dll"));
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        }

        public void Dispose()
        {
            Manager.Dispose();
            Root.Dispose();
            Cancellation.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ControlledPublicationParticipant : IHotfixRuntimePublicationParticipant
    {
        public bool FailActivation, BlockActivation;
        public int Rollbacks;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IHotfixRuntimePublicationTransaction> PrepareAsync(HotfixRuntimeSnapshot previous,
            HotfixRuntimeSnapshot candidate, CancellationToken cancellationToken = default) => new(new Transaction(this));

        private sealed class Transaction(ControlledPublicationParticipant owner) : IHotfixRuntimePublicationTransaction
        {
            public async ValueTask ActivateAsync(CancellationToken cancellationToken = default)
            {
                if (owner.BlockActivation)
                {
                    owner.Entered.TrySetResult();
                    await owner.Release.Task.WaitAsync(cancellationToken);
                }
                if (owner.FailActivation) throw new InvalidOperationException("publication activation failed");
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) { owner.Rollbacks++; return default; }
            public ValueTask CommitAsync(CancellationToken cancellationToken = default) => default;
            public ValueTask DisposeAsync() => default;
        }
    }
}
