using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixManagerTests
{
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
    public async Task Reload_failure_keeps_previous_snapshot()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var stableAssembly = Assembly.LoadFrom(compiled.StableAssemblyPath);
        var source = new SwitchableAssemblySource(compiled.ManagerTestHotfixAssemblyPath);
        var manager = new HotfixManager(source, [stableAssembly.GetName().Name!]);
        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        source.Path = @"Z:\missing\Missing.Hotfix.dll";

        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(first.Current.DispatchTableVersion, second.Current.DispatchTableVersion);
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
                "fixed",
                "test",
                _path,
                Path.GetDirectoryName(_path)!));
        }
    }

    private sealed class SwitchableAssemblySource : IHotfixAssemblySource
    {
        public SwitchableAssemblySource(string path)
        {
            Path = path;
        }

        public string Path { get; set; }

        public Action? AfterResolve { get; set; }

        public ValueTask<HotfixAssemblySourceResult> ResolveAsync(CancellationToken cancellationToken = default)
        {
            var result = new HotfixAssemblySourceResult(
                "switchable",
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
                "blocking",
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
            string hotfixWithPrivateDependencyAssemblyPath)
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

            await EmitAssemblyAsync(
                stableAssemblyName,
                stableAssemblyPath,
                """
                namespace StableContracts;

                public sealed class ArenaSimulation
                {
                }

                public sealed class ManagerTestState
                {
                }
                """,
                [],
                cancellationToken);

            var stableReference = MetadataReference.CreateFromFile(stableAssemblyPath);
            var abstractionsReference = MetadataReference.CreateFromFile(typeof(HotfixSystemOfAttribute).Assembly.Location);

            await EmitAssemblyAsync(
                hotfixAssemblyName,
                hotfixAssemblyPath,
                $$"""
                using StableContracts;
                using Lakona.Game.Server.Hotfix.Abstractions;

                namespace HotfixLogic;

                [HotfixSystemOf(typeof(ArenaSimulation))]
                public static class ArenaSimulationSystem
                {
                    public static int Tick(this ArenaSimulation self, int delta)
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

                namespace HotfixLogicV2;

                [HotfixSystemOf(typeof(ArenaSimulation))]
                public static class ArenaSimulationSystem
                {
                    public static int Tick(this ArenaSimulation self, int delta)
                    {
                        return delta + 1;
                    }
                }
                """,
                [stableReference, abstractionsReference],
                cancellationToken);

            await EmitAssemblyAsync(
                invalidAssemblyName,
                invalidAssemblyPath,
                $$"""
                using StableContracts;
                using Lakona.Game.Server.Hotfix.Abstractions;

                namespace InvalidHotfixLogic;

                [HotfixSystemOf(typeof(ArenaSimulation))]
                public static class ArenaSimulationSystem
                {
                    public static bool TryRead(this ArenaSimulation self, out int value)
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

                [HotfixSystemOf(typeof(OwnedState))]
                public static class OwnedStateSystem
                {
                    public static int Tick(this OwnedState self)
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

                [HotfixSystemOf(typeof(ArenaSimulation))]
                public static class ArenaSimulationSystem
                {
                    public static OwnedResult Tick(this ArenaSimulation self)
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

                [HotfixSystemOf(typeof(ArenaSimulation))]
                public static class ArenaSimulationSystem
                {
                    public static int Tick(this ArenaSimulation self, OwnedCommand command)
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

                namespace HotfixLogic;

                [HotfixSystemOf(typeof(ManagerTestState))]
                public static class ManagerTestStateSystem
                {
                    public static int Add(this ManagerTestState state, int value)
                    {
                        return value;
                    }
                }
                """,
                [stableReference, abstractionsReference],
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

                [HotfixSystemOf(typeof(ArenaSimulation))]
                public static class ArenaSimulationSystem
                {
                    public static int Tick(this ArenaSimulation self, int delta)
                    {
                        return InternalHelper.Transform(delta);
                    }
                }
                """,
                [stableReference, abstractionsReference, privateDepReference],
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
                hotfixWithPrivateDependencyAssemblyPath);
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
                .Select(static path => MetadataReference.CreateFromFile(path));
        }
    }
}
