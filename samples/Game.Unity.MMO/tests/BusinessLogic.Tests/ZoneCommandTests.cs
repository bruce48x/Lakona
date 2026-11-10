using System.Reflection;
using Game.Unity.MMO.Server.App.World;
using Game.Unity.MMO.Server.Hotfix.World;
using Lakona.Game.Server.Sessions;
using MemoryPack;
using Shared.Interfaces;
using Xunit;

namespace BusinessLogic.Tests;

public sealed class ZoneCommandTests
{
    public static IEnumerable<object[]> InvalidMovement()
    {
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            yield return [value, 0.5f];
            yield return [0.5f, value];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidMovement))]
    public async Task Invalid_command_preserves_state_and_sequence_and_allows_next_valid_command(float x, float y)
    {
        var (zone, behavior, character, _) = CreateZone();
        character.MoveX = 0.25f;
        character.MoveY = -0.5f;
        character.FacingX = 0.6f;
        character.FacingY = 0.8f;
        character.PendingAttackTargetId = "previous-target";
        character.NextAttackTick = 15;
        var before = Snapshot(character);
        // Exercise the actual DTO codec as well as the business boundary.
        var invalid = MemoryPackSerializer.Deserialize<CharacterCommand>(MemoryPackSerializer.Serialize(
            new CharacterCommand { Sequence = long.MaxValue, MoveX = x, MoveY = y, AttackTargetId = "target" }))!;
        await behavior.SubmitCommandAsync(zone, Request(character, invalid));
        Assert.Equal(before, Snapshot(character));

        await behavior.SubmitCommandAsync(zone, Request(character,
            new CharacterCommand { Sequence = 11, MoveX = 0.6f, MoveY = 0.8f }));
        Assert.Equal(11, character.LastCommandSequence);
        Assert.Equal(0.6f, character.MoveX);
        Assert.Equal(0.8f, character.MoveY);
        Assert.Equal("", character.PendingAttackTargetId);
        character.HasSession = false; // Isolate simulation from snapshot transport.
        await behavior.TickAsync(zone, new ZoneTickRequest());
        Assert.True(float.IsFinite(character.X) && float.IsFinite(character.Y));
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(0.3f, 0.4f, 0.3f, 0.4f)]
    [InlineData(1f, 0f, 1f, 0f)]
    [InlineData(3f, 4f, 0.6f, 0.8f)]
    [InlineData(-3f, -4f, -0.6f, -0.8f)]
    [InlineData(float.MaxValue, 0f, 1f, 0f)]
    [InlineData(0f, float.MinValue, 0f, -1f)]
    [InlineData(float.MaxValue, float.MaxValue, 0.70710677f, 0.70710677f)]
    [InlineData(float.MaxValue, float.MinValue, 0.70710677f, -0.70710677f)]
    [InlineData(float.Epsilon, 0f, float.Epsilon, 0f)]
    public async Task Finite_movement_preserves_direction_and_caps_speed(float x, float y, float expectedX, float expectedY)
    {
        var (zone, behavior, character, _) = CreateZone();
        await behavior.SubmitCommandAsync(zone, Request(character,
            new CharacterCommand { Sequence = 11, MoveX = x, MoveY = y }));
        Assert.InRange(Math.Abs(character.MoveX - expectedX), 0, 0.000001f);
        Assert.InRange(Math.Abs(character.MoveY - expectedY), 0, 0.000001f);
        Assert.True(float.IsFinite(character.MoveX) && float.IsFinite(character.MoveY));
        Assert.Equal(11, character.LastCommandSequence);
        if (x == float.Epsilon) Assert.Equal(float.Epsilon, character.MoveX);
        character.HasSession = false;
        await behavior.TickAsync(zone, new ZoneTickRequest());
        var travel = Math.Sqrt((double)character.X * character.X + (double)character.Y * character.Y);
        Assert.InRange(travel, 0, WorldProtocol.CharacterSpeed * WorldProtocol.TickIntervalSeconds + 0.000001);
    }

    [Theory]
    [InlineData(0f, 0f, 1f, 0f, true)]
    [InlineData(0f, 0f, WorldProtocol.AttackRange, 0f, true)]
    [InlineData(0f, 0f, WorldProtocol.AttackRange + 0.01f, 0f, false)]
    [InlineData(0f, 0f, float.NaN, 0f, false)]
    [InlineData(0f, 0f, 0f, float.NaN, false)]
    [InlineData(float.NaN, 0f, 0f, 0f, false)]
    [InlineData(0f, float.NaN, 0f, 0f, false)]
    [InlineData(0f, 0f, float.PositiveInfinity, 0f, false)]
    [InlineData(0f, 0f, 0f, float.NegativeInfinity, false)]
    [InlineData(0f, 0f, float.MaxValue, float.MaxValue, false)]
    public async Task Attack_requires_a_finite_distance_within_range(float ax, float ay, float tx, float ty, bool allowed)
    {
        var (zone, behavior, character, target) = CreateZone();
        character.X = ax;
        character.Y = ay;
        target.X = tx;
        target.Y = ty;
        await behavior.SubmitCommandAsync(zone, Request(character,
            new CharacterCommand { Sequence = 11, AttackTargetId = target.EntityId }));
        character.HasSession = false;
        await behavior.TickAsync(zone, new ZoneTickRequest());
        Assert.Equal(100 - (allowed ? WorldProtocol.AttackDamage : 0), target.Health);
        Assert.Equal(allowed ? 1 + WorldProtocol.AttackCooldownTicks : 0, character.NextAttackTick);
        Assert.Equal("", character.PendingAttackTargetId);
    }

    [Theory]
    [MemberData(nameof(InvalidMovement))]
    public async Task Invalid_movement_cannot_queue_a_remote_attack(float x, float y)
    {
        var (zone, behavior, character, target) = CreateZone();
        target.X = 50;
        await behavior.SubmitCommandAsync(zone, Request(character,
            new CharacterCommand { Sequence = 11, MoveX = x, MoveY = y, AttackTargetId = target.EntityId }));
        character.HasSession = false;
        await behavior.TickAsync(zone, new ZoneTickRequest());
        Assert.Equal(100, target.Health);
        Assert.Equal(0f, character.X);
        Assert.Equal(0f, character.Y);
        Assert.Equal(10, character.LastCommandSequence);
    }

    private static ZoneCommandRequest Request(ZoneEntity character, CharacterCommand command) => new()
    { CharacterId = character.EntityId, Session = character.Session, Command = command };

    private static object Snapshot(ZoneEntity entity) => new
    {
        entity.X, entity.Y, entity.MoveX, entity.MoveY, entity.FacingX, entity.FacingY,
        entity.LastCommandSequence, entity.PendingAttackTargetId, entity.NextAttackTick,
        entity.Health, entity.Alive, entity.HasSession, entity.Session
    };

    private static (ZoneActor, ZoneBehavior, ZoneEntity, ZoneEntity) CreateZone()
    {
        var zone = new ZoneActor();
        // Seed business state only; execute public production behavior methods directly.
        // No runtime hosting, timer or RPC behavior is claimed by these domain tests.
        var entities = (Dictionary<string, ZoneEntity>)typeof(ZoneActor)
            .GetField("Entities", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(zone)!;
        var character = new ZoneEntity
        {
            EntityId = "character", Session = new GameSessionKey("character", "session"),
            HasSession = true, LastCommandSequence = 10, Health = 100, MaxHealth = 100
        };
        var target = new ZoneEntity { EntityId = "target", X = 50, Health = 100, MaxHealth = 100 };
        entities.Add(character.EntityId, character);
        entities.Add(target.EntityId, target);
        return (zone, new ZoneBehavior(new ZoneNotifier(new NoNotifications())), character, target);
    }

    private sealed class NoNotifications : IClientNotifications
    {
        public ClientNotificationTarget<TCallback> ForSession<TCallback>(GameSessionKey session) where TCallback : class =>
            throw new InvalidOperationException("Network delivery is outside these domain tests.");
    }
}
