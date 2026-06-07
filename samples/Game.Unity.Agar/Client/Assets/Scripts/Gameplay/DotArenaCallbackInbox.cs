#nullable enable

using System.Collections.Generic;
using Shared.Interfaces;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaCallbackInbox
    {
        private readonly object _gate = new();
        private WorldState? _pendingWorldState;
        private readonly Queue<PlayerDead> _pendingDeaths = new();
        private MatchEnd? _pendingMatchEnd;
        private MatchmakingStatusUpdate? _pendingMatchmakingStatus;
        private string? _pendingRealtimeFallbackMessage;
        private string? _pendingDisconnectMessage;

        public void EnqueueWorldState(WorldState worldState)
        {
            lock (_gate)
            {
                _pendingWorldState = CloneWorldState(worldState);
            }
        }

        public void EnqueuePlayerDead(PlayerDead deadEvent)
        {
            lock (_gate)
            {
                _pendingDeaths.Enqueue(new PlayerDead
                {
                    PlayerId = deadEvent.PlayerId,
                    Tick = deadEvent.Tick
                });
            }
        }

        public void EnqueueMatchEnd(MatchEnd matchEnd)
        {
            lock (_gate)
            {
                _pendingMatchEnd = new MatchEnd
                {
                    WinnerPlayerId = matchEnd.WinnerPlayerId,
                    Tick = matchEnd.Tick
                };
            }
        }

        public void EnqueueMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            lock (_gate)
            {
                _pendingMatchmakingStatus = CloneMatchmakingStatus(matchmakingStatus);
            }
        }

        public void EnqueueDisconnected(string? disconnectMessage)
        {
            lock (_gate)
            {
                _pendingDisconnectMessage = disconnectMessage;
            }
        }

        public void EnqueueRealtimeFallback(string message)
        {
            lock (_gate)
            {
                _pendingRealtimeFallbackMessage = message;
            }
        }

        public DrainedCallbacks Drain()
        {
            var deadEvents = new List<PlayerDead>();
            WorldState? worldState;
            MatchEnd? matchEnd;
            MatchmakingStatusUpdate? matchmakingStatus;
            string? realtimeFallbackMessage;
            string? disconnectMessage;

            lock (_gate)
            {
                worldState = _pendingWorldState;
                _pendingWorldState = null;

                while (_pendingDeaths.Count > 0)
                {
                    deadEvents.Add(_pendingDeaths.Dequeue());
                }

                matchEnd = _pendingMatchEnd;
                _pendingMatchEnd = null;

                matchmakingStatus = _pendingMatchmakingStatus;
                _pendingMatchmakingStatus = null;

                realtimeFallbackMessage = _pendingRealtimeFallbackMessage;
                _pendingRealtimeFallbackMessage = null;

                disconnectMessage = _pendingDisconnectMessage;
                _pendingDisconnectMessage = null;
            }

            return new DrainedCallbacks(worldState, deadEvents, matchEnd, matchmakingStatus, realtimeFallbackMessage, disconnectMessage);
        }

        public void Clear()
        {
            lock (_gate)
            {
                _pendingWorldState = null;
                _pendingDeaths.Clear();
                _pendingMatchEnd = null;
                _pendingMatchmakingStatus = null;
                _pendingRealtimeFallbackMessage = null;
                _pendingDisconnectMessage = null;
            }
        }

        private static WorldState CloneWorldState(WorldState source)
        {
            var clone = new WorldState
            {
                Tick = source.Tick,
                RespawnDelaySeconds = source.RespawnDelaySeconds,
                ArenaHalfExtentX = source.ArenaHalfExtentX,
                ArenaHalfExtentY = source.ArenaHalfExtentY,
                RoundRemainingSeconds = source.RoundRemainingSeconds
            };

            foreach (var player in source.Players)
            {
                clone.Players.Add(new PlayerState
                {
                    PlayerId = player.PlayerId,
                    X = player.X,
                    Y = player.Y,
                    Vx = player.Vx,
                    Vy = player.Vy,
                    State = player.State,
                    Alive = player.Alive,
                    RespawnRemainingSeconds = player.RespawnRemainingSeconds,
                    Mass = player.Mass,
                    Radius = player.Radius,
                    MoveSpeed = player.MoveSpeed
                });
            }

            foreach (var pickup in source.Pickups)
            {
                clone.Pickups.Add(new PickupState
                {
                    Type = pickup.Type,
                    X = pickup.X,
                    Y = pickup.Y
                });
            }

            return clone;
        }

        private static MatchmakingStatusUpdate CloneMatchmakingStatus(MatchmakingStatusUpdate source)
        {
            return new MatchmakingStatusUpdate
            {
                State = source.State,
                Message = source.Message,
                RoomId = source.RoomId,
                QueuePosition = source.QueuePosition,
                QueueSize = source.QueueSize,
                RoomCapacity = source.RoomCapacity,
                MatchedPlayerCount = source.MatchedPlayerCount,
                ReliableSequence = source.ReliableSequence,
                RealtimeConnection = source.RealtimeConnection is null
                    ? null
                    : new RealtimeConnectionInfo
                    {
                        Transport = source.RealtimeConnection.Transport,
                        Host = source.RealtimeConnection.Host,
                        Port = source.RealtimeConnection.Port,
                        Path = source.RealtimeConnection.Path,
                        RoomId = source.RealtimeConnection.RoomId,
                        MatchId = source.RealtimeConnection.MatchId,
                        SessionToken = source.RealtimeConnection.SessionToken
                    }
            };
        }
    }

    internal readonly struct DrainedCallbacks
    {
        public DrainedCallbacks(WorldState? worldState, List<PlayerDead> deaths, MatchEnd? matchEnd, MatchmakingStatusUpdate? matchmakingStatus, string? realtimeFallbackMessage, string? disconnectedMessage)
        {
            WorldState = worldState;
            Deaths = deaths;
            MatchEnd = matchEnd;
            MatchmakingStatus = matchmakingStatus;
            RealtimeFallbackMessage = realtimeFallbackMessage;
            DisconnectedMessage = disconnectedMessage;
        }

        public WorldState? WorldState { get; }
        public List<PlayerDead> Deaths { get; }
        public MatchEnd? MatchEnd { get; }
        public MatchmakingStatusUpdate? MatchmakingStatus { get; }
        public string? RealtimeFallbackMessage { get; }
        public string? DisconnectedMessage { get; }
    }
}
