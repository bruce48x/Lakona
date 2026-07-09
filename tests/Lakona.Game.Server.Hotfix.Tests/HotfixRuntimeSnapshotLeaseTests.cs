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

public sealed class HotfixRuntimeSnapshotLeaseTests
{
    [Fact]
    public void Runtime_snapshot_exposes_no_public_retirement_control()
    {
        Assert.Null(typeof(HotfixRuntimeSnapshot).GetMethod(
            "Retire",
            BindingFlags.Instance | BindingFlags.Public));
        Assert.DoesNotContain(
            typeof(HotfixRuntimeSnapshot).GetConstructors(BindingFlags.Instance | BindingFlags.Public),
            constructor => constructor.GetParameters().Any(static parameter => parameter.ParameterType == typeof(Action)));
    }

    [Fact]
    public void AcquireCurrent_pins_provider_until_last_lease_is_disposed()
    {
        var provider = new TrackingServiceProvider();
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(),
            provider,
            onRetired: provider.Dispose);
        var accessor = new FixedRuntimeAccessor(snapshot);

        using var first = accessor.AcquireCurrent();
        using var second = accessor.AcquireCurrent();
        snapshot.Retire();

        Assert.False(provider.Disposed);

        first.Dispose();
        Assert.False(provider.Disposed);

