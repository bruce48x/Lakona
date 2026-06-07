#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SampleClient.Gameplay
{
    internal static partial class DotArenaMetaProgression
    {
        private static string LegacyStatePath => Path.Combine(Application.persistentDataPath, "dotarena_meta.json");

        public static DotArenaMetaState LoadOrCreate(string playerId)
        {
            var resolvedPlayerId = ResolvePlayerId(playerId);
            var state = TryLoadState(GetStatePath(resolvedPlayerId)) ?? TryLoadLegacyState(resolvedPlayerId) ?? new DotArenaMetaState();
            NormalizeState(state, resolvedPlayerId);
            HandleLogin(state);
            Save(state);
            return state;
        }

        public static void Save(DotArenaMetaState state)
        {
            try
            {
                if (state == null)
                {
                    return;
                }

                NormalizeState(state, state.PlayerId);
                var path = GetStatePath(state.PlayerId);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }

        private static void HandleLogin(DotArenaMetaState state)
        {
            var today = DateTime.UtcNow.Date;
            var todayIso = today.ToString("yyyy-MM-dd");
            if (string.Equals(state.LastLoginDateIso, todayIso, StringComparison.Ordinal))
            {
                return;
            }

            if (DateTime.TryParse(state.LastLoginDateIso, out var lastLogin) && lastLogin.Date == today.AddDays(-1))
            {
                state.CurrentLoginStreak += 1;
            }
            else
            {
                state.CurrentLoginStreak = 1;
            }

            state.LastLoginDateIso = todayIso;
            state.SoftCurrency += 10 * state.CurrentLoginStreak;
        }

        private static DotArenaMetaState? TryLoadState(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                return JsonUtility.FromJson<DotArenaMetaState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        private static DotArenaMetaState? TryLoadLegacyState(string playerId)
        {
            var legacy = TryLoadState(LegacyStatePath);
            if (legacy == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(legacy.PlayerId) && !string.Equals(ResolvePlayerId(legacy.PlayerId), playerId, StringComparison.Ordinal))
            {
                return null;
            }

            return legacy;
        }

        private static void NormalizeState(DotArenaMetaState state, string playerId)
        {
            state.PlayerId = ResolvePlayerId(string.IsNullOrWhiteSpace(playerId) ? state.PlayerId : playerId);
            state.Settings ??= new DotArenaSettings();
            state.Settings.Language = NormalizeLanguage(state.Settings.Language);
            state.Settings.MasterVolume = Mathf.Clamp01(state.Settings.MasterVolume);
            state.Settings.MusicVolume = Mathf.Clamp01(state.Settings.MusicVolume);
            state.Settings.SfxVolume = Mathf.Clamp01(state.Settings.SfxVolume);
            state.OwnedCosmeticIds ??= new List<string>();
            state.MatchHistory ??= new List<DotArenaMatchRecord>();
            if (!state.OwnedCosmeticIds.Contains("skin_default"))
            {
                state.OwnedCosmeticIds.Insert(0, "skin_default");
            }

            if (string.IsNullOrWhiteSpace(state.EquippedCosmeticId) || !state.OwnedCosmeticIds.Contains(state.EquippedCosmeticId))
            {
                state.EquippedCosmeticId = "skin_default";
            }
        }

        private static string ResolvePlayerId(string playerId)
        {
            return string.IsNullOrWhiteSpace(playerId) ? "Guest" : playerId.Trim();
        }

        private static string NormalizeLanguage(string language)
        {
            return string.IsNullOrWhiteSpace(language) ? "zh-CN" : language.Trim();
        }

        private static string GetStatePath(string playerId)
        {
            return Path.Combine(Application.persistentDataPath, $"dotarena_meta_{SanitizePathSegment(playerId)}.json");
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Guest";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var buffer = value.Trim().ToCharArray();
            for (var i = 0; i < buffer.Length; i++)
            {
                var ch = buffer[i];
                if (Array.IndexOf(invalidChars, ch) >= 0 || char.IsControl(ch) || char.IsWhiteSpace(ch))
                {
                    buffer[i] = '_';
                }
            }

            var sanitized = new string(buffer).Trim('_');
            return string.IsNullOrWhiteSpace(sanitized) ? "Guest" : sanitized;
        }

    }
}
