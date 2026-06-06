using System.Numerics;
using Shared.Interfaces;

namespace Agar.MixedTransport.Server.Services;

public sealed class BattleWorld : IAsyncDisposable
{
    private const float WorldWidth = 3200f;
    private const float WorldHeight = 3200f;
    private const float FoodRadius = 6f;
    private const float InitialMass = 24f;
    private const float FoodMass = 2.5f;
    private const int TargetFoodCount = 180;
    private const float TickSeconds = 0.05f;
    private readonly object _gate = new();
    private readonly Dictionary<string, PlayerState> _players = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IBattleNotifications> _subscribers = new(StringComparer.Ordinal);
    private readonly List<FoodPellet> _food = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private long _tick;

    public BattleWorld()
    {
        lock (_gate)
        {
            RefillFood_NoLock();
        }

        _loop = Task.Run(RunLoopAsync);
    }

    public JoinWorldResult JoinOrRespawn(string playerId, string account)
    {
        lock (_gate)
        {
            if (!_players.TryGetValue(playerId, out var player))
            {
                player = new PlayerState(playerId, account);
                SpawnPlayer_NoLock(player);
                _players[playerId] = player;
            }
            else
            {
                player.Name = account;
                player.LastSeenUtc = DateTimeOffset.UtcNow;
            }

            return new JoinWorldResult(player.PlayerId, WorldWidth, WorldHeight, player.Position.X, player.Position.Y);
        }
    }

    public void UpdateInput(string playerId, float directionX, float directionY)
    {
        lock (_gate)
        {
            if (!_players.TryGetValue(playerId, out var player))
                return;

            var direction = new Vector2(directionX, directionY);
            if (direction.LengthSquared() > 1f)
                direction = Vector2.Normalize(direction);

            player.Direction = direction;
            player.LastSeenUtc = DateTimeOffset.UtcNow;
        }
    }

    public void RegisterSubscriber(string playerId, IBattleNotifications notifications)
    {
        lock (_gate)
        {
            _subscribers[playerId] = notifications;
        }
    }

