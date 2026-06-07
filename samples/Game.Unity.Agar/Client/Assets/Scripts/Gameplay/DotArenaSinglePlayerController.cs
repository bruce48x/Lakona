#nullable enable

using System;
using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaSinglePlayerController
    {
        private const string LocalPlayerId = "Player";

        private ArenaSimulation? _match;
        private float _tickAccumulator;

        public ArenaSimulation? Match => _match;

        public DotArenaSinglePlayerStartResult BeginMatch(SinglePlayerMode mode, ref int playlistIndex)
        {
            var preset = DotArenaSinglePlayerCatalog.GetNextPreset(ref playlistIndex);
            var options = DotArenaSinglePlayerCatalog.CreateOptions(preset);
            var initialMass = options.InitialPlayerMass;

            if (mode == SinglePlayerMode.Invincible)
            {
                initialMass = InvincibleSinglePlayerInitialMass;
                options.FixedRespawnPlayerId = LocalPlayerId;
                options.FixedRespawnMass = InvincibleSinglePlayerInitialMass;
                options.MoveSpeedMultiplierPlayerId = LocalPlayerId;
                options.MoveSpeedMultiplier = 2f;
                options.InvertedMoveSpeedPlayerId = LocalPlayerId;
            }

            _match = new ArenaSimulation(options);
            _match.UpsertPlayer(new ArenaPlayerRegistration
            {
                PlayerId = LocalPlayerId,
                Mass = initialMass
            });

            _tickAccumulator = 0f;

            return new DotArenaSinglePlayerStartResult(
                mode,
                LocalPlayerId,
                preset.MapVariant,
                preset.RuleVariant,
                _match,
                _match.CreateWorldState());
        }

        public void SubmitInput(Vector2 move, int tick)
        {
            if (_match == null)
            {
                return;
            }

            _match.SubmitInput(new InputMessage
            {
                PlayerId = LocalPlayerId,
                MoveX = move.x,
                MoveY = move.y,
                Tick = tick
            });
        }

        public DotArenaSinglePlayerTickResult Tick(float deltaTime)
        {
            if (_match == null)
            {
                return DotArenaSinglePlayerTickResult.Empty;
            }

            _tickAccumulator += Mathf.Min(deltaTime, SinglePlayerTickSeconds * MaxSinglePlayerCatchUpTicks);

            var steps = new ArenaStepResult[MaxSinglePlayerCatchUpTicks];
            var catchUpTicks = 0;
            var reachedMatchEnd = false;

            while (_tickAccumulator >= SinglePlayerTickSeconds && catchUpTicks < MaxSinglePlayerCatchUpTicks)
            {
                _tickAccumulator -= SinglePlayerTickSeconds;

                var step = _match.Tick(SinglePlayerTickSeconds);
                steps[catchUpTicks] = step;
                catchUpTicks++;

                if (step.MatchEnd != null)
                {
                    _tickAccumulator = 0f;
                    reachedMatchEnd = true;
                    break;
                }
            }

            if (catchUpTicks == MaxSinglePlayerCatchUpTicks && _tickAccumulator > SinglePlayerTickSeconds)
            {
                _tickAccumulator = 0f;
            }

            if (catchUpTicks == 0)
            {
                return DotArenaSinglePlayerTickResult.Empty;
            }

            if (catchUpTicks != steps.Length)
            {
                Array.Resize(ref steps, catchUpTicks);
            }

            return new DotArenaSinglePlayerTickResult(steps, reachedMatchEnd);
        }
    }

    internal sealed class DotArenaSinglePlayerStartResult
    {
        public DotArenaSinglePlayerStartResult(
            SinglePlayerMode mode,
            string localPlayerId,
            ArenaMapVariant mapVariant,
            ArenaRuleVariant ruleVariant,
            ArenaSimulation match,
            WorldState initialWorldState)
        {
            Mode = mode;
            LocalPlayerId = localPlayerId;
            MapVariant = mapVariant;
            RuleVariant = ruleVariant;
            Match = match;
            InitialWorldState = initialWorldState;
        }

        public SinglePlayerMode Mode { get; }
        public string LocalPlayerId { get; }
        public ArenaMapVariant MapVariant { get; }
        public ArenaRuleVariant RuleVariant { get; }
        public ArenaSimulation Match { get; }
        public WorldState InitialWorldState { get; }
    }

    internal sealed class DotArenaSinglePlayerTickResult
    {
        public static readonly DotArenaSinglePlayerTickResult Empty = new(Array.Empty<ArenaStepResult>(), false);

        public DotArenaSinglePlayerTickResult(ArenaStepResult[] steps, bool reachedMatchEnd)
        {
            Steps = steps;
            ReachedMatchEnd = reachedMatchEnd;
        }

        public ArenaStepResult[] Steps { get; }
        public bool ReachedMatchEnd { get; }
    }
}
