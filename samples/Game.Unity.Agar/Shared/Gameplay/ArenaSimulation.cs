#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Interfaces;
using UnityEngine;
#if NET10_0_OR_GREATER
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
#endif

namespace Shared.Gameplay
{
    public sealed class ArenaSimulationOptions
    {
        public ArenaConfig Arena { get; set; } = ArenaConfig.CreateDefault();
        public float RespawnDelaySeconds { get; set; } = 4f;
        public int MinPlayersToStart { get; set; } = 2;
        public int TargetParticipantCount { get; set; } = 4;
        public bool EnableBots { get; set; } = true;
        public int RestartDelayTicks { get; set; } = 60;
        public float MaxRoundSeconds { get; set; } = 120f;
        public float InitialPlayerMass { get; set; } = 24f;
        public string? FixedRespawnPlayerId { get; set; }
        public float FixedRespawnMass { get; set; }
        public string? MoveSpeedMultiplierPlayerId { get; set; }
        public float MoveSpeedMultiplier { get; set; } = 1f;
        public string? InvertedMoveSpeedPlayerId { get; set; }
        public float InitialMass { get; set; } = 24f;
        public float RespawnMass { get; set; } = 24f;
        public float FoodMassGain { get; set; } = 1.35f;
        public float PlayerConsumeMassGainFactor { get; set; } = 0.88f;
        public float BaseMoveSpeed { get; set; } = 8.8f;
        public float MinMoveSpeed { get; set; } = 3.1f;
        public float MoveSpeedMassFactor { get; set; } = 0.12f;
        public float RadiusMassFactor { get; set; } = 0.18f;
        public float EatMassRatio { get; set; } = 1.15f;
        public int FoodTargetCount { get; set; } = 96;
        public string BotPrefix { get; set; } = "AI";
        public float BotThreatDistance { get; set; } = 12f;
        public float BotFoodWeight { get; set; } = 0.85f;
        public float BotPreyWeight { get; set; } = 1.2f;
        public float BotThreatWeight { get; set; } = 2.2f;
        public float ShrinkStartDelaySeconds { get; set; } = 999f;
        public float ShrinkDurationSeconds { get; set; } = 0f;
        public Vector2 FinalArenaHalfExtents { get; set; } = new(18f, 18f);
        public PickupType[] EnabledPickupTypes { get; set; } =
        {
            PickupType.MassPoint
        };
    }

    public sealed class ArenaPlayerRegistration
    {
        public string PlayerId { get; set; } = string.Empty;
        public float Mass { get; set; }
        public int PreferredSpawnIndex { get; set; } = -1;
        public bool IsBot { get; set; }
        public int BotNumber { get; set; }
    }

    public sealed class ArenaPlayerSnapshot
    {
        public string PlayerId { get; set; } = string.Empty;
        public float Mass { get; set; }
        public int SpawnIndex { get; set; }
        public bool IsBot { get; set; }
        public int BotNumber { get; set; }
    }

    public sealed class ArenaStepResult
    {
        public ArenaStepResult(WorldState worldState, PlayerDead[] deaths, MatchEnd? matchEnd)
        {
            WorldState = worldState;
            Deaths = deaths;
            MatchEnd = matchEnd;
        }

        public WorldState WorldState { get; }
        public PlayerDead[] Deaths { get; }
        public MatchEnd? MatchEnd { get; }
    }

#if NET10_0_OR_GREATER
    [HotfixState]
#endif
    public sealed partial class ArenaSimulation
    {
        private static readonly Vector2 Zero = new(0f, 0f);

        private readonly List<PlayerDead> _pendingDeaths = new();
        private readonly Dictionary<string, ArenaPlayer> _players = new(StringComparer.Ordinal);
        private readonly List<ArenaFood> _foods = new();
        private readonly ArenaSimulationOptions _options;
        private readonly System.Random _random = new();
        private readonly int _respawnDelaySecondsCeiling;
        private MatchEnd? _pendingMatchEnd;
        private int? _restartAtTick;
        private int _tick;
        private float _roundElapsedSeconds;
        private Vector2 _currentArenaHalfExtents;
        private string? _winnerPlayerId;
        private int _nextBotNumber = 1;

