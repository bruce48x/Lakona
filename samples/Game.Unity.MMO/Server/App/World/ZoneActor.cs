using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Sessions;

namespace Game.Unity.MMO.Server.App.World;

public readonly record struct ZoneId(string Value);

public sealed class ZoneActor : Actor<ZoneId>
{
    internal readonly Dictionary<string, ZoneEntity> Entities = new(StringComparer.Ordinal);
    internal TimerId SimulationTimerId;
    internal long ServerTick;
}

public sealed class ZoneEntity
{
    public string EntityId { get; set; } = "";
    public string Name { get; set; } = "";
    public GameSessionKey Session { get; set; }
    public bool HasSession { get; set; }
    public bool IsMonster { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float MoveX { get; set; }
    public float MoveY { get; set; }
    public float FacingX { get; set; } = 1f;
    public float FacingY { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public bool Alive { get; set; } = true;
    public long LastCommandSequence { get; set; }
    public string PendingAttackTargetId { get; set; } = "";
    public int RespawnTicks { get; set; }
}