    public void UnregisterSubscriber(string playerId)
    {
        lock (_gate)
        {
            _subscribers.Remove(playerId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }

    private async Task RunLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TickSeconds));
        while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
        {
            List<(IBattleNotifications notifications, WorldSnapshotReply snapshot)> deliveries;
            lock (_gate)
            {
                _tick++;
                UpdatePlayers_NoLock();
                ResolveFoodCollisions_NoLock();
                ResolvePlayerCollisions_NoLock();
                DespawnIdlePlayers_NoLock();
                RefillFood_NoLock();
                deliveries = BuildSnapshots_NoLock();
            }

            foreach (var delivery in deliveries)
            {
                try
                {
                    delivery.notifications.OnSnapshot(delivery.snapshot);
                }
                catch
                {
                }
            }
        }
    }

    private List<(IBattleNotifications notifications, WorldSnapshotReply snapshot)> BuildSnapshots_NoLock()
    {
        var deliveries = new List<(IBattleNotifications notifications, WorldSnapshotReply snapshot)>(_subscribers.Count);
        foreach (var pair in _subscribers)
        {
            if (!_players.TryGetValue(pair.Key, out var self))
                continue;

            self.LastSeenUtc = DateTimeOffset.UtcNow;
            deliveries.Add((pair.Value, CreateSnapshot_NoLock(self.PlayerId)));
        }

        return deliveries;
    }

    private WorldSnapshotReply CreateSnapshot_NoLock(string playerId)
    {
        var snapshot = new WorldSnapshotReply
        {
            Code = 0,
            Message = "ok",
            Tick = _tick,
            YourPlayerId = playerId,
            WorldWidth = WorldWidth,
            WorldHeight = WorldHeight
        };

        foreach (var player in _players.Values.OrderByDescending(static player => player.Mass))
        {
            snapshot.Players.Add(new PlayerBlobState
            {
                PlayerId = player.PlayerId,
                Name = player.Name,
                X = player.Position.X,
                Y = player.Position.Y,
                Radius = ComputeRadius(player.Mass),
                Mass = player.Mass,
                IsSelf = player.PlayerId == playerId
            });
        }

        foreach (var pellet in _food)
        {
            snapshot.Foods.Add(new FoodState
            {
                X = pellet.Position.X,
                Y = pellet.Position.Y,
                Radius = FoodRadius
            });
        }

        return snapshot;
    }

    private void UpdatePlayers_NoLock()
    {
        foreach (var player in _players.Values)
        {
            if (player.Direction.LengthSquared() <= 0.0001f)
                continue;

            var radius = ComputeRadius(player.Mass);
            var speed = 540f / MathF.Max(1f, MathF.Sqrt(radius));
            player.Position += player.Direction * speed * TickSeconds;
            player.Position = new Vector2(
                Math.Clamp(player.Position.X, radius, WorldWidth - radius),
                Math.Clamp(player.Position.Y, radius, WorldHeight - radius));
        }
    }

    private void ResolveFoodCollisions_NoLock()
    {
        for (var i = _food.Count - 1; i >= 0; i--)
        {
            var pellet = _food[i];
            foreach (var player in _players.Values)
            {
                var radius = ComputeRadius(player.Mass);
                if (Vector2.DistanceSquared(player.Position, pellet.Position) > radius * radius)
                    continue;

                player.Mass += FoodMass;
                _food.RemoveAt(i);
                break;
            }
        }
    }

    private void ResolvePlayerCollisions_NoLock()
    {
        var players = _players.Values.ToArray();
        for (var i = 0; i < players.Length; i++)
        {
            for (var j = i + 1; j < players.Length; j++)
            {
                var left = players[i];
                var right = players[j];
                var leftRadius = ComputeRadius(left.Mass);
                var rightRadius = ComputeRadius(right.Mass);
                var distance = Vector2.Distance(left.Position, right.Position);

                if (left.Mass > right.Mass * 1.15f && distance < leftRadius - rightRadius * 0.2f)
                {
                    left.Mass += right.Mass * 0.85f;
                    RespawnPlayer_NoLock(right);
                }
                else if (right.Mass > left.Mass * 1.15f && distance < rightRadius - leftRadius * 0.2f)
                {
                    right.Mass += left.Mass * 0.85f;
                    RespawnPlayer_NoLock(left);
                }
            }
        }
    }

    private void DespawnIdlePlayers_NoLock()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-20);
        foreach (var playerId in _players.Values
                     .Where(player => player.LastSeenUtc <= cutoff)
                     .Select(static player => player.PlayerId)
                     .ToArray())
        {
            _players.Remove(playerId);
        }
    }

    private void RefillFood_NoLock()
    {
        while (_food.Count < TargetFoodCount)
        {
            _food.Add(new FoodPellet(RandomPosition()));
        }
    }

    private void SpawnPlayer_NoLock(PlayerState player)
    {
        player.Mass = InitialMass;
        player.Direction = Vector2.Zero;
        player.LastSeenUtc = DateTimeOffset.UtcNow;
        player.Position = RandomPosition();
    }

    private void RespawnPlayer_NoLock(PlayerState player)
    {
        SpawnPlayer_NoLock(player);
    }

    private static float ComputeRadius(float mass)
    {
        return MathF.Sqrt(MathF.Max(4f, mass)) * 4f;
    }

    private static Vector2 RandomPosition()
    {
        return new Vector2(
            Random.Shared.NextSingle() * WorldWidth,
            Random.Shared.NextSingle() * WorldHeight);
    }

    public sealed record JoinWorldResult(string PlayerId, float WorldWidth, float WorldHeight, float SpawnX, float SpawnY);

    private sealed class PlayerState
    {
        public PlayerState(string playerId, string name)
        {
            PlayerId = playerId;
            Name = name;
        }

        public string PlayerId { get; }
        public string Name { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Direction { get; set; }
        public float Mass { get; set; } = InitialMass;
        public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed record FoodPellet(Vector2 Position);
}