        public ArenaSimulation(ArenaSimulationOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _respawnDelaySecondsCeiling = (int)MathF.Ceiling(Math.Max(1f, _options.RespawnDelaySeconds));
            _currentArenaHalfExtents = options.Arena.ArenaHalfExtents;
            EnsureFoodPopulation();
        }

        public int TickCount => _tick;

        public void UpsertPlayer(ArenaPlayerRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(registration.PlayerId))
            {
                throw new ArgumentException("Player id is required.", nameof(registration));
            }

            if (_players.TryGetValue(registration.PlayerId, out var existing))
            {
                SetMass(existing, Math.Max(existing.Mass, GetInitialMass()));
                return;
            }

            var spawnIndex = ClaimSpawnIndex(registration.PreferredSpawnIndex);
            var player = new ArenaPlayer(
                registration.PlayerId,
                spawnIndex,
                Math.Max(NormalizeMass(registration.Mass), GetInitialMass()),
                registration.IsBot,
                registration.BotNumber)
            {
                Position = GetRandomSpawnPosition()
            };
            RefreshPlayerBodyFromMass(player);
            _players.Add(registration.PlayerId, player);

            if (!registration.IsBot)
            {
                RebalanceBots();
                ResetMatchIfNeeded();
            }
        }

        public bool RemovePlayer(string playerId, out ArenaPlayerSnapshot snapshot)
        {
            snapshot = new ArenaPlayerSnapshot();
            if (!_players.Remove(playerId, out var player))
            {
                return false;
            }

            snapshot = CreateSnapshot(player);
            _pendingDeaths.RemoveAll(deadEvent => string.Equals(deadEvent.PlayerId, playerId, StringComparison.Ordinal));

            if (player.IsBot)
            {
                ReleaseBot(player);
            }
            else
            {
                RebalanceBots();
            }

            if (HumanPlayerCount() == 0)
            {
                RemoveAllBots();
                ClearMatchState();
                _foods.Clear();
                _tick = 0;
                EnsureFoodPopulation();
                return true;
            }

            if (_players.Count < _options.MinPlayersToStart ||
                string.Equals(_winnerPlayerId, playerId, StringComparison.Ordinal))
            {
                ClearMatchState();
            }

            return true;
        }

        public bool TryGetPlayerSnapshot(string playerId, out ArenaPlayerSnapshot snapshot)
        {
            if (_players.TryGetValue(playerId, out var player))
            {
                snapshot = CreateSnapshot(player);
                return true;
            }

            snapshot = new ArenaPlayerSnapshot();
            return false;
        }

        public void SubmitInput(InputMessage input)
        {
            if (!_players.TryGetValue(input.PlayerId, out var player) || !player.Alive)
            {
                return;
            }

            player.LastInputTick = input.Tick;
            player.Input = new Vector2(
                Math.Clamp(input.MoveX, -1f, 1f),
                Math.Clamp(input.MoveY, -1f, 1f));
        }

        public ArenaStepResult Tick(float deltaTime)
        {
            return TickCore(deltaTime);
        }

        internal ArenaStepResult TickCore(float deltaTime)
        {
            _tick++;
            _pendingDeaths.Clear();

            if (_players.Count > 0)
            {
                UpdateArenaBounds(deltaTime);
                UpdateBotInputs();
                SimulatePlayers(deltaTime);
                ResolveFoodCollections();
                ResolvePlayerConsumptions();
                EnsureFoodPopulation();
                ResolveMatchLifecycle();
            }

            var result = new ArenaStepResult(
                CreateWorldState(),
                _pendingDeaths.ToArray(),
                _pendingMatchEnd);
            _pendingMatchEnd = null;
            return result;
        }

#if NET10_0_OR_GREATER
        public ArenaStepResult TickWithHotfix(float deltaTime)
        {
            return HotfixDispatch.Invoke<ArenaSimulation, float, ArenaStepResult>(
                nameof(Tick),
                this,
                deltaTime);
        }

