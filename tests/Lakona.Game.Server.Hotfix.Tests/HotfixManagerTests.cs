using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Rpc.Core;
using Xunit;
using ActorNameAttribute = Lakona.Game.Server.Actors.ActorNameAttribute;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixManagerTests
{
    [Fact]
    public async Task PublishCandidate_runs_publication_transaction_in_order()
    {
        var events = new List<string>();
        var participant = new RecordingPublicationParticipant(events);
        var manager = new HotfixManager(new FixedAssemblySource("unused"), participants: [participant]);
        var candidate = CreateRuntimeSnapshot("v2");

        var result = await manager.PublishCandidateAsync(
            candidate,
            CreateSnapshot("v2"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(["prepare", "activate", "commit", "dispose"], events);
        Assert.Equal("v2", manager.Current.Version);
    }

    [Fact]
    public async Task PublishCandidate_restores_previous_publication_and_rolls_back_on_activation_failure()
    {
        var events = new List<string>();
        var participant = new RecordingPublicationParticipant(events, failActivation: true);
        var manager = new HotfixManager(new FixedAssemblySource("unused"), participants: [participant]);

        var result = await manager.PublishCandidateAsync(
            CreateRuntimeSnapshot("v2"),
            CreateSnapshot("v2"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(["prepare", "activate", "rollback", "dispose"], events);
        Assert.Null(manager.Current.Version);
    }

    [Fact]
    public async Task PublishCandidate_does_not_roll_back_after_cleanup_commit_failure()
    {
        var events = new List<string>();
        var manager = new HotfixManager(
            new FixedAssemblySource("unused"),
            participants:
            [
                new RecordingPublicationParticipant(events),
                new RecordingPublicationParticipant(events, failCommit: true)
            ]);

        var result = await manager.PublishCandidateAsync(
            CreateRuntimeSnapshot("v2"),
            CreateSnapshot("v2"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("rollback", events);
        Assert.Equal("v2", manager.Current.Version);
    }

    [Fact]
    public async Task Validate_runs_publication_candidate_validation_without_preparing()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(
            TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var events = new List<string>();
        var participant = new RecordingPublicationParticipant(events);
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.ManagerTestHotfixAssemblyPath),
            [stableAssembly.GetName().Name!],
            participants: [participant]);

        var result = await manager.ValidateAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(["validate"], events);
    }

    private static HotfixRuntimeSnapshot CreateRuntimeSnapshot(string version) => new(
        new HotfixServiceInvoker(new HotfixDispatchTable(1, [])),
        new ServiceCollection().BuildServiceProvider(),
        actorStartups: [],
        sourceVersion: version);

    private static HotfixSnapshot CreateSnapshot(string version) => new(
        version, null, DateTimeOffset.UtcNow, 1, [], HotfixReloadStatus.Succeeded, null, null);

    private sealed class RecordingPublicationParticipant(
        List<string> events,
        bool failActivation = false,
        bool failCommit = false)
        : IHotfixRuntimePublicationParticipant
    {
        public ValueTask ValidateAsync(
            HotfixRuntimeSnapshot previous,
            HotfixRuntimeSnapshot candidate,
            CancellationToken cancellationToken = default)
        {
            events.Add("validate");
            return default;
        }

        public ValueTask<IHotfixRuntimePublicationTransaction> PrepareAsync(
            HotfixRuntimeSnapshot previous,
            HotfixRuntimeSnapshot candidate,
            CancellationToken cancellationToken = default)
        {
            events.Add("prepare");
            return ValueTask.FromResult<IHotfixRuntimePublicationTransaction>(new Transaction(events, failActivation, failCommit));
        }

        private sealed class Transaction(List<string> events, bool failActivation, bool failCommit)
            : IHotfixRuntimePublicationTransaction
        {
            public ValueTask ActivateAsync(CancellationToken cancellationToken = default)
            {
                events.Add("activate");
                if (failActivation) throw new InvalidOperationException("activation failed");
                return default;
            }
            public ValueTask CommitAsync(CancellationToken cancellationToken = default)
            {
                events.Add("commit");
                if (failCommit) throw new InvalidOperationException("cleanup failed");
                return default;
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) { events.Add("rollback"); return default; }
            public ValueTask DisposeAsync() { events.Add("dispose"); return default; }
        }
    }

    [Theory]
    [InlineData("FooActor", "foo")]
    [InlineData("Foo", "foo")]
    [InlineData("Actor", "actor")]
    public void Actor_name_default_convention_is_stable(string typeName, string expected)
    {
        Assert.Equal(expected, ActorNameConventions.ResolveDefault(typeName));
    }

    [Fact]
    public void Actor_host_descriptors_use_explicit_actor_wire_name()
    {
        var scan = new HotfixBehaviorScanResult(
            [],
            [],
            [],
            [],
            [ActorPlacementDeclaration.Create<BattleRoomActor, string>(static context => context.Candidates[0])],
            [],
            [],
            [],
            []);
        var method = typeof(HotfixManager).GetMethod(
            "CreateActorHostDescriptors",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var descriptors = Assert.IsAssignableFrom<IReadOnlyList<HotfixActorHostDescriptor>>(
            method.Invoke(null, [scan, "test-build"]));

        var descriptor = Assert.Single(descriptors);
        Assert.Equal("battle-room", descriptor.Actor);
    }

    [Fact]
    public void Startup_actor_host_descriptor_uses_typed_actor_and_key_identity()
    {
        var scan = new HotfixBehaviorScanResult(
            [],
            [],
            [],
            [ActorStartupDeclaration.Create<BattleRoomActor, string>(static context => context.Candidates[0])],
            [],
            [],
            [],
            [],
            []);
        var method = typeof(HotfixManager).GetMethod(
            "CreateActorHostDescriptors",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var descriptors = Assert.IsAssignableFrom<IReadOnlyList<HotfixActorHostDescriptor>>(
            method.Invoke(null, [scan, "test-build"]));

        var descriptor = Assert.Single(descriptors);
        Assert.Equal("battle-room", descriptor.Actor);
        Assert.Equal(
            $"startup:v1:{typeof(BattleRoomActor).FullName}:{typeof(string).FullName}",
            descriptor.PolicyHash);
        Assert.Equal("test-build", descriptor.BuildTag);
    }

    [Fact]
    public async Task Reload_fails_when_state_type_is_loaded_from_hotfix_context()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var hotfixDir = Path.GetDirectoryName(compiled.HotfixAssemblyPath)!;
        var stableName = Path.GetFileName(compiled.StableAssemblyPath);
        var localStablePath = Path.Combine(hotfixDir, stableName);
        File.Copy(compiled.StableAssemblyPath, localStablePath, overwrite: true);
        var manager = new HotfixManager(new FixedAssemblySource(compiled.HotfixAssemblyPath));

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("must resolve from a shared AssemblyLoadContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reload_replaces_current_snapshot_after_successful_scan()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new FixedAssemblySource(compiled.ManagerTestHotfixAssemblyPath);
        var manager = new HotfixManager(source, [stableAssembly.GetName().Name!]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(1, result.Current.DispatchTableVersion);
        Assert.Equal(result.Current.DispatchTableVersion, HotfixDispatch.Current.Version);
        Assert.Contains(result.Current.Methods, key => key.MethodName == "Add");
    }

    [Fact]
    public async Task Reload_success_writes_framework_information_log()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var loggerProvider = new RecordingLoggerProvider();
        await using var rootServices = new ServiceCollection()
            .AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddProvider(loggerProvider);
            })
            .BuildServiceProvider();
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.ManagerTestHotfixAssemblyPath),
            [stableAssembly.GetName().Name!],
            rootServices: rootServices);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Category == "Lakona.Game.Hotfix" &&
            entry.Level == LogLevel.Information &&
            entry.Message.Contains("Hotfix reload succeeded", StringComparison.Ordinal) &&
            entry.Message.Contains(compiled.ManagerTestHotfixAssemblyPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reload_failure_writes_framework_error_log()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new SwitchableAssemblySource(compiled.ManagerTestHotfixAssemblyPath);
        var loggerProvider = new RecordingLoggerProvider();
        await using var rootServices = new ServiceCollection()
            .AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddProvider(loggerProvider);
            })
            .BuildServiceProvider();
        var manager = new HotfixManager(
            source,
            [stableAssembly.GetName().Name!],
            rootServices: rootServices);
        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        source.Path = @"Z:\missing\Missing.Hotfix.dll";

        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(second.Succeeded);
        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Category == "Lakona.Game.Hotfix" &&
            entry.Level == LogLevel.Error &&
            entry.Message.Contains("Hotfix reload failed", StringComparison.Ordinal) &&
            entry.Message.Contains(source.Path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reload_failure_keeps_previous_snapshot()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new SwitchableAssemblySource(compiled.ManagerTestHotfixAssemblyPath);
        var manager = new HotfixManager(source, [stableAssembly.GetName().Name!]);
        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        var previousRuntime = ((IHotfixRuntimeAccessor)manager).Current;
        source.Path = @"Z:\missing\Missing.Hotfix.dll";

        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Same(previousRuntime, ((IHotfixRuntimeAccessor)manager).Current);
        Assert.Equal(HotfixReloadStatus.Failed, manager.Current.LastReloadStatus);
        Assert.Equal(first.Current.DispatchTableVersion, second.Current.DispatchTableVersion);
        Assert.Equal(first.Current.SourcePath, manager.Current.SourcePath);
        Assert.NotEmpty(manager.Current.Methods);
    }

    [Fact]
    public async Task Reload_publishes_hotfix_service_provider_with_dispatch_table()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new SwitchableAssemblySource(compiled.ManagerTestHotfixAssemblyPath);
        var manager = new HotfixManager(source, [
            stableAssembly.GetName().Name!,
            typeof(IGenerationMarker).Assembly.GetName().Name!
        ]);
        var accessor = Assert.IsAssignableFrom<IHotfixServiceProviderAccessor>(manager);

        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.Equal("one", accessor.Current.GetRequiredService<IGenerationMarker>().Generation);

        source.Path = compiled.SecondHotfixAssemblyPath;
        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Diagnostics));
        Assert.Equal("two", accessor.Current.GetRequiredService<IGenerationMarker>().Generation);

        source.Path = compiled.InvalidHotfixAssemblyPath;
        var failed = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(failed.Succeeded);
        Assert.Equal("two", accessor.Current.GetRequiredService<IGenerationMarker>().Generation);
    }

    [Fact]
    public async Task ServiceProviderAccessor_uses_leased_snapshot_services_inside_dispatch_scope()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new SwitchableAssemblySource(compiled.ManagerTestHotfixAssemblyPath);
        var manager = new HotfixManager(source, [
            stableAssembly.GetName().Name!,
            typeof(IGenerationMarker).Assembly.GetName().Name!
        ]);
        var servicesAccessor = Assert.IsAssignableFrom<IHotfixServiceProviderAccessor>(manager);
        var runtimeAccessor = Assert.IsAssignableFrom<IHotfixRuntimeAccessor>(manager);
        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        using var lease = runtimeAccessor.AcquireCurrent();

        source.Path = compiled.SecondHotfixAssemblyPath;
        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Diagnostics));
        Assert.Equal("one", servicesAccessor.Current.GetRequiredService<IGenerationMarker>().Generation);
    }

    [Fact]
    public async Task ServiceProviderAccessor_uses_volatile_current_services_outside_dispatch_scope()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new SwitchableAssemblySource(compiled.ManagerTestHotfixAssemblyPath);
        var manager = new HotfixManager(source, [
            stableAssembly.GetName().Name!,
            typeof(IGenerationMarker).Assembly.GetName().Name!
        ]);
        var servicesAccessor = Assert.IsAssignableFrom<IHotfixServiceProviderAccessor>(manager);
        var runtimeAccessor = Assert.IsAssignableFrom<IHotfixRuntimeAccessor>(manager);
        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));

        using (runtimeAccessor.AcquireCurrent())
        {
            source.Path = compiled.SecondHotfixAssemblyPath;
            var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);
            Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Diagnostics));
        }

        Assert.Equal("two", servicesAccessor.Current.GetRequiredService<IGenerationMarker>().Generation);
    }

    [Fact]
    public async Task ServiceProviderAccessor_uses_current_services_for_disposed_captured_dispatch_scope()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new SwitchableAssemblySource(compiled.ManagerTestHotfixAssemblyPath);
        var manager = new HotfixManager(source, [
            stableAssembly.GetName().Name!,
            typeof(IGenerationMarker).Assembly.GetName().Name!
        ]);
        var servicesAccessor = Assert.IsAssignableFrom<IHotfixServiceProviderAccessor>(manager);
        var runtimeAccessor = Assert.IsAssignableFrom<IHotfixRuntimeAccessor>(manager);
        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));

        var lease = runtimeAccessor.AcquireCurrent();
        var captured = ExecutionContext.Capture();
        HotfixDispatchRuntimeContext? capturedContext = null;
        ExecutionContext.Run(
            captured!,
            _ => capturedContext = HotfixDispatchRuntimeScope.Current,
            null);
        source.Path = compiled.SecondHotfixAssemblyPath;
        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Diagnostics));
        lease.Dispose();
        ForceStaleActiveFlag(capturedContext!);
        string? generation = null;

        ExecutionContext.Run(
            captured!,
            _ => generation = servicesAccessor.Current.GetRequiredService<IGenerationMarker>().Generation,
            null);

        Assert.Equal("two", generation);
    }

    [Fact]
    public async Task Reload_shares_configured_stable_assemblies_from_default_context()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new FixedAssemblySource(compiled.HotfixAssemblyPath);
        var manager = new HotfixManager(source, [stableAssembly.GetName().Name!]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var method = HotfixDispatch.Current.Resolve(result.Current.Methods.Single());
        Assert.Same(stableAssembly, method.GetParameters()[0].ParameterType.Assembly);
    }

    [Fact]
    public async Task Reload_uses_default_load_context_for_hotfix_abstractions()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.HotfixAssemblyPath),
            [stableAssembly.GetName().Name!]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var abstractionsAssembly = typeof(HotfixServiceAttribute).Assembly;
        var abstractionsName = abstractionsAssembly.GetName().Name;
        Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(abstractionsAssembly));
        Assert.DoesNotContain(
            AssemblyLoadContext.All.Where(context => !ReferenceEquals(context, AssemblyLoadContext.Default)),
            context => context.Assemblies.Any(assembly => string.Equals(assembly.GetName().Name, abstractionsName, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Reload_does_not_hold_source_dll_file_lock()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.HotfixAssemblyPath),
            [stableAssembly.GetName().Name!]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        await using var stream = new FileStream(
            compiled.HotfixAssemblyPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task Reload_does_not_hold_private_dependency_dll_file_lock()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.HotfixWithPrivateDependencyAssemblyPath),
            [stableAssembly.GetName().Name!]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var hotfixDir = Path.GetDirectoryName(compiled.HotfixWithPrivateDependencyAssemblyPath)!;
        var privateDepName = Path.GetFileName(compiled.PrivateDependencyAssemblyPath);
        var privateDepNextToHotfix = Path.Combine(hotfixDir, privateDepName);
        await using var hotfixStream = new FileStream(
            compiled.HotfixWithPrivateDependencyAssemblyPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        await using var helperStream = new FileStream(
            privateDepNextToHotfix,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        Assert.True(hotfixStream.CanWrite);
        Assert.True(helperStream.CanWrite);
    }

    [Fact]
    public async Task Reload_releases_previous_collectible_load_context_after_replacement()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new SwitchableAssemblySource(compiled.HotfixAssemblyPath);
        var manager = new HotfixManager(source, [stableAssembly.GetName().Name!]);

        var previousContext = await LoadFirstVersionAndCaptureContextAsync(manager, TestContext.Current.CancellationToken);
        source.Path = compiled.SecondHotfixAssemblyPath;

        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Diagnostics));
        await AssertLoadContextUnloadedAsync(previousContext, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reload_does_not_replace_dispatch_after_scan_failure()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new SwitchableAssemblySource(compiled.ManagerTestHotfixAssemblyPath);
        var manager = new HotfixManager(source, [stableAssembly.GetName().Name!]);
        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        var key = first.Current.Methods.Single(key =>
            key.StateTypeName == "StableContracts.ManagerTestState" && key.MethodName == "Add");
        var previousMethod = HotfixDispatch.Current.Resolve(key);
        source.Path = compiled.InvalidHotfixAssemblyPath;

        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(second.Succeeded);
        Assert.Equal(first.Current.DispatchTableVersion, second.Current.DispatchTableVersion);
        Assert.Same(previousMethod, HotfixDispatch.Current.Resolve(key));
    }

    [Fact]
    public async Task Reload_propagates_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var manager = new HotfixManager(new CanceledAssemblySource());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await manager.ReloadAsync(cts.Token));
    }

    [Fact]
    public async Task Reload_canceled_after_source_resolution_does_not_publish()
    {
        using var cts = new CancellationTokenSource();
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new SwitchableAssemblySource(compiled.ManagerTestHotfixAssemblyPath);
        var manager = new HotfixManager(source, [stableAssembly.GetName().Name!]);
        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        var key = first.Current.Methods.Single(key =>
            key.StateTypeName == "StableContracts.ManagerTestState" && key.MethodName == "Add");
        var previousMethod = HotfixDispatch.Current.Resolve(key);
        source.Path = compiled.ManagerTestHotfixAssemblyPath;
        source.AfterResolve = () => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await manager.ReloadAsync(cts.Token));

        Assert.Equal(first.Current.DispatchTableVersion, manager.Current.DispatchTableVersion);
        Assert.Same(previousMethod, HotfixDispatch.Current.Resolve(key));
    }

    [Fact]
    public async Task ReloadAsync_serializes_concurrent_reloads()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new BlockingAssemblySource(compiled.ManagerTestHotfixAssemblyPath);
        var manager = new HotfixManager(source, [stableAssembly.GetName().Name!]);
        var first = manager.ReloadAsync(TestContext.Current.CancellationToken).AsTask();
        await source.FirstResolveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        var second = manager.ReloadAsync(TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();

        Assert.Equal(1, source.ResolveStarts);
        source.AllowFirstResolve.SetResult();
        await first.WaitAsync(TestContext.Current.CancellationToken);
        await source.SecondResolveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        source.AllowSecondResolve.SetResult();
        await second.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void ValidateMethodShapes_rejects_binding_with_mismatched_parameter_count()
    {
        var key = HotfixDispatch.CreateKey<DispatchTestState, int>("NoArg");
        var binding = new HotfixMethodBinding(
            key,
            typeof(DispatchTestStateSystem).GetMethod(nameof(DispatchTestStateSystem.Add))!,
            typeof(DispatchTestStateSystem),
            typeof(DispatchTestState),
            typeof(int),
            []);
        var table = new HotfixDispatchTable(1, [binding]);

        var ex = Assert.Throws<InvalidOperationException>(() => table.ValidateMethodShapes());

        Assert.Contains("parameter count", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"../Hotfix.dll")]
    [InlineData(@"nested/Hotfix.dll")]
    [InlineData(@"/tmp/Hotfix.dll")]
    public async Task CurrentDirectorySource_rejects_unsafe_assembly_file_names(string assemblyFileName)
    {
        var source = new CurrentDirectoryHotfixAssemblySource(Environment.CurrentDirectory, assemblyFileName);

        await Assert.ThrowsAsync<ArgumentException>(async () => await source.ResolveAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(@"../current.txt", "Hotfix.dll")]
    [InlineData(@"nested/current.txt", "Hotfix.dll")]
    [InlineData("current.txt", @"../Hotfix.dll")]
    [InlineData("current.txt", @"nested/Hotfix.dll")]
    [InlineData("current.txt", @"/tmp/Hotfix.dll")]
    public async Task VersionPointerSource_rejects_unsafe_file_names(string pointerFileName, string assemblyFileName)
    {
        var source = new VersionPointerHotfixAssemblySource(Environment.CurrentDirectory, pointerFileName, assemblyFileName);

        await Assert.ThrowsAsync<ArgumentException>(async () => await source.ResolveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AddLakonaGameHotfix_replaces_existing_source_registration()
    {
        var dummy = Path.Combine(AppContext.BaseDirectory, "dummy.dll");
        var oldSource = new FixedAssemblySource(dummy);
        var newSource = new FixedAssemblySource(dummy);
        var services = new ServiceCollection();
        services.AddSingleton<IHotfixAssemblySource>(oldSource);

        services.AddLakonaGameHotfix(newSource);

        using var provider = services.BuildServiceProvider();
        Assert.Same(newSource, provider.GetRequiredService<IHotfixAssemblySource>());
    }

    [Fact]
    public async Task Reload_fails_when_state_type_is_defined_in_hotfix_assembly()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var manager = new HotfixManager(new FixedAssemblySource(compiled.HotfixOwnedStateAssemblyPath));

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("must resolve from a shared AssemblyLoadContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reload_fails_when_return_type_is_defined_in_hotfix_assembly()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.HotfixOwnedReturnAssemblyPath),
            [stableAssembly.GetName().Name!]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("must resolve from a shared AssemblyLoadContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reload_fails_when_argument_type_is_defined_in_hotfix_assembly()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.HotfixOwnedArgumentAssemblyPath),
            [stableAssembly.GetName().Name!]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("must resolve from a shared AssemblyLoadContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reload_fails_when_service_return_type_is_defined_in_hotfix_assembly()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.HotfixOwnedServiceReturnAssemblyPath),
            [stableAssembly.GetName().Name!]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("must return", StringComparison.Ordinal)
                && diagnostic.Contains("to match contract method", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reload_fails_when_service_argument_type_is_defined_in_hotfix_assembly()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.HotfixOwnedServiceArgumentAssemblyPath),
            [stableAssembly.GetName().Name!]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("does not match a method on contract", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reload_publishes_valid_service_using_stable_boundary_types()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var requestType = stableAssembly.GetType("StableContracts.ServiceRequest", throwOnError: true)!;
        var replyType = stableAssembly.GetType("StableContracts.ServiceReply", throwOnError: true)!;
        var contractType = stableAssembly.GetType("StableContracts.IManagerService", throwOnError: true)!;
        var request = Activator.CreateInstance(requestType, 41)!;
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.ValidServiceHotfixAssemblyPath),
            [stableAssembly.GetName().Name!]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var callType = typeof(HotfixServiceCall<>).MakeGenericType(requestType);
        var call = Activator.CreateInstance(
            callType,
            request,
            ((IHotfixServiceProviderAccessor)manager).Current)!;
        var invoke = typeof(HotfixDispatchTable)
            .GetMethods()
            .Single(method => method.Name == nameof(HotfixDispatchTable.InvokeServiceAsync)
                && method.GetGenericArguments().Length == 3
                && method.GetParameters()[0].ParameterType == typeof(int))
            .MakeGenericMethod(contractType, callType, replyType);
        var task = invoke.Invoke(HotfixDispatch.Current, [7, call])!;
        var reply = await ((ValueTask<object?>)typeof(HotfixManagerTests)
            .GetMethod(nameof(AwaitValueTaskAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(replyType)
            .Invoke(null, [task])!);
        Assert.NotNull(reply);
        var value = (int)replyType.GetProperty("Value")!.GetValue(reply)!;
        Assert.Equal(42, value);
    }

    [Fact]
    public async Task Reload_validates_generation_local_constructor_dependencies_before_publish()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.ConstructorDiServiceHotfixAssemblyPath),
            [stableAssembly.GetName().Name!, typeof(IGenerationMarker).Assembly.GetName().Name!],
            requiredServiceContracts: [stableAssembly.GetType("StableContracts.IManagerService", throwOnError: true)!]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public async Task Reload_fails_before_publish_when_constructor_dependency_is_missing()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var firstSource = new SwitchableAssemblySource(compiled.ValidServiceHotfixAssemblyPath);
        var contract = stableAssembly.GetType("StableContracts.IManagerService", throwOnError: true)!;
        var manager = new HotfixManager(
            firstSource,
            [stableAssembly.GetName().Name!],
            requiredServiceContracts: [contract]);
        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        firstSource.Path = compiled.MissingDiServiceHotfixAssemblyPath;

        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(first.Current.DispatchTableVersion, manager.Current.DispatchTableVersion);
        Assert.Contains(second.Diagnostics, diagnostic =>
            diagnostic.Contains("constructor activation failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reload_validates_lifecycle_constructor_dependencies_before_publish()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var contract = stableAssembly.GetType("StableContracts.IManagerLifecycle", throwOnError: true)!;
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.ConstructorDiLifecycleHotfixAssemblyPath),
            [stableAssembly.GetName().Name!, typeof(IGenerationMarker).Assembly.GetName().Name!],
            requiredServiceContracts: [contract]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public async Task Reload_validates_root_constructor_dependencies_before_publish()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var contract = stableAssembly.GetType("StableContracts.IManagerService", throwOnError: true)!;
        await using var rootServices = new ServiceCollection()
            .AddSingleton<IRootOnlyMarker, RootOnlyMarker>()
            .BuildServiceProvider();
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.RootDiServiceHotfixAssemblyPath),
            [stableAssembly.GetName().Name!, typeof(IRootOnlyMarker).Assembly.GetName().Name!],
            requiredServiceContracts: [contract],
            rootServices: rootServices);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public async Task Runtime_services_resolve_hotfix_local_dependencies_from_root_provider()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var requestType = stableAssembly.GetType("StableContracts.ServiceRequest", throwOnError: true)!;
        var replyType = stableAssembly.GetType("StableContracts.ServiceReply", throwOnError: true)!;
        var contract = stableAssembly.GetType("StableContracts.IManagerService", throwOnError: true)!;
        await using var rootServices = new ServiceCollection()
            .AddSingleton<IRootOnlyMarker, RootOnlyMarker>()
            .BuildServiceProvider();
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.RootDiLocalServiceHotfixAssemblyPath),
            [stableAssembly.GetName().Name!, typeof(IRootOnlyMarker).Assembly.GetName().Name!],
            requiredServiceContracts: [contract],
            rootServices: rootServices);
        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));

        var request = Activator.CreateInstance(requestType, 7)!;
        var callType = typeof(HotfixServiceCall<>).MakeGenericType(requestType);
        var call = Activator.CreateInstance(
            callType,
            request,
            ((IHotfixServiceProviderAccessor)manager).Current)!;
        var invoke = typeof(HotfixDispatchTable)
            .GetMethods()
            .Single(method => method.Name == nameof(HotfixDispatchTable.InvokeServiceAsync)
                && method.GetGenericArguments().Length == 3
                && method.GetParameters()[0].ParameterType == typeof(int))
            .MakeGenericMethod(contract, callType, replyType);
        var task = invoke.Invoke(HotfixDispatch.Current, [7, call])!;

        var reply = await ((ValueTask<object?>)typeof(HotfixManagerTests)
            .GetMethod(nameof(AwaitValueTaskAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(replyType)
            .Invoke(null, [task])!);

        Assert.NotNull(reply);
        var value = (int)replyType.GetProperty("Value")!.GetValue(reply)!;
        Assert.Equal(11, value);
    }

    [Fact]
    public void BuildHotfixProvider_discovers_generated_service_registration_from_hotfix_assembly()
    {
        using var rootServices = new ServiceCollection()
            .AddSingleton<IRootOnlyMarker, RootOnlyMarker>()
            .BuildServiceProvider();
        var manager = new HotfixManager(new FixedAssemblySource("unused"), rootServices: rootServices);
        var services = BuildHotfixProvider(
            manager,
            typeof(GeneratedHotfixServiceRegistrationForTest).Assembly);
        try
        {
            Assert.IsType<GeneratedHotfixRegistrationMarker>(
                services.GetRequiredService<IHotfixGeneratedRegistrationMarker>());
            var consumer = services.GetRequiredService<HotfixGeneratedRegistrationConsumer>();
            Assert.Equal("root", consumer.Root.Value);
        }
        finally
        {
            (services as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void BuildHotfixProvider_includes_startup_service_registrations()
    {
        var manager = new HotfixManager(new FixedAssemblySource("unused"));
        var services = BuildHotfixProvider(
            manager,
            [ServiceDescriptor.Singleton<IStartupConfiguredMarker, StartupConfiguredMarker>()],
            typeof(HotfixManagerTests).Assembly);
        try
        {
            Assert.IsType<StartupConfiguredMarker>(
                services.GetRequiredService<IStartupConfiguredMarker>());
        }
        finally
        {
            (services as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task Reload_fails_before_publish_when_service_has_multiple_unmarked_constructors()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var contract = stableAssembly.GetType("StableContracts.IManagerService", throwOnError: true)!;
        await using var rootServices = new ServiceCollection()
            .AddSingleton<IRootOnlyMarker, RootOnlyMarker>()
            .BuildServiceProvider();
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.MultipleConstructorServiceHotfixAssemblyPath),
            [stableAssembly.GetName().Name!, typeof(IGenerationMarker).Assembly.GetName().Name!, typeof(IRootOnlyMarker).Assembly.GetName().Name!],
            requiredServiceContracts: [contract],
            rootServices: rootServices);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("multiple public constructors", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reload_uses_activator_utilities_constructor_attribute()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var contract = stableAssembly.GetType("StableContracts.IManagerService", throwOnError: true)!;
        var manager = new HotfixManager(
            new FixedAssemblySource(compiled.SelectedConstructorDiServiceHotfixAssemblyPath),
            [stableAssembly.GetName().Name!, typeof(IGenerationMarker).Assembly.GetName().Name!],
            requiredServiceContracts: [contract]);

        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public async Task AddLakonaGameHotfix_second_call_rebuilds_manager_with_latest_source_and_shared_policy()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var services = new ServiceCollection();
        services.AddLakonaGameHotfix(new FixedAssemblySource(@"Z:\missing\Missing.Hotfix.dll"), ["MissingStableContracts"]);
        services.AddLakonaGameHotfix(new FixedAssemblySource(compiled.HotfixAssemblyPath), [stableAssembly.GetName().Name!]);

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IHotfixManager>();
        var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var method = HotfixDispatch.Current.Resolve(result.Current.Methods.Single());
        Assert.Same(stableAssembly, method.GetParameters()[0].ParameterType.Assembly);
    }

    private static async Task<WeakReference> LoadFirstVersionAndCaptureContextAsync(
        HotfixManager manager,
        CancellationToken cancellationToken)
    {
        var first = await manager.ReloadAsync(cancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));

        var method = HotfixDispatch.Current.Resolve(first.Current.Methods.Single());
        var loadContext = AssemblyLoadContext.GetLoadContext(method.Module.Assembly);
        Assert.NotNull(loadContext);
        Assert.True(loadContext.IsCollectible);

        return new WeakReference(loadContext);
    }

    [ActorName("battle-room")]
    private sealed class BattleRoomActor;

    private static async Task AssertLoadContextUnloadedAsync(WeakReference loadContextReference, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!loadContextReference.IsAlive)
            {
                return;
            }

            await Task.Delay(25, cancellationToken);
        }

        Assert.False(loadContextReference.IsAlive, "Previous hotfix AssemblyLoadContext should be collectible after a successful replacement reload.");
    }

    private static IServiceProvider BuildHotfixProvider(
        HotfixManager manager,
        Assembly hotfixAssembly)
    {
        return BuildHotfixProvider(manager, [], hotfixAssembly);
    }

    private static IServiceProvider BuildHotfixProvider(
        HotfixManager manager,
        IReadOnlyList<ServiceDescriptor> startupServices,
        Assembly hotfixAssembly)
    {
        return (IServiceProvider)typeof(HotfixManager)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == "BuildHotfixProvider"
                && method.GetParameters().Length == 2)
            .Invoke(manager, [startupServices, hotfixAssembly])!;
    }

    private static void ForceStaleActiveFlag(HotfixDispatchRuntimeContext context)
    {
        var field = typeof(HotfixDispatchRuntimeContext).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? typeof(HotfixDispatchRuntimeContext).GetField(
                "<IsActive>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(context, field.FieldType == typeof(int) ? 1 : true);
    }

    private sealed class FixedAssemblySource : IHotfixAssemblySource
    {
        private readonly string _path;

        public FixedAssemblySource(string path)
        {
            _path = path;
        }

        public ValueTask<HotfixAssemblySourceResult> ResolveAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new HotfixAssemblySourceResult(
                "test",
                _path,
                Path.GetDirectoryName(_path)!));
        }
    }

    private interface IStartupConfiguredMarker
    {
    }

    private sealed class StartupConfiguredMarker : IStartupConfiguredMarker
    {
    }

    private sealed class SwitchableAssemblySource : IHotfixAssemblySource
    {
        public SwitchableAssemblySource(string path)
        {
            Path = path;
        }

        public string Path { get; set; }

        public string AssemblyPath
        {
            get => Path;
            set => Path = value;
        }

        public Action? AfterResolve { get; set; }

        public ValueTask<HotfixAssemblySourceResult> ResolveAsync(CancellationToken cancellationToken = default)
        {
            var result = new HotfixAssemblySourceResult(
                "test",
                Path,
                System.IO.Path.GetDirectoryName(Path) ?? Environment.CurrentDirectory);
            AfterResolve?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CanceledAssemblySource : IHotfixAssemblySource
    {
        public ValueTask<HotfixAssemblySourceResult> ResolveAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromCanceled<HotfixAssemblySourceResult>(cancellationToken);
        }
    }

    private sealed class BlockingAssemblySource : IHotfixAssemblySource
    {
        private readonly string _path;
        private int _resolveStarts;

        public BlockingAssemblySource(string path)
        {
            _path = path;
        }

        public int ResolveStarts => Volatile.Read(ref _resolveStarts);

        public TaskCompletionSource FirstResolveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondResolveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowFirstResolve { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowSecondResolve { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<HotfixAssemblySourceResult> ResolveAsync(CancellationToken cancellationToken = default)
        {
            var start = Interlocked.Increment(ref _resolveStarts);
            if (start == 1)
            {
                FirstResolveStarted.SetResult();
                await AllowFirstResolve.Task.WaitAsync(cancellationToken);
            }
            else
            {
                SecondResolveStarted.SetResult();
                await AllowSecondResolve.Task.WaitAsync(cancellationToken);
            }

            return new HotfixAssemblySourceResult(
                start.ToString(),
                _path,
                Path.GetDirectoryName(_path)!);
        }
    }

    private sealed class CompiledHotfixFixture : IDisposable
    {
        private CompiledHotfixFixture(
            string rootDirectory,
            string stableAssemblyPath,
            string hotfixAssemblyPath,
            string secondHotfixAssemblyPath,
            string invalidHotfixAssemblyPath,
            string hotfixOwnedStateAssemblyPath,
            string hotfixOwnedReturnAssemblyPath,
            string hotfixOwnedArgumentAssemblyPath,
            string managerTestHotfixAssemblyPath,
            string privateDependencyAssemblyPath,
            string hotfixWithPrivateDependencyAssemblyPath,
            string hotfixOwnedServiceReturnAssemblyPath,
            string hotfixOwnedServiceArgumentAssemblyPath,
            string validServiceHotfixAssemblyPath,
            string constructorDiServiceHotfixAssemblyPath,
            string constructorDiLifecycleHotfixAssemblyPath,
            string rootDiServiceHotfixAssemblyPath,
            string rootDiLocalServiceHotfixAssemblyPath,
            string missingDiServiceHotfixAssemblyPath,
            string multipleConstructorServiceHotfixAssemblyPath,
            string selectedConstructorDiServiceHotfixAssemblyPath)
        {
            RootDirectory = rootDirectory;
            StableAssemblyPath = stableAssemblyPath;
            HotfixAssemblyPath = hotfixAssemblyPath;
            SecondHotfixAssemblyPath = secondHotfixAssemblyPath;
            InvalidHotfixAssemblyPath = invalidHotfixAssemblyPath;
            HotfixOwnedStateAssemblyPath = hotfixOwnedStateAssemblyPath;
            HotfixOwnedReturnAssemblyPath = hotfixOwnedReturnAssemblyPath;
            HotfixOwnedArgumentAssemblyPath = hotfixOwnedArgumentAssemblyPath;
            ManagerTestHotfixAssemblyPath = managerTestHotfixAssemblyPath;
            PrivateDependencyAssemblyPath = privateDependencyAssemblyPath;
            HotfixWithPrivateDependencyAssemblyPath = hotfixWithPrivateDependencyAssemblyPath;
            HotfixOwnedServiceReturnAssemblyPath = hotfixOwnedServiceReturnAssemblyPath;
            HotfixOwnedServiceArgumentAssemblyPath = hotfixOwnedServiceArgumentAssemblyPath;
            ValidServiceHotfixAssemblyPath = validServiceHotfixAssemblyPath;
            ConstructorDiServiceHotfixAssemblyPath = constructorDiServiceHotfixAssemblyPath;
            ConstructorDiLifecycleHotfixAssemblyPath = constructorDiLifecycleHotfixAssemblyPath;
            RootDiServiceHotfixAssemblyPath = rootDiServiceHotfixAssemblyPath;
            RootDiLocalServiceHotfixAssemblyPath = rootDiLocalServiceHotfixAssemblyPath;
            MissingDiServiceHotfixAssemblyPath = missingDiServiceHotfixAssemblyPath;
            MultipleConstructorServiceHotfixAssemblyPath = multipleConstructorServiceHotfixAssemblyPath;
            SelectedConstructorDiServiceHotfixAssemblyPath = selectedConstructorDiServiceHotfixAssemblyPath;
        }

        public string RootDirectory { get; }

        public string StableAssemblyPath { get; }

        public string HotfixAssemblyPath { get; }

        public string SecondHotfixAssemblyPath { get; }

        public string InvalidHotfixAssemblyPath { get; }

        public string HotfixOwnedStateAssemblyPath { get; }

        public string HotfixOwnedReturnAssemblyPath { get; }

        public string HotfixOwnedArgumentAssemblyPath { get; }

        public string ManagerTestHotfixAssemblyPath { get; }

        public string PrivateDependencyAssemblyPath { get; }

        public string HotfixWithPrivateDependencyAssemblyPath { get; }

        public string HotfixOwnedServiceReturnAssemblyPath { get; }

        public string HotfixOwnedServiceArgumentAssemblyPath { get; }

        public string ValidServiceHotfixAssemblyPath { get; }

        public string ConstructorDiServiceHotfixAssemblyPath { get; }

        public string ConstructorDiLifecycleHotfixAssemblyPath { get; }

        public string RootDiServiceHotfixAssemblyPath { get; }

        public string RootDiLocalServiceHotfixAssemblyPath { get; }

        public string MissingDiServiceHotfixAssemblyPath { get; }

        public string MultipleConstructorServiceHotfixAssemblyPath { get; }

        public string SelectedConstructorDiServiceHotfixAssemblyPath { get; }

        public static async Task<CompiledHotfixFixture> CreateAsync(CancellationToken cancellationToken)
        {
            var root = Path.Combine(Path.GetTempPath(), "LakonaGameHotfixTests", Guid.NewGuid().ToString("N"));
            var suffix = Guid.NewGuid().ToString("N");
            var stableAssemblyName = $"StableContracts_{suffix}";
            var hotfixAssemblyName = $"HotfixLogic_{suffix}";
            var secondHotfixAssemblyName = $"HotfixLogicV2_{suffix}";
            var invalidAssemblyName = $"InvalidHotfixLogic_{suffix}";
            var stableAssemblyPath = Path.Combine(root, "stable", $"{stableAssemblyName}.dll");
            var hotfixAssemblyPath = Path.Combine(root, "hotfix", $"{hotfixAssemblyName}.dll");
            var secondHotfixAssemblyPath = Path.Combine(root, "hotfix-v2", $"{secondHotfixAssemblyName}.dll");
            var invalidAssemblyPath = Path.Combine(root, "invalid", $"{invalidAssemblyName}.dll");
            var hotfixOwnedStateAssemblyName = $"HotfixOwnedState_{suffix}";
            var hotfixOwnedReturnAssemblyName = $"HotfixOwnedReturn_{suffix}";
            var hotfixOwnedArgumentAssemblyName = $"HotfixOwnedArgument_{suffix}";
            var managerTestHotfixAssemblyName = $"ManagerTestHotfix_{suffix}";
            var hotfixOwnedStateAssemblyPath = Path.Combine(root, "hotfix-owned-state", $"{hotfixOwnedStateAssemblyName}.dll");
            var hotfixOwnedReturnAssemblyPath = Path.Combine(root, "hotfix-owned-return", $"{hotfixOwnedReturnAssemblyName}.dll");
            var hotfixOwnedArgumentAssemblyPath = Path.Combine(root, "hotfix-owned-argument", $"{hotfixOwnedArgumentAssemblyName}.dll");
            var managerTestHotfixAssemblyPath = Path.Combine(root, "manager-test-hotfix", $"{managerTestHotfixAssemblyName}.dll");
            var privateDependencyAssemblyName = $"PrivateHelper_{suffix}";
            var hotfixWithPrivateDependencyAssemblyName = $"HotfixWithPrivateDep_{suffix}";
            var privateDependencyAssemblyPath = Path.Combine(root, "private-dep", $"{privateDependencyAssemblyName}.dll");
            var hotfixWithPrivateDependencyAssemblyPath = Path.Combine(root, "hotfix-with-dep", $"{hotfixWithPrivateDependencyAssemblyName}.dll");
            var hotfixOwnedServiceReturnAssemblyName = $"HotfixOwnedServiceReturn_{suffix}";
            var hotfixOwnedServiceArgumentAssemblyName = $"HotfixOwnedServiceArgument_{suffix}";
            var validServiceHotfixAssemblyName = $"ValidServiceHotfix_{suffix}";
            var constructorDiServiceHotfixAssemblyName = $"ConstructorDiServiceHotfix_{suffix}";
            var constructorDiLifecycleHotfixAssemblyName = $"ConstructorDiLifecycleHotfix_{suffix}";
            var rootDiServiceHotfixAssemblyName = $"RootDiServiceHotfix_{suffix}";
            var rootDiLocalServiceHotfixAssemblyName = $"RootDiLocalServiceHotfix_{suffix}";
            var missingDiServiceHotfixAssemblyName = $"MissingDiServiceHotfix_{suffix}";
            var multipleConstructorServiceHotfixAssemblyName = $"MultipleConstructorServiceHotfix_{suffix}";
            var selectedConstructorDiServiceHotfixAssemblyName = $"SelectedConstructorDiServiceHotfix_{suffix}";
            var hotfixOwnedServiceReturnAssemblyPath = Path.Combine(root, "hotfix-owned-service-return", $"{hotfixOwnedServiceReturnAssemblyName}.dll");
            var hotfixOwnedServiceArgumentAssemblyPath = Path.Combine(root, "hotfix-owned-service-argument", $"{hotfixOwnedServiceArgumentAssemblyName}.dll");
            var validServiceHotfixAssemblyPath = Path.Combine(root, "valid-service-hotfix", $"{validServiceHotfixAssemblyName}.dll");
            var constructorDiServiceHotfixAssemblyPath = Path.Combine(root, "constructor-di-service-hotfix", $"{constructorDiServiceHotfixAssemblyName}.dll");
            var constructorDiLifecycleHotfixAssemblyPath = Path.Combine(root, "constructor-di-lifecycle-hotfix", $"{constructorDiLifecycleHotfixAssemblyName}.dll");
            var rootDiServiceHotfixAssemblyPath = Path.Combine(root, "root-di-service-hotfix", $"{rootDiServiceHotfixAssemblyName}.dll");
            var rootDiLocalServiceHotfixAssemblyPath = Path.Combine(root, "root-di-local-service-hotfix", $"{rootDiLocalServiceHotfixAssemblyName}.dll");
            var missingDiServiceHotfixAssemblyPath = Path.Combine(root, "missing-di-service-hotfix", $"{missingDiServiceHotfixAssemblyName}.dll");
            var multipleConstructorServiceHotfixAssemblyPath = Path.Combine(root, "multiple-constructor-service-hotfix", $"{multipleConstructorServiceHotfixAssemblyName}.dll");
            var selectedConstructorDiServiceHotfixAssemblyPath = Path.Combine(root, "selected-constructor-di-service-hotfix", $"{selectedConstructorDiServiceHotfixAssemblyName}.dll");

            Directory.CreateDirectory(Path.GetDirectoryName(stableAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(hotfixAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(secondHotfixAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(invalidAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(hotfixOwnedStateAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(hotfixOwnedReturnAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(hotfixOwnedArgumentAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(managerTestHotfixAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(privateDependencyAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(hotfixWithPrivateDependencyAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(hotfixOwnedServiceReturnAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(hotfixOwnedServiceArgumentAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(validServiceHotfixAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(constructorDiServiceHotfixAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(constructorDiLifecycleHotfixAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(rootDiServiceHotfixAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(rootDiLocalServiceHotfixAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(missingDiServiceHotfixAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(multipleConstructorServiceHotfixAssemblyPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(selectedConstructorDiServiceHotfixAssemblyPath)!);

            await EmitAssemblyAsync(
                stableAssemblyName,
                stableAssemblyPath,
                """
                namespace StableContracts;

                using System.Threading.Tasks;
                using Lakona.Rpc.Core;

                public sealed class ArenaSimulation
                {
                }

                public sealed class ManagerTestState
                {
                }

                public sealed record ServiceRequest(int Value);

                public sealed record ServiceReply(int Value);

                public sealed record LifecycleRequest(int Value);

                public interface IManagerService
                {
                    [RpcMethod(7)]
                    ValueTask<ServiceReply> LoginAsync(ServiceRequest request);
                }

                public interface IManagerLifecycle
                {
                    [RpcMethod(8)]
                    ValueTask ExpiredAsync(LifecycleRequest request);
                }
                """,
                [MetadataReference.CreateFromFile(typeof(RpcMethodAttribute).Assembly.Location)],
                cancellationToken);

            var stableReference = MetadataReference.CreateFromFile(stableAssemblyPath);
            var abstractionsReference = MetadataReference.CreateFromFile(typeof(HotfixBehaviorOfAttribute).Assembly.Location);
            var testsReference = MetadataReference.CreateFromFile(typeof(IGenerationMarker).Assembly.Location);
            var dependencyInjectionReference = MetadataReference.CreateFromFile(typeof(ServiceCollectionServiceExtensions).Assembly.Location);
            var dependencyInjectionAbstractionsReference = MetadataReference.CreateFromFile(typeof(ActivatorUtilitiesConstructorAttribute).Assembly.Location);

            await EmitAssemblyAsync(
                hotfixAssemblyName,
                hotfixAssemblyPath,
                $$"""
                using StableContracts;
                using Lakona.Game.Server.Hotfix.Abstractions;

                namespace HotfixLogic;

                [HotfixBehaviorOf(typeof(ArenaSimulation))]
                public sealed class ArenaSimulationSystem
                {
                    public int Tick(ArenaSimulation self, int delta)
                    {
                        return delta;
                    }
                }
                """,
                [stableReference, abstractionsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                secondHotfixAssemblyName,
                secondHotfixAssemblyPath,
                $$"""
                using StableContracts;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix.Tests;
                using Microsoft.Extensions.DependencyInjection;

                namespace HotfixLogicV2;

                [HotfixBehaviorOf(typeof(ArenaSimulation))]
                public sealed class ArenaSimulationSystem
                {
                    public int Tick(ArenaSimulation self, int delta)
                    {
                        return delta + 1;
                    }
                }

                public sealed class GenerationTwoMarker : IGenerationMarker
                {
                    public string Generation => "two";
                }

                [HotfixStartup]
                public static class HotfixStartup
                {
                    [HotfixConfigureServices]
                    public static void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IGenerationMarker, GenerationTwoMarker>();
                    }
                }
                """,
                [
                    stableReference,
                    abstractionsReference,
                    MetadataReference.CreateFromFile(typeof(IGenerationMarker).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(ServiceCollectionServiceExtensions).Assembly.Location)
                ],
                cancellationToken);

            await EmitAssemblyAsync(
                invalidAssemblyName,
                invalidAssemblyPath,
                $$"""
                using StableContracts;
                using Lakona.Game.Server.Hotfix.Abstractions;

                namespace InvalidHotfixLogic;

                [HotfixBehaviorOf(typeof(ArenaSimulation))]
                public sealed class ArenaSimulationSystem
                {
                    public bool TryRead(ArenaSimulation self, out int value)
                    {
                        value = 0;
                        return true;
                    }
                }
                """,
                [stableReference, abstractionsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                hotfixOwnedStateAssemblyName,
                hotfixOwnedStateAssemblyPath,
                """
                using Lakona.Game.Server.Hotfix.Abstractions;

                namespace HotfixOwnedState;

                public sealed class OwnedState
                {
                }

                [HotfixBehaviorOf(typeof(OwnedState))]
                public sealed class OwnedStateSystem
                {
                    public int Tick(OwnedState self)
                    {
                        return 1;
                    }
                }
                """,
                [abstractionsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                hotfixOwnedReturnAssemblyName,
                hotfixOwnedReturnAssemblyPath,
                """
                using StableContracts;
                using Lakona.Game.Server.Hotfix.Abstractions;

                namespace HotfixOwnedReturn;

                public sealed class OwnedResult
                {
                }

                [HotfixBehaviorOf(typeof(ArenaSimulation))]
                public sealed class ArenaSimulationSystem
                {
                    public OwnedResult Tick(ArenaSimulation self)
                    {
                        return new OwnedResult();
                    }
                }
                """,
                [stableReference, abstractionsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                hotfixOwnedArgumentAssemblyName,
                hotfixOwnedArgumentAssemblyPath,
                """
                using StableContracts;
                using Lakona.Game.Server.Hotfix.Abstractions;

                namespace HotfixOwnedArgument;

                public sealed class OwnedCommand
                {
                }

                [HotfixBehaviorOf(typeof(ArenaSimulation))]
                public sealed class ArenaSimulationSystem
                {
                    public int Tick(ArenaSimulation self, OwnedCommand command)
                    {
                        return 1;
                    }
                }
                """,
                [stableReference, abstractionsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                managerTestHotfixAssemblyName,
                managerTestHotfixAssemblyPath,
                """
                using StableContracts;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix.Tests;
                using Microsoft.Extensions.DependencyInjection;

                namespace HotfixLogic;

                [HotfixBehaviorOf(typeof(ManagerTestState))]
                public sealed class ManagerTestStateSystem
                {
                    public int Add(ManagerTestState state, int value)
                    {
                        return value;
                    }
                }

                public sealed class GenerationOneMarker : IGenerationMarker
                {
                    public string Generation => "one";
                }

                [HotfixStartup]
                public static class HotfixStartup
                {
                    [HotfixConfigureServices]
                    public static void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IGenerationMarker, GenerationOneMarker>();
                    }
                }
                """,
                [
                    stableReference,
                    abstractionsReference,
                    MetadataReference.CreateFromFile(typeof(IGenerationMarker).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(ServiceCollectionServiceExtensions).Assembly.Location)
                ],
                cancellationToken);

            await EmitAssemblyAsync(
                privateDependencyAssemblyName,
                privateDependencyAssemblyPath,
                """
                namespace PrivateHelper;

                public static class InternalHelper
                {
                    public static int Transform(int value)
                    {
                        return value + 1;
                    }
                }
                """,
                [],
                cancellationToken);

            var privateDepReference = MetadataReference.CreateFromFile(privateDependencyAssemblyPath);

            await EmitAssemblyAsync(
                hotfixWithPrivateDependencyAssemblyName,
                hotfixWithPrivateDependencyAssemblyPath,
                """
                using StableContracts;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using PrivateHelper;

                namespace HotfixWithPrivateDep;

                [HotfixBehaviorOf(typeof(ArenaSimulation))]
                public sealed class ArenaSimulationSystem
                {
                    public int Tick(ArenaSimulation self, int delta)
                    {
                        return InternalHelper.Transform(delta);
                    }
                }
                """,
                [stableReference, abstractionsReference, privateDepReference],
                cancellationToken);

            await EmitAssemblyAsync(
                hotfixOwnedServiceReturnAssemblyName,
                hotfixOwnedServiceReturnAssemblyPath,
                """
                using System.Threading.Tasks;
                using StableContracts;
                using Lakona.Game.Server.Hotfix;
                using Lakona.Game.Server.Hotfix.Abstractions;

                namespace HotfixOwnedServiceReturn;

                public sealed record OwnedReply(int Value);

                [HotfixService(typeof(IManagerService))]
                public sealed class ManagerService
                {
                    public ValueTask<OwnedReply> LoginAsync(HotfixServiceCall<ServiceRequest> call)
                    {
                        return new ValueTask<OwnedReply>(new OwnedReply(call.Request!.Value));
                    }
                }
                """,
                [stableReference, abstractionsReference, testsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                hotfixOwnedServiceArgumentAssemblyName,
                hotfixOwnedServiceArgumentAssemblyPath,
                """
                using System.Threading.Tasks;
                using StableContracts;
                using Lakona.Game.Server.Hotfix;
                using Lakona.Game.Server.Hotfix.Abstractions;

                namespace HotfixOwnedServiceArgument;

                public sealed record OwnedRequest(int Value);

                [HotfixService(typeof(IManagerService))]
                public sealed class ManagerService
                {
                    public ValueTask<ServiceReply> LoginAsync(HotfixServiceCall<OwnedRequest> call)
                    {
                        return new ValueTask<ServiceReply>(new ServiceReply(call.Request!.Value));
                    }
                }
                """,
                [stableReference, abstractionsReference, testsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                validServiceHotfixAssemblyName,
                validServiceHotfixAssemblyPath,
                """
                using System.Threading.Tasks;
                using StableContracts;
                using Lakona.Game.Server.Hotfix;
                using Lakona.Game.Server.Hotfix.Abstractions;

                namespace ValidServiceHotfix;

                [HotfixService(typeof(IManagerService))]
                public sealed class ManagerService
                {
                    public ValueTask<ServiceReply> LoginAsync(HotfixServiceCall<ServiceRequest> call)
                    {
                        return new ValueTask<ServiceReply>(new ServiceReply(call.Request!.Value + 1));
                    }
                }
                """,
                [stableReference, abstractionsReference, testsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                constructorDiServiceHotfixAssemblyName,
                constructorDiServiceHotfixAssemblyPath,
                """
                using System.Threading.Tasks;
                using StableContracts;
                using Lakona.Game.Server.Hotfix;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix.Tests;
                using Microsoft.Extensions.DependencyInjection;

                namespace ConstructorDiServiceHotfix;

                public sealed class LocalMarker : IGenerationMarker
                {
                    public string Generation => "constructor-di";
                }

                [HotfixStartup]
                public static class HotfixStartup
                {
                    [HotfixConfigureServices]
                    public static void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IGenerationMarker, LocalMarker>();
                    }
                }

                [HotfixService(typeof(IManagerService))]
                public sealed class ManagerService
                {
                    private readonly IGenerationMarker _marker;

                    public ManagerService(IGenerationMarker marker)
                    {
                        _marker = marker;
                    }

                    public ValueTask<ServiceReply> LoginAsync(HotfixServiceCall<ServiceRequest> call)
                    {
                        return new ValueTask<ServiceReply>(new ServiceReply(call.Request!.Value + _marker.Generation.Length));
                    }
                }
                """,
                [
                    stableReference,
                    abstractionsReference,
                    testsReference,
                    dependencyInjectionReference
                ],
                cancellationToken);

            await EmitAssemblyAsync(
                constructorDiLifecycleHotfixAssemblyName,
                constructorDiLifecycleHotfixAssemblyPath,
                """
                using System.Threading.Tasks;
                using StableContracts;
                using Lakona.Game.Server.Hotfix;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix.Tests;
                using Microsoft.Extensions.DependencyInjection;

                namespace ConstructorDiLifecycleHotfix;

                public sealed class LifecycleMarker : IGenerationMarker
                {
                    public string Generation => "lifecycle";
                }

                [HotfixStartup]
                public static class HotfixStartup
                {
                    [HotfixConfigureServices]
                    public static void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IGenerationMarker, LifecycleMarker>();
                    }
                }

                [HotfixLifecycle(typeof(IManagerLifecycle))]
                public sealed class ManagerLifecycle
                {
                    private readonly IGenerationMarker _marker;

                    public ManagerLifecycle(IGenerationMarker marker)
                    {
                        _marker = marker;
                    }

                    public ValueTask ExpiredAsync(HotfixLifecycleCall<LifecycleRequest> call)
                    {
                        if (_marker.Generation.Length == 0)
                        {
                            throw new System.InvalidOperationException("marker was not injected");
                        }

                        return default;
                    }
                }
                """,
                [
                    stableReference,
                    abstractionsReference,
                    testsReference,
                    dependencyInjectionReference
                ],
                cancellationToken);

            await EmitAssemblyAsync(
                rootDiServiceHotfixAssemblyName,
                rootDiServiceHotfixAssemblyPath,
                """
                using System.Threading.Tasks;
                using StableContracts;
                using Lakona.Game.Server.Hotfix;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix.Tests;

                namespace RootDiServiceHotfix;

                [HotfixService(typeof(IManagerService))]
                public sealed class ManagerService
                {
                    private readonly IRootOnlyMarker _marker;

                    public ManagerService(IRootOnlyMarker marker)
                    {
                        _marker = marker;
                    }

                    public ValueTask<ServiceReply> LoginAsync(HotfixServiceCall<ServiceRequest> call)
                    {
                        return new ValueTask<ServiceReply>(new ServiceReply(call.Request!.Value + _marker.Value.Length));
                    }
                }
                """,
                [stableReference, abstractionsReference, testsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                rootDiLocalServiceHotfixAssemblyName,
                rootDiLocalServiceHotfixAssemblyPath,
                """
                using System.Threading.Tasks;
                using StableContracts;
                using Lakona.Game.Server.Hotfix;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix.Tests;
                using Microsoft.Extensions.DependencyInjection;

                namespace RootDiLocalServiceHotfix;

                public sealed class HotfixLocalUsesRoot
                {
                    private readonly IRootOnlyMarker _marker;

                    public HotfixLocalUsesRoot(IRootOnlyMarker marker)
                    {
                        _marker = marker;
                    }

                    public int Offset => _marker.Value.Length;
                }

                [HotfixStartup]
                public static class HotfixStartup
                {
                    [HotfixConfigureServices]
                    public static void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<HotfixLocalUsesRoot>();
                    }
                }

                [HotfixService(typeof(IManagerService))]
                public sealed class ManagerService
                {
                    public ValueTask<ServiceReply> LoginAsync(HotfixServiceCall<ServiceRequest> call)
                    {
                        var localService = call.Services.GetRequiredService<HotfixLocalUsesRoot>();
                        return new ValueTask<ServiceReply>(new ServiceReply(call.Request!.Value + localService.Offset));
                    }
                }
                """,
                [
                    stableReference,
                    abstractionsReference,
                    testsReference,
                    dependencyInjectionAbstractionsReference
                ],
                cancellationToken);

            await EmitAssemblyAsync(
                missingDiServiceHotfixAssemblyName,
                missingDiServiceHotfixAssemblyPath,
                """
                using System.Threading.Tasks;
                using StableContracts;
                using Lakona.Game.Server.Hotfix;
                using Lakona.Game.Server.Hotfix.Abstractions;

                namespace MissingDiServiceHotfix;

                public sealed class MissingDependency
                {
                }

                [HotfixService(typeof(IManagerService))]
                public sealed class ManagerService
                {
                    public ManagerService(MissingDependency dependency)
                    {
                    }

                    public ValueTask<ServiceReply> LoginAsync(HotfixServiceCall<ServiceRequest> call)
                    {
                        return new ValueTask<ServiceReply>(new ServiceReply(call.Request!.Value));
                    }
                }
                """,
                [stableReference, abstractionsReference, testsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                multipleConstructorServiceHotfixAssemblyName,
                multipleConstructorServiceHotfixAssemblyPath,
                """
                using System.Threading.Tasks;
                using StableContracts;
                using Lakona.Game.Server.Hotfix;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix.Tests;

                namespace MultipleConstructorServiceHotfix;

                [HotfixService(typeof(IManagerService))]
                public sealed class ManagerService
                {
                    public ManagerService(IGenerationMarker marker)
                    {
                    }

                    public ManagerService(IRootOnlyMarker marker)
                    {
                    }

                    public ValueTask<ServiceReply> LoginAsync(HotfixServiceCall<ServiceRequest> call)
                    {
                        return new ValueTask<ServiceReply>(new ServiceReply(call.Request!.Value));
                    }
                }
                """,
                [stableReference, abstractionsReference, testsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                selectedConstructorDiServiceHotfixAssemblyName,
                selectedConstructorDiServiceHotfixAssemblyPath,
                """
                using System.Threading.Tasks;
                using StableContracts;
                using Lakona.Game.Server.Hotfix;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix.Tests;
                using Microsoft.Extensions.DependencyInjection;

                namespace SelectedConstructorDiServiceHotfix;

                public sealed class MissingDependency
                {
                }

                [HotfixService(typeof(IManagerService))]
                public sealed class ManagerService
                {
                    private readonly IGenerationMarker _marker;

                    public ManagerService(MissingDependency dependency)
                    {
                        throw new System.InvalidOperationException("wrong constructor");
                    }

                    [ActivatorUtilitiesConstructor]
                    public ManagerService(IGenerationMarker marker)
                    {
                        _marker = marker;
                    }

                    public ValueTask<ServiceReply> LoginAsync(HotfixServiceCall<ServiceRequest> call)
                    {
                        return new ValueTask<ServiceReply>(new ServiceReply(call.Request!.Value + _marker.Generation.Length));
                    }
                }

                [HotfixStartup]
                public static class HotfixStartup
                {
                    [HotfixConfigureServices]
                    public static void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<IGenerationMarker, SelectedMarker>();
                    }
                }

                public sealed class SelectedMarker : IGenerationMarker
                {
                    public string Generation => "selected";
                }
                """,
                [
                    stableReference,
                    abstractionsReference,
                    testsReference,
                    dependencyInjectionReference,
                    dependencyInjectionAbstractionsReference
                ],
                cancellationToken);

            // Copy private dependency next to the hotfix assembly so AssemblyDependencyResolver finds it.
            var privateDepNextToHotfix = Path.Combine(Path.GetDirectoryName(hotfixWithPrivateDependencyAssemblyPath)!, Path.GetFileName(privateDependencyAssemblyPath));
            File.Copy(privateDependencyAssemblyPath, privateDepNextToHotfix, overwrite: true);

            return new CompiledHotfixFixture(
                root,
                stableAssemblyPath,
                hotfixAssemblyPath,
                secondHotfixAssemblyPath,
                invalidAssemblyPath,
                hotfixOwnedStateAssemblyPath,
                hotfixOwnedReturnAssemblyPath,
                hotfixOwnedArgumentAssemblyPath,
                managerTestHotfixAssemblyPath,
                privateDependencyAssemblyPath,
                hotfixWithPrivateDependencyAssemblyPath,
                hotfixOwnedServiceReturnAssemblyPath,
                hotfixOwnedServiceArgumentAssemblyPath,
                validServiceHotfixAssemblyPath,
                constructorDiServiceHotfixAssemblyPath,
                constructorDiLifecycleHotfixAssemblyPath,
                rootDiServiceHotfixAssemblyPath,
                rootDiLocalServiceHotfixAssemblyPath,
                missingDiServiceHotfixAssemblyPath,
                multipleConstructorServiceHotfixAssemblyPath,
                selectedConstructorDiServiceHotfixAssemblyPath);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static async Task EmitAssemblyAsync(
            string assemblyName,
            string assemblyPath,
            string source,
            IReadOnlyList<MetadataReference> additionalReferences,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
            var references = GetTrustedPlatformReferences()
                .Concat(additionalReferences)
                .GroupBy(static reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToArray();
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [syntaxTree],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            await using var stream = File.Create(assemblyPath);
            var emit = compilation.Emit(stream, cancellationToken: cancellationToken);
            if (!emit.Success)
            {
                var diagnostics = string.Join(Environment.NewLine, emit.Diagnostics);
                throw new InvalidOperationException($"Could not emit test assembly '{assemblyName}'.{Environment.NewLine}{diagnostics}");
            }

            await stream.FlushAsync(cancellationToken);
        }

        private static IEnumerable<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (trustedPlatformAssemblies is null)
            {
                throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Where(static path => !string.Equals(
                    Path.GetFileName(path),
                    "Lakona.Game.Server.dll",
                    StringComparison.OrdinalIgnoreCase))
                .Select(static path => MetadataReference.CreateFromFile(path));
        }
    }

    private static async ValueTask<object?> AwaitValueTaskAsync<T>(object valueTask)
    {
        return await (ValueTask<T>)valueTask!;
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(categoryName, _entries);
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(string category, List<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Add(new LogEntry(category, logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(
        string Category,
        LogLevel Level,
        string Message,
        Exception? Exception);
}

public interface IGenerationMarker
{
    string Generation { get; }
}

public interface IRootOnlyMarker
{
    string Value { get; }
}

public sealed class RootOnlyMarker : IRootOnlyMarker
{
    public string Value => "root";
}

public interface IHotfixGeneratedRegistrationMarker
{
}

public sealed class GeneratedHotfixRegistrationMarker : IHotfixGeneratedRegistrationMarker
{
}

public sealed class HotfixGeneratedRegistrationConsumer
{
    public HotfixGeneratedRegistrationConsumer(IRootOnlyMarker root)
    {
        Root = root;
    }

    public IRootOnlyMarker Root { get; }
}

public sealed class GeneratedHotfixServiceRegistrationForTest : IHotfixGeneratedServiceRegistration
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IHotfixGeneratedRegistrationMarker, GeneratedHotfixRegistrationMarker>();
        services.AddTransient<HotfixGeneratedRegistrationConsumer>();
    }
}
