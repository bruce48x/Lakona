using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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
    public async Task CommitStagedTimersAsync_rolls_back_partially_activated_timers_when_commit_fails()
    {
        await using var fixture = TimerFixture.Create(typeof(TimerCallback));
        var backend = new LakonaTimerBackend();
        TimerId existingTimerId;
        using (LakonaTimerExecutionScope.Enter(backend, fixture.Lease))
        {
            existingTimerId = await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
                TimeSpan.Zero,
                nameof(TimerCallback.HandleAsync),
                new TimerArgs("existing", 1),
                CancellationToken.None);
        }

        var stagingBackend = backend.CreateStagingBackend();
        using (LakonaTimerExecutionScope.Enter(stagingBackend, fixture.Lease))
        {
            var firstStagedTimerId = await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
                TimeSpan.Zero,
                nameof(TimerCallback.HandleAsync),
                new TimerArgs("first-staged", 2),
                CancellationToken.None);
            var secondStagedTimerId = await LakonaTimer.CreateOnceTimerAsync<TimerCallback, TimerArgs>(
                TimeSpan.Zero,
                nameof(TimerCallback.HandleAsync),
                new TimerArgs("second-staged", 3),
                CancellationToken.None);
            ReplaceStagedTimerId(stagingBackend, secondStagedTimerId, existingTimerId);

            var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await backend.CommitStagedTimersAsync(stagingBackend, CancellationToken.None));

            Assert.Contains("same key", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(backend.TryGetDescriptor(existingTimerId, out _));
            Assert.False(backend.TryGetDescriptor(firstStagedTimerId, out _));
            Assert.Single(backend.Descriptors);
        }
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

    [Fact]
    public async Task CreateOnceTimerAsync_serializes_collection_nested_enum_and_primitive_timer_args()
    {
        await using var fixture = TimerFixture.Create(typeof(ComplexTimerCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);
        var args = new ComplexTimerArgs(
            Numbers: [1, 2, 3],
            Names: ["one", "two"],
            Children:
            [
                new ComplexNestedArgs("alpha", ComplexTimerMode.Fast),
                new ComplexNestedArgs("beta", ComplexTimerMode.Slow)
            ],
            Mode: ComplexTimerMode.Fast,
            MaybeCount: 12,
            SignedByte: -4,
            UnsignedShort: 65000,
            UnsignedInt: 4000000000,
            UnsignedLong: 9000000000000000000,
            Code: 'Z');

        var timerId = await LakonaTimer.CreateOnceTimerAsync<ComplexTimerCallback, ComplexTimerArgs>(
            TimeSpan.Zero,
            nameof(ComplexTimerCallback.HandleAsync),
            args,
            CancellationToken.None);

        Assert.True(backend.TryGetDescriptor(timerId, out var descriptor));
        using var document = JsonDocument.Parse(descriptor.JsonPayload);
        var root = document.RootElement;

        Assert.Equal([1, 2, 3], root.GetProperty(nameof(ComplexTimerArgs.Numbers)).EnumerateArray().Select(static item => item.GetInt32()).ToArray());
        Assert.Equal(["one", "two"], root.GetProperty(nameof(ComplexTimerArgs.Names)).EnumerateArray().Select(static item => item.GetString()!).ToArray());
        Assert.Equal("Fast", root.GetProperty(nameof(ComplexTimerArgs.Mode)).GetString());
        Assert.Equal(12, root.GetProperty(nameof(ComplexTimerArgs.MaybeCount)).GetInt32());
        Assert.Equal(-4, root.GetProperty(nameof(ComplexTimerArgs.SignedByte)).GetInt32());
        Assert.Equal(65000, root.GetProperty(nameof(ComplexTimerArgs.UnsignedShort)).GetInt32());
        Assert.Equal(4000000000U, root.GetProperty(nameof(ComplexTimerArgs.UnsignedInt)).GetUInt32());
        Assert.Equal(9000000000000000000UL, root.GetProperty(nameof(ComplexTimerArgs.UnsignedLong)).GetUInt64());
        Assert.Equal("Z", root.GetProperty(nameof(ComplexTimerArgs.Code)).GetString());
        var children = root.GetProperty(nameof(ComplexTimerArgs.Children)).EnumerateArray().ToArray();
        Assert.Equal("alpha", children[0].GetProperty(nameof(ComplexNestedArgs.Label)).GetString());
        Assert.Equal("Fast", children[0].GetProperty(nameof(ComplexNestedArgs.Mode)).GetString());
        Assert.Equal("beta", children[1].GetProperty(nameof(ComplexNestedArgs.Label)).GetString());
        Assert.Equal("Slow", children[1].GetProperty(nameof(ComplexNestedArgs.Mode)).GetString());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(null)]
    public async Task CreateOnceTimerAsync_accepts_nullable_primitive_root_args(int? value)
    {
        await using var fixture = TimerFixture.Create(typeof(NullableIntCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var timerId = await LakonaTimer.CreateOnceTimerAsync<NullableIntCallback, int?>(
            TimeSpan.Zero,
            nameof(NullableIntCallback.HandleAsync),
            value,
            CancellationToken.None);

        Assert.True(backend.TryGetDescriptor(timerId, out var descriptor));
        using var document = JsonDocument.Parse(descriptor.JsonPayload);
        if (value is null)
        {
            Assert.Equal(JsonValueKind.Null, document.RootElement.ValueKind);
        }
        else
        {
            Assert.Equal(value.Value, document.RootElement.GetInt32());
        }
    }

    [Fact]
    public void ArgsSerializer_rejects_unknown_descriptor_serializer_id()
    {
        var serializer = new LakonaTimerArgsSerializer();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            serializer.Deserialize(
                serializerId: "system-text-json-v2",
                Encoding.UTF8.GetBytes("5"),
                typeof(int)));

        Assert.Contains("serializer", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("system-text-json-v2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ArgsSerializer_deserializes_supported_descriptor_payload()
    {
        var serializer = new LakonaTimerArgsSerializer();

        var value = serializer.Deserialize(
            LakonaTimerArgsSerializer.SystemTextJsonSerializerId,
            Encoding.UTF8.GetBytes("""{"Name":"payload","Count":42}"""),
            typeof(TimerArgs));

        Assert.Equal(new TimerArgs("payload", 42), Assert.IsType<TimerArgs>(value));
    }

    [Fact]
    public void CallbackResolver_resolves_descriptor_against_active_reloaded_snapshot()
    {
        const string assemblyName = "TimerReloadPositive";
        var v1Assembly = CompileHotfixAssembly(
            assemblyName,
            """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions.Timers;
            namespace Reload;
            public sealed record Args(string Value);
            public sealed class Callback
            {
                public static ValueTask HandleAsync(TimerTick<Args> tick)
                {
                    _ = tick;
                    return default;
                }
            }
            """);
        var v2Assembly = CompileHotfixAssembly(
            assemblyName,
            """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions.Timers;
            namespace Reload;
            public sealed record Args(string Value);
            public sealed class Callback
            {
                public static ValueTask HandleAsync(TimerTick<Args> tick)
                {
                    _ = tick;
                    return default;
                }
            }
            """);
        var v1CallbackType = v1Assembly.GetType("Reload.Callback", throwOnError: true)!;
        var v2CallbackType = v2Assembly.GetType("Reload.Callback", throwOnError: true)!;
        var descriptor = new LakonaTimerDescriptor(
            TimerId.FromGuid(Guid.NewGuid()),
            v1Assembly.GetName().Name!,
            v1CallbackType.FullName!,
            "HandleAsync",
            v1Assembly.GetName().Name!,
            "Reload.Args",
            "system-text-json-v1",
            Encoding.UTF8.GetBytes("""{"Value":"from-v1"}"""),
            DateTimeOffset.UtcNow,
            period: null,
            generation: 1);
        var snapshot = CreateSnapshotForAssembly(v2Assembly);
        try
        {
            var method = new LakonaTimerCallbackResolver().Resolve(snapshot, descriptor);

            Assert.Same(v2Assembly, method.DeclaringType!.Assembly);
            Assert.Equal(v2CallbackType, method.DeclaringType);
        }
        finally
        {
            snapshot.Retire();
        }
    }

    [Theory]
    [MemberData(nameof(UnsupportedTimerArgsCases))]
    public async Task CreateOnceTimerAsync_rejects_unsupported_declared_timer_args_shapes(
        Type callbackType,
        Type argsType,
        object args,
        string expectedMessage)
    {
        await using var fixture = TimerFixture.Create(callbackType);
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await InvokeCreateOnceTimerAsync(callbackType, argsType, args));

        Assert.Contains(expectedMessage, exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Descriptors);
    }

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_cyclic_timer_args()
    {
        await using var fixture = TimerFixture.Create(typeof(CyclicTimerCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);
        var args = new CyclicTimerArgs { Name = "cycle" };
        args.Next = args;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<CyclicTimerCallback, CyclicTimerArgs>(
                TimeSpan.Zero,
                nameof(CyclicTimerCallback.HandleAsync),
                args,
                CancellationToken.None));

        Assert.Contains("cycle", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Descriptors);
    }

    [Fact]
    public void CallbackResolver_resolves_non_hotfix_args_only_from_default_load_context()
    {
        const string assemblyName = "TimerStableArgsCollision";
        var staleAssembly = CompileHotfixAssembly(
            assemblyName,
            """
            namespace StableCollision;
            public sealed record Args(string Value);
            """);
        _ = staleAssembly.GetType("StableCollision.Args", throwOnError: true);
        var activeAssembly = CompileHotfixAssembly(
            "TimerStableCallback",
            """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions.Timers;
            namespace StableCollision;
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
        var descriptor = new LakonaTimerDescriptor(
            TimerId.FromGuid(Guid.NewGuid()),
            activeAssembly.GetName().Name!,
            "StableCollision.Callback",
            "HandleAsync",
            assemblyName,
            "StableCollision.Args",
            "system-text-json-v1",
            Encoding.UTF8.GetBytes("""{"Value":"stale"}"""),
            DateTimeOffset.UtcNow,
            period: null,
            generation: 1);
        var snapshot = CreateSnapshotForAssembly(activeAssembly);

        var exception = Assert.IsType<InvalidOperationException>(
            Record.Exception(() => new LakonaTimerCallbackResolver().Resolve(snapshot, descriptor)));

        Assert.Contains("not loaded", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StableCollision.Args", exception.Message, StringComparison.Ordinal);
        snapshot.Retire();
    }

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_args_from_non_main_collectible_assembly()
    {
        var callbackAssembly = CompileHotfixAssembly(
            "TimerCallbackOnly",
            """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions.Timers;
            namespace SplitHotfix;
            public sealed class Callback
            {
                public static ValueTask HandleAsync(TimerTick<ExternalArgs> tick)
                {
                    _ = tick;
                    return default;
                }
            }
            public sealed record ExternalArgs(string Value);
            """);
        var argsAssembly = CompileHotfixAssembly(
            "TimerArgsOnly",
            """
            namespace SplitHotfixArgs;
            public sealed record ExternalArgs(string Value);
            """);
        var callbackType = callbackAssembly.GetType("SplitHotfix.Callback", throwOnError: true)!;
        var argsType = argsAssembly.GetType("SplitHotfixArgs.ExternalArgs", throwOnError: true)!;
        var args = Activator.CreateInstance(argsType, "external")!;
        var backend = new LakonaTimerBackend();
        await using var fixture = TimerFixture.Create(callbackType, mainAssembly: callbackAssembly);
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await InvokeCreateOnceTimerAsync(callbackType, argsType, args));

        Assert.Contains("active hotfix assembly", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Descriptors);
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

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_round_trip_failure_even_when_equality_is_permissive()
    {
        await using var fixture = TimerFixture.Create(typeof(PermissiveRoundTripCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<PermissiveRoundTripCallback, PermissiveRoundTripArgs>(
                TimeSpan.Zero,
                nameof(PermissiveRoundTripCallback.HandleAsync),
                new PermissiveRoundTripArgs("original"),
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

    public sealed class PermissiveRoundTripArgs : IEquatable<PermissiveRoundTripArgs>
    {
        private string value = string.Empty;

        public PermissiveRoundTripArgs()
        {
        }

        public PermissiveRoundTripArgs(string value)
        {
            this.value = value;
        }

        public string Value
        {
            get => value;
            set => this.value = value + "-roundtrip";
        }

        public bool Equals(PermissiveRoundTripArgs? other)
        {
            return other is not null;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as PermissiveRoundTripArgs);
        }

        public override int GetHashCode()
        {
            return 0;
        }
    }

    public sealed class PermissiveRoundTripCallback
    {
        public static ValueTask HandleAsync(TimerTick<PermissiveRoundTripArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public enum ComplexTimerMode
    {
        Slow,
        Fast
    }

    public sealed record ComplexNestedArgs(string Label, ComplexTimerMode Mode);

    public sealed record ComplexTimerArgs(
        int[] Numbers,
        List<string> Names,
        List<ComplexNestedArgs> Children,
        ComplexTimerMode Mode,
        int? MaybeCount,
        sbyte SignedByte,
        ushort UnsignedShort,
        uint UnsignedInt,
        ulong UnsignedLong,
        char Code);

    public sealed class ComplexTimerCallback
    {
        public static ValueTask HandleAsync(TimerTick<ComplexTimerArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public sealed class NullableIntCallback
    {
        public static ValueTask HandleAsync(TimerTick<int?> tick)
        {
            _ = tick;
            return default;
        }
    }

    public static TheoryData<Type, Type, object, string> UnsupportedTimerArgsCases()
    {
        return new TheoryData<Type, Type, object, string>
        {
            { typeof(ObjectRootCallback), typeof(object), new object(), "object" },
            { typeof(ObjectMemberCallback), typeof(ObjectMemberArgs), new ObjectMemberArgs(new ComplexNestedArgs("value", ComplexTimerMode.Fast)), "object" },
            { typeof(ObjectArrayCallback), typeof(ObjectArrayArgs), new ObjectArrayArgs([new ComplexNestedArgs("value", ComplexTimerMode.Fast)]), "object" },
            { typeof(ObjectListCallback), typeof(ObjectListArgs), new ObjectListArgs([new ComplexNestedArgs("value", ComplexTimerMode.Fast)]), "object" },
            { typeof(ObjectDelegateCallback), typeof(ObjectMemberArgs), new ObjectMemberArgs((Func<int>)(() => 1)), "object" },
            { typeof(InterfaceMemberCallback), typeof(InterfaceMemberArgs), new InterfaceMemberArgs(new InterfaceImplementation("value")), "interface" },
            { typeof(AbstractMemberCallback), typeof(AbstractMemberArgs), new AbstractMemberArgs(new ConcreteAbstractValue("value")), "abstract" },
            { typeof(FieldBearingCallback), typeof(FieldBearingArgs), new FieldBearingArgs { Count = 7 }, "field" }
        };
    }

    public sealed class ObjectRootCallback
    {
        public static ValueTask HandleAsync(TimerTick<object> tick)
        {
            _ = tick;
            return default;
        }
    }

    public sealed record ObjectMemberArgs(object Value);

    public sealed class ObjectMemberCallback
    {
        public static ValueTask HandleAsync(TimerTick<ObjectMemberArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public sealed record ObjectArrayArgs(object[] Values);

    public sealed class ObjectArrayCallback
    {
        public static ValueTask HandleAsync(TimerTick<ObjectArrayArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public sealed record ObjectListArgs(List<object> Values);

    public sealed class ObjectListCallback
    {
        public static ValueTask HandleAsync(TimerTick<ObjectListArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public sealed class ObjectDelegateCallback
    {
        public static ValueTask HandleAsync(TimerTick<ObjectMemberArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public interface IInterfaceValue
    {
        string Value { get; }
    }

    public sealed record InterfaceImplementation(string Value) : IInterfaceValue;

    public sealed record InterfaceMemberArgs(IInterfaceValue Value);

    public sealed class InterfaceMemberCallback
    {
        public static ValueTask HandleAsync(TimerTick<InterfaceMemberArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public abstract record AbstractValue(string Value);

    public sealed record ConcreteAbstractValue(string Value) : AbstractValue(Value);

    public sealed record AbstractMemberArgs(AbstractValue Value);

    public sealed class AbstractMemberCallback
    {
        public static ValueTask HandleAsync(TimerTick<AbstractMemberArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public sealed class CyclicTimerArgs
    {
        public string? Name { get; set; }

        public CyclicTimerArgs? Next { get; set; }
    }

    public sealed class CyclicTimerCallback
    {
        public static ValueTask HandleAsync(TimerTick<CyclicTimerArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    public sealed class FieldBearingArgs
    {
        public int Count;
    }

    public sealed class FieldBearingCallback
    {
        public static ValueTask HandleAsync(TimerTick<FieldBearingArgs> tick)
        {
            _ = tick;
            return default;
        }
    }

    [Fact]
    public async Task CreateOnceTimerAsync_rejects_null_root_with_unsupported_declared_members()
    {
        await using var fixture = TimerFixture.Create(typeof(ObjectMemberCallback));
        var backend = new LakonaTimerBackend();
        using var scope = LakonaTimerExecutionScope.Enter(backend, fixture.Lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LakonaTimer.CreateOnceTimerAsync<ObjectMemberCallback, ObjectMemberArgs>(
                TimeSpan.Zero,
                nameof(ObjectMemberCallback.HandleAsync),
                args: null!,
                CancellationToken.None));

        Assert.Contains("object", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Descriptors);
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

    private static HotfixRuntimeSnapshot CreateSnapshotForAssembly(Assembly assembly)
    {
        var table = new HotfixDispatchTable(1, Array.Empty<HotfixMethodBinding>());
        var services = new ServiceCollection().BuildServiceProvider();
        return new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
            EmptyHotfixFeatureCommandInvoker.Instance,
            services,
            table,
            services,
            assembly,
            loadContext: null,
            sourceVersion: "test",
            sourceKind: "test",
            sourcePath: null,
            ownsRuntimeResources: true,
            onRetired: null);
    }

    private static async ValueTask<TimerId> InvokeCreateOnceTimerAsync(
        Type callbackType,
        Type argsType,
        object args)
    {
        var method = typeof(LakonaTimer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(static method => method.Name == nameof(LakonaTimer.CreateOnceTimerAsync))
            .MakeGenericMethod(callbackType, argsType);
        object? result;
        try
        {
            result = method.Invoke(
                obj: null,
                [TimeSpan.Zero, "HandleAsync", args, CancellationToken.None]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        return await ((ValueTask<TimerId>)result!).ConfigureAwait(false);
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

    private static void ReplaceStagedTimerId(
        ILakonaTimerBackend stagingBackend,
        TimerId stagedTimerId,
        TimerId replacementTimerId)
    {
        var descriptorsField = stagingBackend.GetType().GetField(
            "descriptors",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var descriptors = (IDictionary<TimerId, LakonaTimerDescriptor>)descriptorsField.GetValue(stagingBackend)!;
        var stagedDescriptor = descriptors[stagedTimerId];
        descriptors.Remove(stagedTimerId);
        descriptors.Add(
            replacementTimerId,
            new LakonaTimerDescriptor(
                replacementTimerId,
                stagedDescriptor.CallbackAssemblyName,
                stagedDescriptor.CallbackFullName,
                stagedDescriptor.MethodName,
                stagedDescriptor.ArgsAssemblyName,
                stagedDescriptor.ArgsFullName,
                stagedDescriptor.SerializerId,
                stagedDescriptor.JsonPayload,
                stagedDescriptor.NextDueAtUtc,
                stagedDescriptor.Period,
                stagedDescriptor.Generation));
    }
}
