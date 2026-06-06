#nullable enable

using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal static class DotArenaSinglePlayerCatalog
    {
        private static readonly SinglePlayerMatchPreset[] Playlist =
        {
            new(ArenaMapVariant.ClassicSquare, ArenaRuleVariant.ClassicElimination),
            new(ArenaMapVariant.NarrowBridge, ArenaRuleVariant.MassRush),
            new(ArenaMapVariant.FinalRing, ArenaRuleVariant.ArenaCollapse)
        };

        public static SinglePlayerMatchPreset GetNextPreset(ref int playlistIndex)
        {
            if (playlistIndex < 0)
            {
                playlistIndex = 0;
            }

            var preset = Playlist[playlistIndex];
            playlistIndex = (playlistIndex + 1) % Playlist.Length;
            return preset;
        }

        public static SinglePlayerMatchPreset PeekPreset(int playlistIndex)
        {
            return Playlist[playlistIndex < 0 ? 0 : playlistIndex % Playlist.Length];
        }

        public static SinglePlayerMatchPreset AdvancePresetSelection(ref int playlistIndex)
        {
            playlistIndex = playlistIndex < 0 ? 0 : (playlistIndex + 1) % Playlist.Length;
            return Playlist[playlistIndex];
        }

        public static string GetPresetLabel(ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant)
        {
            return $"{GetMapVariantName(mapVariant)} / {GetRuleVariantName(ruleVariant)}";
        }

        public static string GetMapVariantName(ArenaMapVariant mapVariant)
        {
            return mapVariant switch
            {
                ArenaMapVariant.NarrowBridge => "Narrow Bridge",
                ArenaMapVariant.FinalRing => "Final Ring",
                _ => "Classic Square"
            };
        }

        public static string GetRuleVariantName(ArenaRuleVariant ruleVariant)
        {
            return ruleVariant switch
            {
                ArenaRuleVariant.MassRush => "Mass Rush",
                ArenaRuleVariant.ArenaCollapse => "Arena Collapse",
                _ => "Classic Elimination"
            };
        }

        public static ArenaSimulationOptions CreateOptions(SinglePlayerMatchPreset preset)
        {
            var options = new ArenaSimulationOptions
            {
                Arena = CreateArenaConfig(preset.MapVariant),
                MaxRoundSeconds = 0f,
                RespawnDelaySeconds = 5f,
                FoodTargetCount = 96,
                InitialMass = 24f,
                InitialPlayerMass = 24f,
                RespawnMass = 24f,
                EnabledPickupTypes = new[] { PickupType.MassPoint }
            };

            switch (preset.RuleVariant)
            {
                case ArenaRuleVariant.MassRush:
                    options.RespawnDelaySeconds = 3f;
                    options.FoodTargetCount = 132;
                    options.FoodMassGain = 1.45f;
                    options.BaseMoveSpeed = 9.4f;
                    options.ShrinkStartDelaySeconds = 26f;
                    options.ShrinkDurationSeconds = 28f;
                    break;
                case ArenaRuleVariant.ArenaCollapse:
                    options.RespawnDelaySeconds = 8f;
                    options.FoodTargetCount = 88;
                    options.BaseMoveSpeed = 8.4f;
                    options.EatMassRatio = 1.12f;
                    options.ShrinkStartDelaySeconds = 8f;
                    options.ShrinkDurationSeconds = 20f;
                    options.FinalArenaHalfExtents = new Vector2(8f, 8f);
                    break;
            }

            return options;
        }

        private static ArenaConfig CreateArenaConfig(ArenaMapVariant mapVariant)
        {
            return mapVariant switch
            {
                ArenaMapVariant.NarrowBridge => new ArenaConfig
                {
                    ArenaHalfExtents = new Vector2(52f, 18f),
                    RespawnInset = 4f,
                    PlayerCollisionRadius = GameplayConfig.PlayerCollisionRadius,
                    PlayerVisualRadius = GameplayConfig.PlayerVisualRadius,
                    PickupCollisionRadius = GameplayConfig.PickupCollisionRadius,
                    PickupSpawnInset = 3.5f
                },
                ArenaMapVariant.FinalRing => new ArenaConfig
                {
                    ArenaHalfExtents = new Vector2(34f, 34f),
                    RespawnInset = 4f,
                    PlayerCollisionRadius = GameplayConfig.PlayerCollisionRadius,
                    PlayerVisualRadius = GameplayConfig.PlayerVisualRadius,
                    PickupCollisionRadius = GameplayConfig.PickupCollisionRadius,
                    PickupSpawnInset = 3f
                },
                _ => new ArenaConfig
                {
                    ArenaHalfExtents = GameplayConfig.ArenaHalfExtents,
                    RespawnInset = GameplayConfig.RespawnInset,
                    PlayerCollisionRadius = GameplayConfig.PlayerCollisionRadius,
                    PlayerVisualRadius = GameplayConfig.PlayerVisualRadius,
                    PickupCollisionRadius = GameplayConfig.PickupCollisionRadius,
                    PickupSpawnInset = GameplayConfig.PickupSpawnInset
                }
            };
        }
    }
}