        public MatchSettlementResult SettleMatch(WorldState worldState)
        {
            return HotfixDispatch.Invoke<ArenaSimulation, WorldState, MatchSettlementResult>(
                nameof(SettleMatch),
                this,
                worldState);
        }
#endif

        public WorldState CreateWorldState()
        {
            var state = new WorldState
            {
                Tick = _tick,
                RespawnDelaySeconds = _respawnDelaySecondsCeiling,
                ArenaHalfExtentX = _currentArenaHalfExtents.x,
                ArenaHalfExtentY = _currentArenaHalfExtents.y,
                RoundRemainingSeconds = (_options.MaxRoundSeconds > 0f && _winnerPlayerId is null && _players.Count >= _options.MinPlayersToStart)
                    ? (int)MathF.Ceiling(MathF.Max(0f, _options.MaxRoundSeconds - _roundElapsedSeconds))
                    : 0
            };

            foreach (var player in _players.Values.OrderBy(static p => p.PlayerId, StringComparer.Ordinal))
            {
                var moveSpeed = player.Alive ? GetMoveSpeed(player) : 0f;
                state.Players.Add(new PlayerState
                {
                    PlayerId = player.PlayerId,
                    X = player.Position.x,
                    Y = player.Position.y,
                    Vx = player.Velocity.x,
                    Vy = player.Velocity.y,
                    State = GetLifeState(player),
                    Alive = player.Alive,
                    RespawnRemainingSeconds = player.Alive ? 0 : (int)MathF.Ceiling(player.RespawnRemaining),
                    Mass = player.Mass,
                    Radius = player.Radius,
                    MoveSpeed = moveSpeed
                });
            }

            foreach (var food in _foods)
            {
                state.Pickups.Add(new PickupState
                {
                    Type = food.Type,
                    X = food.Position.x,
                    Y = food.Position.y
                });
            }

            return state;
        }

        public void Clear()
        {
            _players.Clear();
            _foods.Clear();
            _pendingDeaths.Clear();
            _pendingMatchEnd = null;
            _restartAtTick = null;
            _winnerPlayerId = null;
            _tick = 0;
            _roundElapsedSeconds = 0f;
            _currentArenaHalfExtents = _options.Arena.ArenaHalfExtents;
            _nextBotNumber = 1;
            EnsureFoodPopulation();
        }

        private void UpdateArenaBounds(float deltaTime)
        {
            if (_players.Count < _options.MinPlayersToStart || _winnerPlayerId is not null)
            {
                _roundElapsedSeconds = 0f;
                _currentArenaHalfExtents = _options.Arena.ArenaHalfExtents;
                return;
            }

            _roundElapsedSeconds += deltaTime;

            var baseHalfExtents = _options.Arena.ArenaHalfExtents;
            var finalHalfExtents = new Vector2(
                MathF.Min(baseHalfExtents.x, MathF.Max(6f, _options.FinalArenaHalfExtents.x)),
                MathF.Min(baseHalfExtents.y, MathF.Max(6f, _options.FinalArenaHalfExtents.y)));

            if (_roundElapsedSeconds <= _options.ShrinkStartDelaySeconds || _options.ShrinkDurationSeconds <= 0f)
            {
                _currentArenaHalfExtents = baseHalfExtents;
                return;
            }

            var progress = Math.Clamp(
                (_roundElapsedSeconds - _options.ShrinkStartDelaySeconds) / _options.ShrinkDurationSeconds,
                0f,
                1f);
            _currentArenaHalfExtents = new Vector2(
                baseHalfExtents.x + ((finalHalfExtents.x - baseHalfExtents.x) * progress),
                baseHalfExtents.y + ((finalHalfExtents.y - baseHalfExtents.y) * progress));
        }

