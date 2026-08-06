#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Interfaces;

namespace Shared.Gameplay
{
    public static class FrameSyncProtocol
    {
        public const int Version = 2;
        public const float FixedDeltaSeconds = 1f / 20f;
        public const int MaxBufferedFrames = 512;
        public const int MaxReplayFrames = 4096;
        public const int RoundSeconds = 120;
        public const int RoundFrameCount = RoundSeconds * 20;

        public static int CreateSeed(string matchId)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var character in matchId ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 16777619u;
                }

                return (int)(hash == 0 ? 1u : hash);
            }
        }
    }

    public sealed class FrameSyncAdvance
    {
        public static readonly FrameSyncAdvance Empty = new(Array.Empty<ArenaStepResult>());

        public FrameSyncAdvance(ArenaStepResult[] steps)
        {
            Steps = steps;
        }

        public ArenaStepResult[] Steps { get; }
    }

    public sealed class FrameSyncSimulation
    {
        private readonly SortedDictionary<int, FrameSyncFrame> _pendingFrames = new();
        private readonly ArenaSimulation _simulation;
        private readonly string _matchId;
        private readonly string _roomId;
        private readonly float _fixedDeltaSeconds;
        private int _nextFrame = 1;

        public FrameSyncSimulation(FrameSyncStart start)
        {
            if (start == null)
            {
                throw new ArgumentNullException(nameof(start));
            }

            if (start.ProtocolVersion != FrameSyncProtocol.Version)
            {
                throw new InvalidOperationException(
                    $"Unsupported frame-sync protocol version {start.ProtocolVersion}; expected {FrameSyncProtocol.Version}.");
            }

            if (string.IsNullOrWhiteSpace(start.MatchId))
            {
                throw new ArgumentException("Match id is required.", nameof(start));
            }

            _matchId = start.MatchId;
            _roomId = start.RoomId;
            _fixedDeltaSeconds = start.FixedDeltaSeconds > 0f
                ? start.FixedDeltaSeconds
                : FrameSyncProtocol.FixedDeltaSeconds;

            var participantCount = Math.Max(1, start.MaxPlayers);
            _simulation = new ArenaSimulation(new ArenaSimulationOptions
            {
                Arena = ArenaConfig.CreateDefault(),
                RespawnDelaySeconds = 5f,
                TargetParticipantCount = participantCount,
                MinPlayersToStart = participantCount,
                EnableBots = true,
                MaxRoundSeconds = FrameSyncProtocol.RoundSeconds,
                RandomSeed = start.RandomSeed
            });

            foreach (var player in start.Players
                .Where(static player => !string.IsNullOrWhiteSpace(player.PlayerId))
                .OrderBy(static player => player.SeatIndex)
                .ThenBy(static player => player.PlayerId, StringComparer.Ordinal))
            {
                _simulation.UpsertPlayer(new ArenaPlayerRegistration
                {
                    PlayerId = player.PlayerId,
                    PreferredSpawnIndex = player.SeatIndex,
                    IsBot = false
                });
            }
        }

        public string MatchId => _matchId;
        public string RoomId => _roomId;
        public int LastAppliedFrame => _nextFrame - 1;
        public WorldState WorldState => _simulation.CreateWorldState();

        public FrameSyncAdvance SubmitFrame(FrameSyncFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (!string.Equals(frame.MatchId, _matchId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Frame belongs to match '{frame.MatchId}', but this simulation owns '{_matchId}'.");
            }

            if (frame.Frame < _nextFrame || _pendingFrames.ContainsKey(frame.Frame))
            {
                return FrameSyncAdvance.Empty;
            }

            if (frame.Frame - _nextFrame >= FrameSyncProtocol.MaxBufferedFrames)
            {
                throw new InvalidOperationException(
                    $"Frame gap is too large: expected {_nextFrame}, received {frame.Frame}.");
            }

            _pendingFrames.Add(frame.Frame, frame);
            if (!_pendingFrames.ContainsKey(_nextFrame))
            {
                return FrameSyncAdvance.Empty;
            }

            var steps = new List<ArenaStepResult>();
            while (_pendingFrames.Remove(_nextFrame, out var next))
            {
                foreach (var input in next.Inputs
                    .Where(static input => !string.IsNullOrWhiteSpace(input.PlayerId))
                    .OrderBy(static input => input.PlayerId, StringComparer.Ordinal))
                {
                    _simulation.SubmitInput(input);
                }

                steps.Add(_simulation.Tick(_fixedDeltaSeconds));
                _nextFrame += 1;
            }

            return new FrameSyncAdvance(steps.ToArray());
        }
    }
}
