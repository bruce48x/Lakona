#nullable enable

using System;
using System.Collections.Generic;

namespace SampleClient.Gameplay
{
    internal static class DotArenaUiTextComposer
    {
        public static string BuildMenuLoginStatusText(bool hasAuthenticatedProfile, string authenticatedPlayerId, int localWinCount)
        {
            if (!hasAuthenticatedProfile || string.IsNullOrWhiteSpace(authenticatedPlayerId))
            {
                return "未登录";
            }

            return $"已登录：{authenticatedPlayerId}   胜场：{localWinCount}";
        }

        public static string BuildSettlementDetail(SessionMode sessionMode, float localMass, int localWinCount, string winnerPlayerId, bool localPlayerWon, ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant)
        {
            var modeText = sessionMode == SessionMode.SinglePlayer ? "单机" : "联机";
            var resultText = localPlayerWon ? "胜利" : "失败";
            var presetLabel = DotArenaSinglePlayerCatalog.GetPresetLabel(mapVariant, ruleVariant);
            var presetLine = sessionMode == SessionMode.SinglePlayer ? $"\n预设：{presetLabel}" : string.Empty;
            return $"模式：{modeText}{presetLine}\n结果：{resultText}\n胜者：{winnerPlayerId}\n质量：{DotArenaPresentation.FormatMass(localMass)}\n胜场：{localWinCount}";
        }

        public static string BuildSettlementRewardSummary(SessionMode sessionMode, DotArenaRewardSummary? lastRewardSummary)
        {
            if (lastRewardSummary == null)
            {
                return sessionMode == SessionMode.Multiplayer
                    ? "奖励：正在同步。"
                    : "奖励：本局暂无。";
            }

            return $"奖励：经验 +{lastRewardSummary.ExperienceGained}，金币 +{lastRewardSummary.CurrencyGained}，等级 {lastRewardSummary.NewLevel}";
        }

        public static string BuildSettlementTaskSummary(DotArenaMetaState? metaState)
        {
            return string.Empty;
        }

        public static string BuildSettlementNextStepSummary(SessionMode sessionMode, ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant)
        {
            return sessionMode == SessionMode.Multiplayer
                ? "下一步：返回大厅后再次开始匹配。"
                : $"下一步：返回模式选择，或重开 {DotArenaSinglePlayerCatalog.GetPresetLabel(mapVariant, ruleVariant)}。";
        }

        public static string BuildDebugPanelDetail(string status, FrontendFlowState flowState, EntryMenuState entryMenuState, SessionMode sessionMode, string localPlayerId, int lastWorldTick, int viewCount, string localPlayerBuffText, string currentEventMessage, string endpoint, bool isConnected, bool isRealtimeConnected, bool isConnecting)
        {
            return string.Empty;
        }

        public static string BuildMatchmakingDetail(SessionMode sessionMode, ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant, string status, string currentEventMessage, int elapsedSeconds, bool cancelRequestPending)
        {
            if (sessionMode == SessionMode.SinglePlayer)
            {
                return $"预设：{DotArenaSinglePlayerCatalog.GetPresetLabel(mapVariant, ruleVariant)}\n正在创建本地对局。";
            }

            var elapsedText = $"已等待 {FormatElapsedSeconds(elapsedSeconds)}";
            if (cancelRequestPending)
            {
                return $"正在取消匹配\n{elapsedText}\n请稍候，正在返回大厅。";
            }

            if (status.Contains("成功", StringComparison.Ordinal) ||
                currentEventMessage.Contains("进入对局", StringComparison.Ordinal))
            {
                return $"匹配成功\n{elapsedText}\n正在进入对局。";
            }

            return $"正在寻找对局\n{elapsedText}\n可随时取消匹配。";
        }

        private static string FormatElapsedSeconds(int elapsedSeconds)
        {
            elapsedSeconds = Math.Max(0, elapsedSeconds);
            var minutes = elapsedSeconds / 60;
            var seconds = elapsedSeconds % 60;
            return minutes > 0 ? $"{minutes}分{seconds:D2}秒" : $"{seconds}秒";
        }

        public static string BuildMetaPlayerSummary(DotArenaMetaState? metaState, bool isInMultiplayerLobby)
        {
            if (metaState == null)
            {
                return "游客资料";
            }

            return isInMultiplayerLobby
                ? $"{metaState.PlayerId}   胜场 {metaState.TotalWins}   金币 {metaState.SoftCurrency}   已就绪"
                : $"{metaState.PlayerId}   等级 {metaState.Level}   经验 {metaState.Experience}/{GetMetaNextLevelRequirement(metaState.Level)}   金币 {metaState.SoftCurrency}";
        }