        private void SimulatePlayers(float deltaTime)
        {
            foreach (var player in _players.Values)
            {
                if (!player.Alive)
                {
                    if (player.RespawnRemaining > 0f)
                    {
                        player.RespawnRemaining = MathF.Max(0f, player.RespawnRemaining - deltaTime);
                    }

                    if (player.RespawnRemaining <= 0f)
                    {
                        RespawnPlayer(player);
                    }

                    player.Velocity = Zero;
                    continue;
                }

                var desired = player.Input;
                if (LengthSquared(desired) > 1f)
                {
                    desired = Normalize(desired);
                }

                player.Velocity = desired * GetMoveSpeed(player);
                player.Position += player.Velocity * deltaTime;
                ClampPlayerInsideArena(player);
            }
        }

        private void ResolveFoodCollections()
        {
            for (var foodIndex = 0; foodIndex < _foods.Count; foodIndex++)
            {
                ArenaPlayer? bestCollector = null;
                var bestDistanceSquared = float.MaxValue;
                var food = _foods[foodIndex];

                foreach (var player in _players.Values)
                {
                    if (!player.Alive)
                    {
                        continue;
                    }

                    var collectionRadius = player.Radius + _options.Arena.PickupCollisionRadius;
                    var distanceSquared = LengthSquared(player.Position - food.Position);
                    if (distanceSquared > collectionRadius * collectionRadius || distanceSquared >= bestDistanceSquared)
                    {
                        continue;
                    }

                    bestCollector = player;
                    bestDistanceSquared = distanceSquared;
                }

                if (bestCollector == null)
                {
                    continue;
                }

                ConsumeFood(bestCollector, food);
                _foods[foodIndex] = new ArenaFood
                {
                    Type = food.Type,
                    Position = GetRandomPickupPosition()
                };
            }
        }

