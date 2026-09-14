using System.Reflection;
using Lakona.Game.Server.Hotfix.Timers;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class LakonaTimerAbstractionsTests
{
    private sealed class UnhostedActor : Lakona.Game.Server.Actors.Actor<string>;
    [Fact]
    public void Timer_creation_is_exclusively_actor_owned()
    {
        Assert.Null(typeof(ActorTimer).Assembly.GetType("Lakona.Game.Server.Hotfix.Timers.LakonaTimer"));
        Assert.Equal(["CreateOnceTimer", "CreatePeriodicTimer", "DestroyTimer"], typeof(ActorTimer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(method => method.Name).Order());
        Assert.Equal(typeof(TimerId), typeof(ILakonaTimerBackend).GetMethod("CreateTimer")!.ReturnType);
        Assert.Equal(typeof(void), typeof(ILakonaTimerBackend).GetMethod("DestroyTimer")!.ReturnType);
        Assert.All(typeof(ActorTimer).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly),
            method => Assert.Equal(method.Name == "DestroyTimer" ? typeof(void) : typeof(TimerId), method.ReturnType));
        Assert.Null(typeof(ActorTimer).Assembly.GetType("Lakona.Game.Server.Hotfix.Abstractions.HotfixTimerAttribute"));
        Assert.Null(typeof(ActorTimer).Assembly.GetType("Lakona.Game.Server.Hotfix.Timers.HotfixTimerCallback`1"));
    }

    [Fact]
    public void Destroy_requires_hosted_actor()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new UnhostedActor().DestroyTimer(TimerId.FromGuid(Guid.NewGuid()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TimerId_default_value_is_invalid()
    {
        var timerId = default(TimerId);

        Assert.False(timerId.IsValid);
        Assert.Equal("invalid", timerId.ToString());
    }

    [Fact]
    public void TimerId_does_not_expose_a_public_valid_id_factory()
    {
        var publicFactories = typeof(TimerId)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == typeof(TimerId))
            .ToArray();

        Assert.Empty(publicFactories);
    }

    [Fact]
    public void TimerId_rejects_empty_guid_from_internal_factory()
    {
        Assert.Throws<ArgumentException>(() => TimerId.FromGuid(Guid.Empty));
    }

    [Fact]
    public void TimerId_uses_guid_equality_and_hash_code()
    {
        var value = Guid.NewGuid();
        var first = TimerId.FromGuid(value);
        var second = TimerId.FromGuid(value);
        var other = TimerId.FromGuid(Guid.NewGuid());

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, other);
        Assert.False(first == other);
        Assert.True(first != other);
    }

    [Fact]
    public void TimerId_valid_value_formats_as_invariant_guid_d()
    {
        var value = Guid.Parse("7d9a16f6-1f66-4d01-a861-85081ce0ba4e");
        var timerId = TimerId.FromGuid(value);

        Assert.Equal(value.ToString("D", System.Globalization.CultureInfo.InvariantCulture), timerId.ToString());
    }

    [Fact]
    public void TimerTick_exposes_expected_public_generic_shape()
    {
        var type = typeof(TimerTick<TimerArgs>);

        Assert.True(type.IsGenericType);
        Assert.True(type.IsPublic);
        Assert.True(type.IsClass);
        Assert.False(type.IsValueType);
        Assert.True(type.IsSealed);
        Assert.Equal("TimerTick`1", type.GetGenericTypeDefinition().Name);
        AssertTimerTickProperty<TimerId>(type, nameof(TimerTick<TimerArgs>.TimerId));
        AssertTimerTickProperty<TimerArgs>(type, nameof(TimerTick<TimerArgs>.Args));
        AssertTimerTickProperty<IServiceProvider>(type, nameof(TimerTick<TimerArgs>.Services));
        AssertTimerTickProperty<DateTimeOffset>(type, nameof(TimerTick<TimerArgs>.DueAtUtc));
        AssertTimerTickProperty<DateTimeOffset>(type, nameof(TimerTick<TimerArgs>.ObservedAtUtc));
        AssertTimerTickProperty<CancellationToken>(type, nameof(TimerTick<TimerArgs>.CancellationToken));
    }

    [Fact]
    public void TimerTick_rejects_null_services()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TimerTick<TimerArgs>(
                TimerId.FromGuid(Guid.NewGuid()),
                new TimerArgs("args"),
                null!,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                CancellationToken.None));
    }

    private static void AssertTimerTickProperty<TProperty>(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        Assert.Equal(typeof(TProperty), property.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.Null(property.SetMethod);
    }
    private sealed record TimerArgs(string Value);
}