        public static string BuildMetaLobbyHighlights(DotArenaMetaState? metaState, bool isInMultiplayerLobby, SinglePlayerMatchPreset previewPreset)
        {
            if (metaState == null)
            {
                return string.Empty;
            }

            return isInMultiplayerLobby
                ? "可以开始匹配"
                : $"下一预设：{DotArenaSinglePlayerCatalog.GetPresetLabel(previewPreset.MapVariant, previewPreset.RuleVariant)}";
        }

        public static string BuildMetaProfileDetail(DotArenaMetaState? metaState, bool isInMultiplayerLobby, SinglePlayerMatchPreset previewPreset, DotArenaRewardSummary? lastRewardSummary, string endpoint)
        {
            if (metaState == null)
            {
                return "尚未加载资料。";
            }

            var modeLine = isInMultiplayerLobby
                ? $"联机大厅：{metaState.PlayerId} 已就绪\n操作：点击“开始匹配”进入队列"
                : $"下一本地预设：{DotArenaSinglePlayerCatalog.GetPresetLabel(previewPreset.MapVariant, previewPreset.RuleVariant)}";

            var lastReward = lastRewardSummary == null
                ? "最近奖励：暂无。"
                : $"最近奖励：经验 +{lastRewardSummary.ExperienceGained}，金币 +{lastRewardSummary.CurrencyGained}，等级 {lastRewardSummary.NewLevel}";
            return $"胜场：{metaState.TotalWins}\n对局：{metaState.TotalMatches}\n连续登录：{metaState.CurrentLoginStreak}\n当前皮肤：{metaState.EquippedCosmeticId}\n{modeLine}\n{lastReward}";
        }

        public static string BuildMetaTasksDetail(DotArenaMetaState? metaState)
        {
            return string.Empty;
        }

        public static string BuildMetaShopDetail(DotArenaMetaState? metaState)
        {
            return string.Empty;
        }

        public static string BuildMetaRecordsDetail(DotArenaMetaState? metaState)
        {
            return string.Empty;
        }

        public static string BuildMetaLeaderboardDetail(DotArenaMetaState? metaState)
        {
            if (metaState == null)
            {
                return "暂无排行榜数据。";
            }

            var summary = DotArenaMetaProgression.GetLeaderboardSummary(metaState);
            var resetText = metaState.LeaderboardSecondsUntilReset > 0
                ? $"本周剩余：{FormatDurationZh(metaState.LeaderboardSecondsUntilReset)}"
                : "本周剩余：等待服务器刷新";
            var lines = new List<string>
            {
                "排行榜",
                $"玩家：{metaState.PlayerId} | 胜场：{metaState.TotalWins} | 对局：{metaState.TotalMatches}",
                resetText,
                string.Empty,
                "本周排名"
            };

            foreach (var entry in summary.Entries)
            {
                var marker = entry.IsLocalPlayer ? "（你）" : string.Empty;
                lines.Add($"{entry.Position}. {entry.Name} - 胜利积分 {entry.VictoryPoints} / 胜场 {entry.Wins}{marker}");
            }

            return string.Join("\n", lines);
        }

        public static string BuildMetaSettingsDetail(DotArenaMetaState? metaState)
        {
            if (metaState == null)
            {
                return "尚未加载设置。";
            }

            return $"主音量：{metaState.Settings.MasterVolume:0.0}\n音乐音量：{metaState.Settings.MusicVolume:0.0}\n音效音量：{metaState.Settings.SfxVolume:0.0}\n全屏：{FormatBoolZh(metaState.Settings.Fullscreen)}";
        }

        public static string BuildMetaFooterHint(bool isInMultiplayerLobby)
        {
            return isInMultiplayerLobby
                ? "底部左侧按钮“开始匹配”进入队列，右侧按钮“退出登录”返回模式选择。"
                : "顶部页签可切换资料、排行榜和设置。";
        }

        public static string GetRematchButtonLabel(SessionMode sessionMode)
        {
            return sessionMode == SessionMode.SinglePlayer ? "再来一局" : "再次匹配";
        }

        private static string FormatDurationZh(int seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (span.TotalDays >= 1d)
            {
                return $"{(int)span.TotalDays}天{span.Hours}小时";
            }

            if (span.TotalHours >= 1d)
            {
                return $"{(int)span.TotalHours}小时{span.Minutes}分";
            }

            return $"{span.Minutes}分";
        }

        private static string FormatBoolZh(bool value)
        {
            return value ? "开" : "关";
        }

        public static int GetMetaNextLevelRequirement(int level)
        {
            return 100 + ((Math.Max(1, level) - 1) * 25);
        }
    }
}
