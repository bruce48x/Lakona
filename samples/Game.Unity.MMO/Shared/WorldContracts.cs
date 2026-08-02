#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Lakona.Rpc.Core;
using MemoryPack;

namespace Shared.Interfaces
{
    [RpcService(1, NotificationContract = typeof(IWorldCallback))]
    public interface IWorldService
    {
        [RpcMethod(1)]
        ValueTask<EnterWorldReply> EnterWorldAsync(EnterWorldRequest request);

        [RpcMethod(2)]
        ValueTask SubmitCommandAsync(CharacterCommand command);

        [RpcMethod(3)]
        ValueTask LeaveWorldAsync(LeaveWorldRequest request);
    }

    [RpcNotificationContract(typeof(IWorldService))]
    public interface IWorldCallback
    {
        [RpcNotification(1)]
        void OnWorldSnapshot(WorldSnapshot snapshot);
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class EnterWorldRequest
    {
        [MemoryPackOrder(0)] public string CharacterName { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class EnterWorldReply
    {
        [MemoryPackOrder(0)] public int Code { get; set; }
        [MemoryPackOrder(1)] public string Message { get; set; } = "";
        [MemoryPackOrder(2)] public string CharacterId { get; set; } = "";
        [MemoryPackOrder(3)] public string ZoneId { get; set; } = "";
        [MemoryPackOrder(4)] public WorldSnapshot Snapshot { get; set; } = new WorldSnapshot();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LeaveWorldRequest { }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class CharacterCommand
    {
        [MemoryPackOrder(0)] public long Sequence { get; set; }
        [MemoryPackOrder(1)] public float MoveX { get; set; }
        [MemoryPackOrder(2)] public float MoveY { get; set; }
        [MemoryPackOrder(3)] public string AttackTargetId { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class WorldSnapshot
    {
        [MemoryPackOrder(0)] public string ZoneId { get; set; } = "";
        [MemoryPackOrder(1)] public long ServerTick { get; set; }
        [MemoryPackOrder(2)] public float TickIntervalSeconds { get; set; }
        [MemoryPackOrder(3)] public float WorldHalfExtent { get; set; }
        [MemoryPackOrder(4)] public List<EntityState> Entities { get; set; } = new List<EntityState>();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class EntityState
    {
        [MemoryPackOrder(0)] public string EntityId { get; set; } = "";
        [MemoryPackOrder(1)] public string Name { get; set; } = "";
        [MemoryPackOrder(2)] public EntityKind Kind { get; set; }
        [MemoryPackOrder(3)] public float X { get; set; }
        [MemoryPackOrder(4)] public float Y { get; set; }
        [MemoryPackOrder(5)] public float FacingX { get; set; }
        [MemoryPackOrder(6)] public float FacingY { get; set; }
        [MemoryPackOrder(7)] public int Health { get; set; }
        [MemoryPackOrder(8)] public int MaxHealth { get; set; }
        [MemoryPackOrder(9)] public bool Alive { get; set; }
        [MemoryPackOrder(10)] public long LastProcessedCommandSequence { get; set; }
    }

    public enum EntityKind
    {
        Character = 0,
        Monster = 1
    }

    public static class WorldProtocol
    {
        public const string DefaultZoneId = "greenfield";
        public const float TickIntervalSeconds = 0.1f;
        public const float WorldHalfExtent = 120f;
        public const float InterestRadius = 28f;
        public const float CharacterSpeed = 8f;
        public const float MonsterSpeed = 2f;
        public const float AttackRange = 2.8f;
        public const int AttackCooldownTicks = 7;
        public const int AttackDamage = 20;
        public const int CharacterMaxHealth = 100;
        public const int MonsterMaxHealth = 60;
        public const int MaxCharacters = 100;
    }
}
