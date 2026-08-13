using Game.Unity.MMO.Server.App.World;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Shared.Interfaces;

namespace Game.Unity.MMO.Server.Hotfix.World;

[HotfixBehaviorOf(typeof(ZoneActor))]
public sealed partial class ZoneBehavior
{
    private const int RespawnTickCount = 30;
    private readonly ZoneNotifier _notifier;

    public ZoneBehavior(ZoneNotifier notifier) => _notifier = notifier;

    [ActorStart]
    public async ValueTask StartAsync(ZoneActor self, ActorStartCall call)
    {
        EnsureMonsters(self);
        if (!self.SimulationTimerId.IsValid)
        {
            self.SimulationTimerId = await LakonaTimer.CreatePeriodicTimerAsync(
                static (ZoneTimerCallbacks callbacks) => callbacks.TickAsync,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(WorldProtocol.TickIntervalSeconds),
                new ZoneTimerArgs { ZoneId = WorldProtocol.DefaultZoneId },
                call.CancellationToken).ConfigureAwait(false);
        }
    }

    [ActorStop]
    public async ValueTask StopAsync(ZoneActor self, ActorStopCall call)
    {
        var timerId = self.SimulationTimerId;
        self.SimulationTimerId = default;
        if (timerId.IsValid)
        {
            await LakonaTimer.DestroyTimerAsync(timerId, call.CleanupCancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask<ZoneEnterResult> EnterAsync(
        ZoneActor self,
        ZoneEnterRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var characterCount = self.Entities.Values.Count(static entity => !entity.IsMonster && entity.Alive);
        if (!self.Entities.ContainsKey(request.CharacterId) && characterCount >= WorldProtocol.MaxCharacters)
        {
            return new ValueTask<ZoneEnterResult>(new ZoneEnterResult
            {
                Accepted = false,
                Message = "The zone has reached its character capacity."
            });
        }

        if (!self.Entities.TryGetValue(request.CharacterId, out var character))
        {
            var spawnIndex = characterCount;
            character = new ZoneEntity
            {
                EntityId = request.CharacterId,
                Name = request.CharacterName,
                X = -6f + spawnIndex % 6 * 2f,
                Y = -4f + spawnIndex / 6 * 2f,
                Health = WorldProtocol.CharacterMaxHealth,
                MaxHealth = WorldProtocol.CharacterMaxHealth
            };
            self.Entities.Add(character.EntityId, character);
        }

        character.Name = request.CharacterName;
        character.Session = request.Session;
        character.HasSession = true;
        character.Alive = true;
        if (character.Health <= 0) character.Health = character.MaxHealth;

        return new ValueTask<ZoneEnterResult>(new ZoneEnterResult
        {
            Accepted = true,
            Message = "Entered the authoritative world.",
            Snapshot = BuildInterestSnapshot(self, character)
        });
    }

    public ValueTask SubmitCommandAsync(
        ZoneActor self,
        ZoneCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (!self.Entities.TryGetValue(request.CharacterId, out var character) ||
            character.IsMonster || !character.HasSession ||
            character.Session != request.Session ||
            request.Command.Sequence <= character.LastCommandSequence)
        {
            return default;
        }

        character.LastCommandSequence = request.Command.Sequence;
        var length = MathF.Sqrt(request.Command.MoveX * request.Command.MoveX + request.Command.MoveY * request.Command.MoveY);
        if (length > 1f)
        {
            character.MoveX = request.Command.MoveX / length;
            character.MoveY = request.Command.MoveY / length;
        }
        else
        {
            character.MoveX = request.Command.MoveX;
            character.MoveY = request.Command.MoveY;
        }

        if (MathF.Abs(character.MoveX) > 0.001f || MathF.Abs(character.MoveY) > 0.001f)
        {
            character.FacingX = character.MoveX;
            character.FacingY = character.MoveY;
        }

        character.PendingAttackTargetId = request.Command.AttackTargetId ?? "";
        return default;
    }

    public ValueTask LeaveAsync(
        ZoneActor self,
        ZoneLeaveRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (self.Entities.TryGetValue(request.CharacterId, out var character) &&
            !character.IsMonster && character.Session == request.Session)
        {
            self.Entities.Remove(request.CharacterId);
        }

        return default;
    }

    public ValueTask TickAsync(
        ZoneActor self,
        ZoneTickRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        self.ServerTick++;
        SimulateCharacters(self);
        SimulateMonsters(self);
        PublishInterestSnapshots(self);
        return default;
    }

    private static void SimulateCharacters(ZoneActor self)
    {
        foreach (var character in self.Entities.Values.Where(static entity => !entity.IsMonster))
        {
            if (!character.Alive)
            {
                TickRespawn(character, -6f, -4f);
                continue;
            }

            Move(character, character.MoveX, character.MoveY, WorldProtocol.CharacterSpeed);
            ApplyPendingAttack(self, character);
        }
    }

    private static void SimulateMonsters(ZoneActor self)
    {
        foreach (var monster in self.Entities.Values.Where(static entity => entity.IsMonster))
        {
            if (!monster.Alive)
            {
                TickRespawn(monster, monster.EntityId.EndsWith("1", StringComparison.Ordinal) ? 5f : 9f, 3f);
                continue;
            }

            var target = self.Entities.Values
                .Where(static entity => !entity.IsMonster && entity.Alive && entity.HasSession)
                .OrderBy(entity => DistanceSquared(monster, entity))
                .FirstOrDefault();
            if (target is null) continue;

            var dx = target.X - monster.X;
            var dy = target.Y - monster.Y;
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance > 1.4f)
            {
                Move(monster, dx / distance, dy / distance, WorldProtocol.MonsterSpeed);
            }
            else if (self.ServerTick % 10 == 0)
            {
                Damage(target, 8);
            }
        }
    }

    private static void ApplyPendingAttack(ZoneActor self, ZoneEntity attacker)
    {
        var targetId = attacker.PendingAttackTargetId;
        attacker.PendingAttackTargetId = "";
        if (string.IsNullOrWhiteSpace(targetId) ||
            !self.Entities.TryGetValue(targetId, out var target) ||
            !target.Alive ||
            self.ServerTick < attacker.NextAttackTick ||
            DistanceSquared(attacker, target) > WorldProtocol.AttackRange * WorldProtocol.AttackRange)
        {
            return;
        }

        attacker.NextAttackTick = self.ServerTick + WorldProtocol.AttackCooldownTicks;
        Damage(target, WorldProtocol.AttackDamage);
    }

    private static void Damage(ZoneEntity target, int amount)
    {
        target.Health = Math.Max(0, target.Health - amount);
        if (target.Health == 0)
        {
            target.Alive = false;
            target.RespawnTicks = RespawnTickCount;
            target.MoveX = 0f;
            target.MoveY = 0f;
        }
    }

    private static void TickRespawn(ZoneEntity entity, float spawnX, float spawnY)
    {
        if (--entity.RespawnTicks > 0) return;
        entity.X = spawnX;
        entity.Y = spawnY;
        entity.Health = entity.MaxHealth;
        entity.Alive = true;
    }

    private static void Move(ZoneEntity entity, float x, float y, float speed)
    {
        entity.X = Math.Clamp(entity.X + x * speed * WorldProtocol.TickIntervalSeconds, -WorldProtocol.WorldHalfExtent, WorldProtocol.WorldHalfExtent);
        entity.Y = Math.Clamp(entity.Y + y * speed * WorldProtocol.TickIntervalSeconds, -WorldProtocol.WorldHalfExtent, WorldProtocol.WorldHalfExtent);
        if (MathF.Abs(x) > 0.001f || MathF.Abs(y) > 0.001f)
        {
            entity.FacingX = x;
            entity.FacingY = y;
        }
    }

    private void PublishInterestSnapshots(ZoneActor self)
    {
        foreach (var character in self.Entities.Values.Where(static entity => !entity.IsMonster && entity.HasSession))
        {
            _notifier.Snapshot(character.Session, BuildInterestSnapshot(self, character));
        }
    }

    private static WorldSnapshot BuildInterestSnapshot(ZoneActor self, ZoneEntity observer)
    {
        var radiusSquared = WorldProtocol.InterestRadius * WorldProtocol.InterestRadius;
        return new WorldSnapshot
        {
            ZoneId = self.Context.Key,
            ServerTick = self.ServerTick,
            TickIntervalSeconds = WorldProtocol.TickIntervalSeconds,
            WorldHalfExtent = WorldProtocol.WorldHalfExtent,
            Entities = self.Entities.Values
                .Where(entity => ReferenceEquals(entity, observer) || DistanceSquared(observer, entity) <= radiusSquared)
                .OrderBy(static entity => entity.EntityId, StringComparer.Ordinal)
                .Select(static entity => new EntityState
                {
                    EntityId = entity.EntityId,
                    Name = entity.Name,
                    Kind = entity.IsMonster ? EntityKind.Monster : EntityKind.Character,
                    X = entity.X,
                    Y = entity.Y,
                    FacingX = entity.FacingX,
                    FacingY = entity.FacingY,
                    Health = entity.Health,
                    MaxHealth = entity.MaxHealth,
                    Alive = entity.Alive,
                    LastProcessedCommandSequence = entity.LastCommandSequence
                }).ToList()
        };
    }

    private static float DistanceSquared(ZoneEntity left, ZoneEntity right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return dx * dx + dy * dy;
    }

    private static void EnsureMonsters(ZoneActor self)
    {
        AddMonster(self, "monster-slime-1", "Slime", 5f, 3f);
        AddMonster(self, "monster-slime-2", "Slime", 9f, -3f);
        AddMonster(self, "monster-wolf-1", "Wolf", -1f, 8f);
        AddMonster(self, "monster-slime-3", "Slime", -14f, 5f);
        AddMonster(self, "monster-wolf-2", "Wolf", 16f, 12f);
        AddMonster(self, "monster-golem-1", "Golem", -20f, -10f);
        AddMonster(self, "monster-golem-2", "Golem", 24f, -16f);
        AddMonster(self, "monster-wolf-3", "Wolf", 4f, 22f);
    }

    private static void AddMonster(ZoneActor self, string id, string name, float x, float y)
    {
        if (self.Entities.ContainsKey(id)) return;
        self.Entities.Add(id, new ZoneEntity
        {
            EntityId = id,
            Name = name,
            IsMonster = true,
            X = x,
            Y = y,
            Health = WorldProtocol.MonsterMaxHealth,
            MaxHealth = WorldProtocol.MonsterMaxHealth
        });
    }
}