        second.Dispose();
        Assert.True(provider.Disposed);
    }

    [Fact]
    public async Task AcquireCurrent_pins_load_context_until_last_lease_is_disposed()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var source = new SwitchableAssemblySource(compiled.FirstHotfixAssemblyPath);
        var manager = new HotfixManager(source, [typeof(IGenerationMarker).Assembly.GetName().Name!]);
        var accessor = Assert.IsAssignableFrom<IHotfixRuntimeAccessor>(manager);

        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        var lease = accessor.AcquireCurrent();
        var oldContext = AssemblyLoadContext.GetLoadContext(lease.Snapshot.MainAssembly!);
        Assert.NotNull(oldContext);
        var unloading = false;
        oldContext.Unloading += _ => unloading = true;
        source.AssemblyPath = compiled.SecondHotfixAssemblyPath;

        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Diagnostics));
        Assert.False(unloading);

        lease.Dispose();
        Assert.True(unloading);
    }

    [Fact]
    public void Disposing_lease_twice_is_harmless()
    {
        var retired = 0;
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(),
            new TrackingServiceProvider(),
            onRetired: () => retired++);
        var lease = snapshot.AcquireLease();

        snapshot.Retire();
        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, retired);
    }

    [Fact]
    public void Runtime_snapshot_retirement_suppresses_cleanup_failures()
    {
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(),
            new ThrowingDisposeServiceProvider(),
            dispatchTable: null,
            hotfixServices: new ThrowingDisposeServiceProvider(),
            mainAssembly: null,
            loadContext: null,
            sourceVersion: null,
            sourcePath: null,
            ownsRuntimeResources: true,
            onRetired: () => throw new ApplicationException("retired callback failed"));
        var lease = snapshot.AcquireLease();

        snapshot.Retire();
        var exception = Record.Exception(lease.Dispose);

        Assert.Null(exception);
    }

    [Fact]
    public void Failed_ambient_scope_creation_releases_snapshot_lease_reference()
    {
        var provider = new ThrowingTimerBackendServiceProvider();
        var retired = false;
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(),
            provider,
            new HotfixDispatchTable(1, Array.Empty<HotfixMethodBinding>()),
            provider,
            mainAssembly: null,
            loadContext: null,
            sourceVersion: null,
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired: () => retired = true);

        var exception = Assert.Throws<InvalidOperationException>(() => snapshot.AcquireLease());
        Assert.Equal("timer backend resolution failed", exception.Message);

        snapshot.Retire();

        Assert.True(retired);
    }

    [Fact]
    public void Retired_snapshot_rejects_new_leases_before_active_leases_drain()
    {
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(),
            new TrackingServiceProvider());
        using var lease = snapshot.AcquireLease();

        snapshot.Retire();

        Assert.Throws<ObjectDisposedException>(() => snapshot.AcquireLease());
    }

    [Fact]
    public async Task Failed_reload_leaves_old_runtime_snapshot_current()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var source = new SwitchableAssemblySource(compiled.FirstHotfixAssemblyPath);
        var manager = new HotfixManager(source, [typeof(IGenerationMarker).Assembly.GetName().Name!]);
        var accessor = Assert.IsAssignableFrom<IHotfixRuntimeAccessor>(manager);

        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        var previousRuntime = accessor.Current;
        source.AssemblyPath = Path.Combine(compiled.RootDirectory, "MissingHotfix.dll");

        var failed = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.False(failed.Succeeded);
        Assert.Same(previousRuntime, accessor.Current);
        Assert.Equal(first.Current.DispatchTableVersion, manager.Current.DispatchTableVersion);
        Assert.Equal(first.Current.ActorHosts, manager.Current.ActorHosts);
    }

    [Fact]
    public async Task Successful_reload_retires_old_snapshot_after_active_leases_drain()
    {
        using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
        var source = new SwitchableAssemblySource(compiled.FirstHotfixAssemblyPath);
        var manager = new HotfixManager(source, [typeof(IGenerationMarker).Assembly.GetName().Name!]);
        var accessor = Assert.IsAssignableFrom<IHotfixRuntimeAccessor>(manager);

        var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        var lease = accessor.AcquireCurrent();
        var oldProvider = (TrackingDisposable)lease.Services.GetRequiredService<TrackingDisposable>();
        source.AssemblyPath = compiled.SecondHotfixAssemblyPath;

        var second = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Diagnostics));
        Assert.False(oldProvider.Disposed);

        lease.Dispose();
        Assert.True(oldProvider.Disposed);
    }

    private sealed class FixedRuntimeAccessor(HotfixRuntimeSnapshot current) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current { get; } = current;

        public HotfixRuntimeSnapshotLease AcquireCurrent()
        {
            return Current.AcquireLease();
        }
    }

    private sealed class TrackingServiceProvider : IServiceProvider
    {
        public bool Disposed { get; private set; }

        public object? GetService(Type serviceType)
        {
            return null;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class ThrowingTimerBackendServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(Lakona.Game.Server.Hotfix.Abstractions.Timers.ILakonaTimerBackend))
            {
                throw new InvalidOperationException("timer backend resolution failed");
            }

            return null;
        }
    }

    private sealed class ThrowingDisposeServiceProvider : IServiceProvider, IDisposable
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }

        public void Dispose()
        {
            throw new ApplicationException("dispose failed");
        }
    }

    public sealed class TrackingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    public interface IGenerationMarker
    {
        string Generation { get; }
    }

    private sealed class SwitchableAssemblySource(string assemblyPath) : IHotfixAssemblySource
    {
        public string AssemblyPath { get; set; } = assemblyPath;

        public ValueTask<HotfixAssemblySourceResult> ResolveAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixAssemblySourceResult>(new HotfixAssemblySourceResult(
                "test",
                AssemblyPath,
                Path.GetDirectoryName(AssemblyPath)!));
        }
    }

    private sealed class CompiledHotfixFixture : IDisposable
    {
        private CompiledHotfixFixture(
            string rootDirectory,
            string firstHotfixAssemblyPath,
            string secondHotfixAssemblyPath,
            string invalidHotfixAssemblyPath)
        {
            RootDirectory = rootDirectory;
            FirstHotfixAssemblyPath = firstHotfixAssemblyPath;
            SecondHotfixAssemblyPath = secondHotfixAssemblyPath;
            InvalidHotfixAssemblyPath = invalidHotfixAssemblyPath;
        }

        public string RootDirectory { get; }

        public string FirstHotfixAssemblyPath { get; }

        public string SecondHotfixAssemblyPath { get; }

        public string InvalidHotfixAssemblyPath { get; }

        public static async Task<CompiledHotfixFixture> CreateAsync(CancellationToken cancellationToken)
        {
            var root = Path.Combine(Path.GetTempPath(), "LakonaHotfixLeaseTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var firstPath = Path.Combine(root, "LeaseHotfixOne.dll");
            var secondPath = Path.Combine(root, "LeaseHotfixTwo.dll");
            var invalidPath = Path.Combine(root, "LeaseHotfixInvalid.dll");
            var abstractionsReference = MetadataReference.CreateFromFile(typeof(HotfixBehaviorOfAttribute).Assembly.Location);
            var testsReference = MetadataReference.CreateFromFile(typeof(TrackingDisposable).Assembly.Location);
            var dependencyInjectionReference = MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location);

            await EmitAssemblyAsync(
                "LeaseHotfixOne",
                firstPath,
                CreateHotfixSource("one"),
                [abstractionsReference, testsReference, dependencyInjectionReference],
                cancellationToken);
            await EmitAssemblyAsync(
                "LeaseHotfixTwo",
                secondPath,
                CreateHotfixSource("two"),
                [abstractionsReference, testsReference, dependencyInjectionReference],
                cancellationToken);
            await EmitAssemblyAsync(
                "LeaseHotfixInvalid",
                invalidPath,
                """
                namespace LeaseHotfixInvalid;

                public sealed class NotAHotfix
                {
                }
                """,
                [abstractionsReference],
                cancellationToken);

            return new CompiledHotfixFixture(root, firstPath, secondPath, invalidPath);
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

        private static string CreateHotfixSource(string generation)
        {
            return $$"""
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix.Tests;
                using Microsoft.Extensions.DependencyInjection;

                namespace LeaseHotfix{{generation}};

                public sealed class GenerationMarker : HotfixRuntimeSnapshotLeaseTests.IGenerationMarker
                {
                    public string Generation => "{{generation}}";
                }

                public static class HotfixStartup
                {
                    public static void ConfigureServices(IServiceCollection services)
                    {
                        services.AddSingleton<HotfixRuntimeSnapshotLeaseTests.TrackingDisposable>();
                        services.AddSingleton<HotfixRuntimeSnapshotLeaseTests.IGenerationMarker, GenerationMarker>();
                    }
                }
                """;
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
}
