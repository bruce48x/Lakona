using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Timers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class LakonaTimerIntegrationTests
{
    [Fact]
    public void AddLakonaGameServer_registers_default_timer_backend()
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameServer()
            .BuildServiceProvider();

        var backend = provider.GetService<ILakonaTimerBackend>();

        Assert.IsType<LakonaTimerBackend>(backend);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_accepts_zero_due_time_and_stores_reload_safe_json_descriptor()
    {
        HotfixDispatch.Replace(new HotfixDispatchTable(0, Array.Empty<HotfixMethodBinding>()));
        await using var fixture = TimerFixture.Create(typeof(TimerCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);
        var args = new TimerArgs("payload", 42);

        var timerId = await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
            TimeSpan.Zero,
            nameof(TimerCallback.HandleAsync),
            args,
            CancellationToken.None);

        Assert.True(timerId.IsValid);
        Assert.True(backend.TryGetDescriptor(timerId, out var descriptor));
        Assert.Equal(typeof(TimerCallback).Assembly.GetName().Name, descriptor.CallbackAssemblyName);
        Assert.Equal(typeof(TimerCallback).FullName, descriptor.CallbackFullName);
        Assert.Equal(nameof(TimerCallback.HandleAsync), descriptor.MethodName);
        Assert.Equal(typeof(TimerArgs).Assembly.GetName().Name, descriptor.ArgsAssemblyName);
        Assert.Equal(typeof(TimerArgs).FullName, descriptor.ArgsFullName);
        Assert.Equal("system-text-json-v1", descriptor.SerializerId);
        Assert.Equal(fixture.Table.Version, descriptor.Generation);
        Assert.Null(descriptor.Period);
        Assert.InRange(descriptor.NextDueAtUtc, DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Equal(args, JsonSerializer.Deserialize<TimerArgs>(descriptor.JsonPayload.Span));
        Assert.DoesNotContain(descriptor.GetType().GetProperties(), property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));
        Assert.DoesNotContain(descriptor.GetType().GetProperties(), property => typeof(Delegate).IsAssignableFrom(property.PropertyType));
        Assert.DoesNotContain(descriptor.GetType().GetProperties(), property => property.PropertyType == typeof(JsonSerializerOptions));
    }

    [Fact]
    public async Task CreatePeriodicTimerAsync_stores_period_and_rejects_non_positive_period()
    {
        await using var fixture = TimerFixture.Create(typeof(TimerCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var timerId = await LakonaTimer.CreatePeriodicTimerAsync<TimerCallback, TimerArgs>(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3),
            nameof(TimerCallback.HandleAsync),
            new TimerArgs("periodic", 7),
            CancellationToken.None);

        Assert.True(backend.TryGetDescriptor(timerId, out var descriptor));
        Assert.Equal(TimeSpan.FromSeconds(3), descriptor.Period);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await LakonaTimer.CreatePeriodicTimerAsync<TimerCallback, TimerArgs>(
                TimeSpan.Zero,
                TimeSpan.Zero,
                nameof(TimerCallback.HandleAsync),
                new TimerArgs("invalid", 0),
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_negative_due_time_before_registration()
    {
        await using var fixture = TimerFixture.Create(typeof(TimerCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
                TimeSpan.FromTicks(-1),
                nameof(TimerCallback.HandleAsync),
                new TimerArgs("negative", 0),
                CancellationToken.None));

        Assert.Empty(backend.Descriptors);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_validates_against_leased_snapshot_not_externally_published_snapshot()
    {
        HotfixDispatch.Replace(new HotfixDispatchTable(100, Array.Empty<HotfixMethodBinding>()));
        await using var fixture = TimerFixture.Create(typeof(TimerCallback), version: 17);
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var timerId = await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
            TimeSpan.Zero,
            nameof(TimerCallback.HandleAsync),
            new TimerArgs("leased", 1),
            CancellationToken.None);

        Assert.True(backend.TryGetDescriptor(timerId, out var descriptor));
        Assert.Equal(17, descriptor.Generation);
    }

    [Fact]
    public async Task CallbackResolver_resolves_callback_from_reload_safe_descriptor_data()
    {
        await using var fixture = TimerFixture.Create(typeof(TimerCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var timerId = await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
            TimeSpan.Zero,
            nameof(TimerCallback.HandleAsync),
            new TimerArgs("resolve", 1),
            CancellationToken.None);

        Assert.True(backend.TryGetDescriptor(timerId, out var descriptor));
        var method = new LakonaTimerCallbackResolver().Resolve(fixture.Lease.Snapshot, descriptor);

        Assert.Equal(typeof(TimerCallback).GetMethod(nameof(TimerCallback.HandleAsync)), method);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_accepts_same_name_overloads_when_exactly_one_matches_timer_signature()
    {
        await using var fixture = TimerFixture.Create(typeof(OverloadedTimerCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var timerId = await LakonaTimer.CreateOnceTimerAsync<OverloadedTimerCallback, TimerArgs>(
            TimeSpan.Zero,
            nameof(OverloadedTimerCallback.HandleAsync),
            new TimerArgs("overload", 1),
            CancellationToken.None);

        Assert.True(backend.TryGetDescriptor(timerId, out var descriptor));
        Assert.Equal(nameof(OverloadedTimerCallback.HandleAsync), descriptor.MethodName);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_same_name_overloads_when_none_match_timer_signature()
    {
        await using var fixture = TimerFixture.Create(typeof(InvalidOverloadedTimerCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<InvalidOverloadedTimerCallback, TimerArgs>(
                TimeSpan.Zero,
                nameof(InvalidOverloadedTimerCallback.HandleAsync),
                new TimerArgs("overload", 1),
                CancellationToken.None));

        Assert.Contains("exactly one", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TimerTick", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallbackResolver_does_not_resolve_hotfix_args_type_from_stale_assembly()
    {
        const string assemblyName = "TimerHotfixArgsCollision";
        var staleAssembly = CompileHotfixAssembly(
            assemblyName,
            """
            namespace Collision;
            public sealed record MissingArgs(string Value);
            """);
        _ = staleAssembly.GetType("Collision.MissingArgs", throwOnError: true);
        var activeAssembly = CompileHotfixAssembly(
            assemblyName,
            """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions.Timers;
            namespace Collision;
            public sealed record OtherArgs(string Value);
            public sealed class Callback
            {
                public static ValueTask HandleAsync(TimerTick<OtherArgs> tick)
                {
                    _ = tick;
                    return default;
                }
            }
            """);
        await using var fixture = TimerFixture.Create(
            activeAssembly.GetType("Collision.Callback", throwOnError: true)!,
            mainAssembly: activeAssembly);
        var descriptor = new LakonaTimerDescriptor(
            TimerId.FromGuid(Guid.NewGuid()),
            assemblyName,
            "Collision.Callback",
            "HandleAsync",
            assemblyName,
            "Collision.MissingArgs",
            "system-text-json-v1",
            Encoding.UTF8.GetBytes("""{"Value":"stale"}"""),
            DateTimeOffset.UtcNow,
            period: null,
            generation: fixture.Table.Version);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LakonaTimerCallbackResolver().Resolve(fixture.Lease.Snapshot, descriptor));

        Assert.Contains("not loaded", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Collision.MissingArgs", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_does_not_pin_collectible_hotfix_args_metadata()
    {
        var loadContextReference = CreateTimerAndReleaseHotfixContextOnIsolatedThread();

        await AssertLoadContextUnloadedAsync(loadContextReference, CancellationToken.None);
    }

    [Theory]
    [InlineData("DoesNotExist", "loaded")]
    [InlineData(nameof(TimerCallback.InstanceAsync), "static")]
    [InlineData(nameof(TimerCallback.ReturnsInt32), "ValueTask")]
    [InlineData(nameof(TimerCallback.WrongParameterAsync), "TimerTick")]
    [InlineData(nameof(TimerCallback.GenericMethodAsync), "generic")]
    public async Task CreateOnceTimerAsync_rejects_invalid_callback_method_shapes(string methodName, string expectedMessage)
    {
        await using var fixture = TimerFixture.Create(typeof(TimerCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
                TimeSpan.Zero,
                methodName,
                new TimerArgs("invalid", 0),
                CancellationToken.None));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Descriptors);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_generic_callback_type()
    {
        await using var fixture = TimerFixture.Create(typeof(GenericTimerCallback<TimerArgs>));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<GenericTimerCallback<TimerArgs>, TimerArgs>(
                TimeSpan.Zero,
                nameof(GenericTimerCallback<TimerArgs>.HandleAsync),
                new TimerArgs("generic", 0),
                CancellationToken.None));

        Assert.Contains("generic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_generic_root_args_type()
    {
        await using var fixture = TimerFixture.Create(typeof(GenericArgsCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<GenericArgsCallback, GenericTimerArgs<string>>(
                TimeSpan.Zero,
                nameof(GenericArgsCallback.HandleAsync),
                new GenericTimerArgs<string>("generic"),
                CancellationToken.None));

        Assert.Contains("generic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_callback_type_outside_active_hotfix_assembly()
    {
        await using var fixture = TimerFixture.Create(typeof(TimerCallback), mainAssembly: typeof(string).Assembly);
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
                TimeSpan.Zero,
                nameof(TimerCallback.HandleAsync),
                new TimerArgs("assembly", 0),
                CancellationToken.None));

        Assert.Contains("active hotfix assembly", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_non_serializable_args()
    {
        await using var fixture = TimerFixture.Create(typeof(NonSerializableCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<NonSerializableCallback, NonSerializableArgs>(
                TimeSpan.Zero,
                nameof(NonSerializableCallback.HandleAsync),
                new NonSerializableArgs(() => 1),
                CancellationToken.None));

        Assert.Contains("serialize", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Descriptors);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_args_round_trip_failure()
    {
        await using var fixture = TimerFixture.Create(typeof(RoundTripCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<RoundTripCallback, RoundTripArgs>(
                TimeSpan.Zero,
                nameof(RoundTripCallback.HandleAsync),
                new RoundTripArgs("original"),
                CancellationToken.None));

        Assert.Contains("round-trip", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Descriptors);
    }

    public sealed record TimerArgs(string Name, int Count);

    public sealed class TimerCallback
    {
        public static ValueTask HandleAsync(TimerTick<TimerArgs> tick)
        {
            _ = tick;
            return default;
        }

        public ValueTask InstanceAsync(TimerTick<TimerArgs> tick)
        {
            _ = tick;
            return default;
        }

        public static int ReturnsInt32(TimerTick<TimerArgs> tick)
        {
            _ = tick;
            return 1;
        }

        public static ValueTask WrongParameterAsync(TimerArgs args)
        {
            _ = args;
            return default;
        }

        public static ValueTask GenericMethodAsync<T>(TimerTick<TimerArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public sealed class GenericTimerCallback<T>
    {
        public static ValueTask HandleAsync(TimerTick<TimerArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public sealed class OverloadedTimerCallback
    {
        public static ValueTask HandleAsync(TimerTick<TimerArgs> tick)
        {
            _ = tick;
            return default;
        }

        public static ValueTask HandleAsync(TimerArgs args)
        {
            _ = args;
            return default;
        }
    }

    public sealed class InvalidOverloadedTimerCallback
    {
        public static ValueTask HandleAsync(TimerArgs args)
        {
            _ = args;
            return default;
        }

        public static int HandleAsync(TimerTick<TimerArgs> tick)
        {
            _ = tick;
            return 1;
        }
    }

    public sealed record GenericTimerArgs<T>(T Value);

    public sealed class GenericArgsCallback
    {
        public static ValueTask HandleAsync(TimerTick<GenericTimerArgs<string>> tick)
        {
            _ = tick;
            return default;
        }
    }

    public sealed record NonSerializableArgs(Func<int> Factory);

    public sealed class NonSerializableCallback
    {
        public static ValueTask HandleAsync(TimerTick<NonSerializableArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public sealed class RoundTripArgs : IEquatable<RoundTripArgs>
    {
        private string value = string.Empty;

        public RoundTripArgs()
        {
        }

        public RoundTripArgs(string value)
        {
            this.value = value;
        }

        public string Value
        {
            get => value;
            set => this.value = value + "-roundtrip";
        }

        public bool Equals(RoundTripArgs? other)
        {
            return other is not null && StringComparer.Ordinal.Equals(Value, other.Value);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as RoundTripArgs);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }
    }

    public sealed class RoundTripCallback
    {
        public static ValueTask HandleAsync(TimerTick<RoundTripArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    private sealed class TimerFixture : IAsyncDisposable
    {
        private TimerFixture(HotfixRuntimeSnapshot snapshot, HotfixRuntimeSnapshotLease lease, HotfixDispatchTable table)
        {
            Snapshot = snapshot;
            Lease = lease;
            Table = table;
        }

        public HotfixRuntimeSnapshot Snapshot { get; }

        public HotfixRuntimeSnapshotLease Lease { get; }

        public HotfixDispatchTable Table { get; }

        public static TimerFixture Create(Type callbackType, long version = 9, Assembly? mainAssembly = null)
        {
            var table = new HotfixDispatchTable(version, Array.Empty<HotfixMethodBinding>());
            var services = new ServiceCollection().BuildServiceProvider();
            var snapshot = new HotfixRuntimeSnapshot(
                new HotfixServiceInvoker(table),
                EmptyHotfixFeatureCommandInvoker.Instance,
                services,
                table,
                services,
                mainAssembly ?? callbackType.Assembly,
                loadContext: null,
                sourceVersion: "test",
                sourceKind: "test",
                sourcePath: null,
                ownsRuntimeResources: true,
                onRetired: null);
            return new TimerFixture(snapshot, snapshot.AcquireLease(), table);
        }

        public ValueTask DisposeAsync()
        {
            Lease.Dispose();
            Snapshot.Retire();
            return default;
        }
    }

    private static Assembly CompileHotfixAssembly(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp10));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            var diagnostics = string.Join(Environment.NewLine, result.Diagnostics);
            throw new InvalidOperationException(diagnostics);
        }

        stream.Position = 0;
        return new AssemblyLoadContext($"{assemblyName}-{Guid.NewGuid():N}", isCollectible: true)
            .LoadFromStream(stream);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateTimerAndReleaseHotfixContextOnIsolatedThread()
    {
        WeakReference? loadContextReference = null;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                loadContextReference = CreateTimerAndReleaseHotfixContext();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw exception;
        }

        return loadContextReference ?? throw new InvalidOperationException("Unload test did not capture a load context reference.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateTimerAndReleaseHotfixContext()
    {
        const string assemblyName = "TimerHotfixUnload";
        var assembly = CompileHotfixAssembly(
            assemblyName,
            """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions.Timers;
            namespace Unload;
            public sealed record TimerArgs(string Name, int Count);
            public sealed class TimerCallback
            {
                public static ValueTask HandleAsync(TimerTick<TimerArgs> tick)
                {
                    _ = tick;
                    return default;
                }
            }
            public static class TimerStarter
            {
                public static ValueTask<TimerId> StartAsync()
                {
                    return LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
                        System.TimeSpan.Zero,
                        nameof(TimerCallback.HandleAsync),
                        new TimerArgs("hotfix", 5),
                        default);
                }
            }
            """);
        var loadContext = AssemblyLoadContext.GetLoadContext(assembly)!;
        var loadContextReference = new WeakReference(loadContext);
        var starterType = assembly.GetType("Unload.TimerStarter", throwOnError: true)!;
        using var services = new ServiceCollection().BuildServiceProvider();
        var table = new HotfixDispatchTable(44, Array.Empty<HotfixMethodBinding>());
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
            EmptyHotfixFeatureCommandInvoker.Instance,
            services,
            table,
            services,
            assembly,
            loadContext: null,
            sourceVersion: "unload-test",
            sourceKind: "test",
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired: null);
        var backend = new LakonaTimerBackend();
        using (var lease = snapshot.AcquireLease())
        using (LakonaTimerExecutionScope.Enter(backend, lease))
        {
            var startMethod = starterType.GetMethod("StartAsync", BindingFlags.Public | BindingFlags.Static)!;
            var result = startMethod.Invoke(null, []);
            var timerId = ((ValueTask<TimerId>)result!).GetAwaiter().GetResult();
            Assert.True(backend.TryGetDescriptor(timerId, out var descriptor));
            Assert.Equal("Unload.TimerArgs", descriptor.ArgsFullName);
        }

        snapshot.Retire();
        loadContext.Unload();
        return loadContextReference;
    }

    private static async Task AssertLoadContextUnloadedAsync(WeakReference loadContextReference, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100 && loadContextReference.IsAlive; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        Assert.False(loadContextReference.IsAlive, "Hotfix timer creation should not retain collectible hotfix AssemblyLoadContext metadata.");
    }
}