        private void ResolvePlayerConsumptions()
        {
            var alivePlayers = _players.Values.Where(static player => player.Alive).ToArray();
            var eatenPlayerIds = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < alivePlayers.Length; i++)
            {
                for (var j = i + 1; j < alivePlayers.Length; j++)
                {
                    var a = alivePlayers[i];
                    var b = alivePlayers[j];
                    if (!a.Alive || !b.Alive || eatenPlayerIds.Contains(a.PlayerId) || eatenPlayerIds.Contains(b.PlayerId))
                    {
                        continue;
                    }

                    if (TryResolveConsumption(a, b, out var eater, out var victim))
                    {
                        ConsumePlayer(eater, victim);
                        eatenPlayerIds.Add(victim.PlayerId);
                    }
                }
            }
        }

        private bool TryResolveConsumption(ArenaPlayer a, ArenaPlayer b, out ArenaPlayer eater, out ArenaPlayer victim)
        {
            eater = null!;
            victim = null!;

            var distance = MathF.Sqrt(LengthSquared(a.Position - b.Position));
            if (distance > MathF.Max(a.Radius, b.Radius) + (MathF.Min(a.Radius, b.Radius) * 0.35f))
            {
                return false;
            }

            if (CanConsumeByMass(a, b))
            {
                eater = a;
                victim = b;
                return true;
            }

            if (CanConsumeByMass(b, a))
            {
                eater = b;
                victim = a;
                return true;
            }

            return false;
        }

        private void ConsumeFood(ArenaPlayer player, ArenaFood food)
        {
            AdjustMass(player, _options.FoodMassGain);
        }

        private void ConsumePlayer(ArenaPlayer eater, ArenaPlayer victim)
        {
            var massGain = MathF.Max(_options.FoodMassGain * 2f, GetConsumableMass(victim) * _options.PlayerConsumeMassGainFactor);
            AdjustMass(eater, massGain);
            SetMass(victim, 0f);

            victim.Alive = false;
            victim.Velocity = Zero;
            victim.Input = Zero;
            victim.RespawnRemaining = Math.Max(1f, _options.RespawnDelaySeconds);
            _pendingDeaths.Add(new PlayerDead
            {
                PlayerId = victim.PlayerId,
                Tick = _tick
            });
        }

        private void ResolveMatchLifecycle()
        {
            if (_restartAtTick is int restartTick && _tick >= restartTick)
            {
                ResetMatch();
                return;
            }

            if (_winnerPlayerId is not null || _players.Count < _options.MinPlayersToStart)
            {
                return;
            }

            if (_options.MaxRoundSeconds <= 0f || _roundElapsedSeconds < _options.MaxRoundSeconds)
            {
                return;
            }

            var winner = _players.Values
                .OrderByDescending(static player => player.Mass)
                .ThenBy(static player => player.PlayerId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (winner is null)
            {
                return;
            }

            _winnerPlayerId = winner.PlayerId;
            _restartAtTick = _tick + _options.RestartDelayTicks;
            _pendingMatchEnd = new MatchEnd
            {
                WinnerPlayerId = _winnerPlayerId,
                Tick = _tick
            };
        }

        private void ResetMatchIfNeeded()
        {
            if (_players.Count >= _options.MinPlayersToStart && _players.Values.All(static p => !p.Alive))
            {
                ResetMatch();
            }
        }

        private void ResetMatch()
        {
            ClearMatchState();
            _foods.Clear();
            EnsureFoodPopulation();

            foreach (var player in _players.Values.OrderBy(static p => p.PlayerId, StringComparer.Ordinal))
            {
                player.Position = GetRandomSpawnPosition(player.PlayerId);
                player.Mass = GetRespawnMass(player);
                RefreshPlayerBodyFromMass(player);
                player.Velocity = Zero;
                player.Input = Zero;
                player.Alive = true;
                player.RespawnRemaining = 0f;
            }
        }

        private void ClearMatchState()
        {
            _winnerPlayerId = null;
            _pendingMatchEnd = null;
            _restartAtTick = null;
            _roundElapsedSeconds = 0f;
            _currentArenaHalfExtents = _options.Arena.ArenaHalfExtents;
        }

        private int ClaimSpawnIndex(int preferredSpawnIndex)
        {
            if (preferredSpawnIndex >= 0 && _players.Values.All(player => player.SpawnIndex != preferredSpawnIndex))
            {
                return preferredSpawnIndex;
            }

            return GetNextSpawnIndex();
        }

        private int GetNextSpawnIndex()
        {
            var used = _players.Values.Select(static player => player.SpawnIndex).ToHashSet();
            var index = 0;
            while (used.Contains(index))
            {
                index++;
            }

            return index;
        }

        private void AdjustMass(ArenaPlayer player, float delta)
        {
            SetMass(player, player.Mass + delta);
        }

        private void SetMass(ArenaPlayer player, float mass)
        {
            player.Mass = NormalizeMass(mass);
            RefreshPlayerBodyFromMass(player);
        }

        private void RespawnPlayer(ArenaPlayer player)
        {
            player.Position = GetRandomSpawnPosition(player.PlayerId);
            player.Mass = GetRespawnMass(player);
            RefreshPlayerBodyFromMass(player);
            player.Velocity = Zero;
            player.Input = Zero;
            player.Alive = true;
            player.RespawnRemaining = 0f;
        }

        private void RefreshPlayerBodyFromMass(ArenaPlayer player)
        {
            player.Radius = GetRadiusForMass(player.Mass);
        }

        private PlayerLifeState GetLifeState(ArenaPlayer player)
        {
            if (!player.Alive)
            {
                return PlayerLifeState.Dead;
            }

            return LengthSquared(player.Input) > 0.001f ? PlayerLifeState.Move : PlayerLifeState.Idle;
        }

        private static float NormalizeMass(float mass)
        {
            return float.IsNaN(mass) || float.IsInfinity(mass) ? 0f : MathF.Max(0f, mass);
        }

        private float GetMoveSpeed(float mass)
        {
            var baselineMass = MathF.Max(1f, _options.InitialMass);
            var extraMass = MathF.Max(0f, mass - baselineMass);
            var slowdown = 1f + (MathF.Sqrt(extraMass) * _options.MoveSpeedMassFactor);
            return MathF.Max(_options.MinMoveSpeed, _options.BaseMoveSpeed / slowdown);
        }

        private float GetInvertedMoveSpeed(float mass)
        {
            var baselineMass = MathF.Max(1f, _options.InitialMass);
            var extraMass = MathF.Max(0f, mass - baselineMass);
            var speedup = 1f + (MathF.Sqrt(extraMass) * _options.MoveSpeedMassFactor);
            return MathF.Max(_options.MinMoveSpeed, _options.BaseMoveSpeed * speedup);
        }

        private float GetMoveSpeed(ArenaPlayer player)
        {
            var speed = IsConfiguredPlayer(player, _options.InvertedMoveSpeedPlayerId)
                ? GetInvertedMoveSpeed(player.Mass)
                : GetMoveSpeed(player.Mass);
            if (IsConfiguredPlayer(player, _options.MoveSpeedMultiplierPlayerId))
            {
                speed *= MathF.Max(0f, _options.MoveSpeedMultiplier);
            }

            return speed;
        }

        private float GetConsumableMass(ArenaPlayer player)
        {
            return MathF.Max(0f, player.Mass - _options.InitialMass);
        }

        private float GetInitialMass()
        {
            return MathF.Max(1f, _options.InitialPlayerMass > 0f ? _options.InitialPlayerMass : _options.InitialMass);
        }

        private float GetRespawnMass(ArenaPlayer player)
        {
            if (!string.IsNullOrWhiteSpace(_options.FixedRespawnPlayerId) &&
                string.Equals(player.PlayerId, _options.FixedRespawnPlayerId, StringComparison.Ordinal))
            {
                return MathF.Max(GetInitialMass(), NormalizeMass(_options.FixedRespawnMass));
            }

            return MathF.Max(1f, _options.RespawnMass > 0f ? _options.RespawnMass : GetInitialMass());
        }

        private static bool IsConfiguredPlayer(ArenaPlayer player, string? configuredPlayerId)
        {
            return !string.IsNullOrWhiteSpace(configuredPlayerId) &&
                   string.Equals(player.PlayerId, configuredPlayerId, StringComparison.Ordinal);
        }

        private float GetRadiusForMass(float mass)
        {
            var baseRadius = MathF.Max(0.4f, _options.Arena.PlayerVisualRadius);
            var baselineMass = MathF.Max(1f, _options.InitialMass);
            var bonusMass = MathF.Max(0f, mass - baselineMass);
            return baseRadius + (MathF.Sqrt(bonusMass) * _options.RadiusMassFactor);
        }

        private static float LengthSquared(Vector2 value)
        {
            return value.x * value.x + value.y * value.y;
        }

        private static Vector2 Normalize(Vector2 value)
        {
            var length = MathF.Sqrt(LengthSquared(value));
            return length <= 0.0001f ? Zero : value / length;
        }

        private Vector2 GetSpawnPosition(int index)
        {
            var insetX = MathF.Max(0.5f, _currentArenaHalfExtents.x - _options.Arena.RespawnInset);
            var insetY = MathF.Max(0.5f, _currentArenaHalfExtents.y - _options.Arena.RespawnInset);
            var points = new[]
            {
                new Vector2(-insetX, -insetY),
                new Vector2(insetX, -insetY),
                new Vector2(-insetX, insetY),
                new Vector2(insetX, insetY),
                new Vector2(0f, -insetY),
                new Vector2(0f, insetY),
                new Vector2(-insetX, 0f),
                new Vector2(insetX, 0f)
            };

            return points[index % points.Length];
        }

        private Vector2 GetRandomSpawnPosition(string? playerIdToIgnore = null)
        {
            var minX = -MathF.Max(0.5f, _currentArenaHalfExtents.x - _options.Arena.RespawnInset);
            var maxX = MathF.Max(0.5f, _currentArenaHalfExtents.x - _options.Arena.RespawnInset);
            var minY = -MathF.Max(0.5f, _currentArenaHalfExtents.y - _options.Arena.RespawnInset);
            var maxY = MathF.Max(0.5f, _currentArenaHalfExtents.y - _options.Arena.RespawnInset);
            var minDistance = MathF.Max(2.8f, _options.Arena.PlayerVisualRadius * 2.4f);

            for (var attempt = 0; attempt < 12; attempt++)
            {
                var candidate = new Vector2(
                    (float)(_random.NextDouble() * (maxX - minX)) + minX,
                    (float)(_random.NextDouble() * (maxY - minY)) + minY);

                var overlaps = _players.Values.Any(player =>
                    player.Alive &&
                    !string.Equals(player.PlayerId, playerIdToIgnore, StringComparison.Ordinal) &&
                    LengthSquared(player.Position - candidate) < minDistance * minDistance);

                if (!overlaps)
                {
                    return candidate;
                }
            }

            return new Vector2(
                (float)(_random.NextDouble() * (maxX - minX)) + minX,
                (float)(_random.NextDouble() * (maxY - minY)) + minY);
        }

        private ArenaFood CreateFood()
        {
            var enabledTypes = _options.EnabledPickupTypes.Length == 0
                ? new[] { PickupType.MassPoint }
                : _options.EnabledPickupTypes;
            return new ArenaFood
            {
                Position = GetRandomPickupPosition(),
                Type = enabledTypes[_random.Next(enabledTypes.Length)]
            };
        }

        private Vector2 GetRandomPickupPosition()
        {
            var minX = -MathF.Max(0.5f, _currentArenaHalfExtents.x - _options.Arena.PickupSpawnInset);
            var maxX = MathF.Max(0.5f, _currentArenaHalfExtents.x - _options.Arena.PickupSpawnInset);
            var minY = -MathF.Max(0.5f, _currentArenaHalfExtents.y - _options.Arena.PickupSpawnInset);
            var maxY = MathF.Max(0.5f, _currentArenaHalfExtents.y - _options.Arena.PickupSpawnInset);

            return new Vector2(
                (float)(_random.NextDouble() * (maxX - minX)) + minX,
                (float)(_random.NextDouble() * (maxY - minY)) + minY);
        }

        private void EnsureFoodPopulation()
        {
            while (_foods.Count < _options.FoodTargetCount)
            {
                _foods.Add(CreateFood());
            }

            if (_foods.Count > _options.FoodTargetCount)
            {
                _foods.RemoveRange(_options.FoodTargetCount, _foods.Count - _options.FoodTargetCount);
            }
        }

        private void UpdateBotInputs()
        {
            var alivePlayers = _players.Values.Where(static player => player.Alive).ToArray();
            foreach (var bot in _players.Values.Where(static player => player.IsBot))
            {
                if (!bot.Alive)
                {
                    bot.Input = Zero;
                    continue;
                }

                var fleeVector = Zero;
                ArenaPlayer? prey = null;
                var bestPreyDistance = float.MaxValue;

                foreach (var candidate in alivePlayers)
                {
                    if (string.Equals(candidate.PlayerId, bot.PlayerId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var offset = candidate.Position - bot.Position;
                    var distanceSquared = LengthSquared(offset);
                    if (candidate.Mass > bot.Mass * 1.12f && distanceSquared <= _options.BotThreatDistance * _options.BotThreatDistance)
                    {
                        var safeDirection = Normalize(bot.Position - candidate.Position);
                        var pressure = 1f / MathF.Max(1f, MathF.Sqrt(distanceSquared));
                        fleeVector += safeDirection * pressure;
                    }

                    if (CanConsumeByMass(bot, candidate) && distanceSquared < bestPreyDistance)
                    {
                        bestPreyDistance = distanceSquared;
                        prey = candidate;
                    }
                }

                var foodTarget = FindNearestFood(bot.Position);
                var desired = fleeVector * _options.BotThreatWeight;
                if (prey is not null)
                {
                    desired += Normalize(prey.Position - bot.Position) * _options.BotPreyWeight;
                }
                else if (foodTarget.HasValue)
                {
                    desired += Normalize(foodTarget.Value - bot.Position) * _options.BotFoodWeight;
                }
                else
                {
                    desired += Normalize(-bot.Position);
                }

                if (LengthSquared(desired) <= 0.0001f)
                {
                    desired = new Vector2(
                        MathF.Sin((_tick * 0.13f) + bot.BotNumber),
                        MathF.Cos((_tick * 0.11f) + bot.BotNumber));
                }

                bot.Input = Normalize(desired);
                bot.LastInputTick = _tick;
            }
        }

        private Vector2? FindNearestFood(Vector2 position)
        {
            if (_foods.Count == 0)
            {
                return null;
            }

            var bestDistance = float.MaxValue;
            var bestPosition = default(Vector2);
            foreach (var food in _foods)
            {
                var distance = LengthSquared(food.Position - position);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestPosition = food.Position;
            }

            return bestDistance < float.MaxValue ? bestPosition : null;
        }

        private bool CanConsumeByMass(ArenaPlayer eater, ArenaPlayer victim)
        {
            return eater.Mass >= victim.Mass * _options.EatMassRatio && eater.Radius > victim.Radius;
        }

        private void ClampPlayerInsideArena(ArenaPlayer player)
        {
            var limitX = MathF.Max(0.5f, _currentArenaHalfExtents.x - player.Radius);
            var limitY = MathF.Max(0.5f, _currentArenaHalfExtents.y - player.Radius);
            player.Position = new Vector2(
                Math.Clamp(player.Position.x, -limitX, limitX),
                Math.Clamp(player.Position.y, -limitY, limitY));
        }

        private void RebalanceBots()
        {
            var humanCount = HumanPlayerCount();
            var desiredBotCount = _options.EnableBots
                ? Math.Max(0, _options.TargetParticipantCount - humanCount)
                : 0;
            var currentBotCount = _players.Values.Count(static player => player.IsBot);

            while (currentBotCount < desiredBotCount)
            {
                AddBot();
                currentBotCount++;
            }

            while (currentBotCount > desiredBotCount)
            {
                var removableBot = _players.Values
                    .Where(static player => player.IsBot)
                    .OrderByDescending(static player => player.BotNumber)
                    .FirstOrDefault();
                if (removableBot is null)
                {
                    break;
                }

                RemoveBot(removableBot.PlayerId);
                currentBotCount--;
            }
        }

        private int HumanPlayerCount()
        {
            return _players.Values.Count(static player => !player.IsBot);
        }

        private void AddBot()
        {
            var botName = $"{_options.BotPrefix}{_nextBotNumber:D2}";
            while (_players.ContainsKey(botName))
            {
                _nextBotNumber++;
                botName = $"{_options.BotPrefix}{_nextBotNumber:D2}";
            }

            var botNumber = _nextBotNumber;
            _nextBotNumber++;
            UpsertPlayer(new ArenaPlayerRegistration
            {
                PlayerId = botName,
                PreferredSpawnIndex = GetNextSpawnIndex(),
                IsBot = true,
                BotNumber = botNumber
            });
        }

        private void RemoveBot(string playerId)
        {
            if (!_players.Remove(playerId, out var bot))
            {
                return;
            }

            ReleaseBot(bot);
            _pendingDeaths.RemoveAll(deadEvent => string.Equals(deadEvent.PlayerId, bot.PlayerId, StringComparison.Ordinal));
        }

        private void RemoveAllBots()
        {
            var botIds = _players.Values.Where(static player => player.IsBot).Select(static player => player.PlayerId).ToArray();
            foreach (var botId in botIds)
            {
                RemoveBot(botId);
            }
        }

        private void ReleaseBot(ArenaPlayer bot)
        {
            _pendingDeaths.RemoveAll(deadEvent => string.Equals(deadEvent.PlayerId, bot.PlayerId, StringComparison.Ordinal));
        }

        private static ArenaPlayerSnapshot CreateSnapshot(ArenaPlayer player)
        {
            return new ArenaPlayerSnapshot
            {
                PlayerId = player.PlayerId,
                Mass = player.Mass,
                SpawnIndex = player.SpawnIndex,
                IsBot = player.IsBot,
                BotNumber = player.BotNumber
            };
        }

    }
}
