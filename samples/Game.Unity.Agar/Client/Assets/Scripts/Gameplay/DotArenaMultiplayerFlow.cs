#nullable enable

using System;
using Shared.Interfaces;

namespace SampleClient.Gameplay
{
    internal readonly struct DotArenaConnectionFeedback
    {
        public DotArenaConnectionFeedback(string status, string message)
        {
            Status = status;
            Message = message;
        }

        public string Status { get; }
        public string Message { get; }
    }

    internal readonly struct DotArenaMatchmakingViewState
    {
        public DotArenaMatchmakingViewState(
            FrontendFlowState flowState,
            EntryMenuState entryMenuState,
            string status,
            string eventMessage,
            bool clearPendingCancelRequest)
        {
            FlowState = flowState;
            EntryMenuState = entryMenuState;
            Status = status;
            EventMessage = eventMessage;
            ClearPendingCancelRequest = clearPendingCancelRequest;
        }

        public FrontendFlowState FlowState { get; }
        public EntryMenuState EntryMenuState { get; }
        public string Status { get; }
        public string EventMessage { get; }
        public bool ClearPendingCancelRequest { get; }
    }

    internal static class DotArenaMultiplayerFlow
    {
        public static DotArenaConnectionFeedback BuildConnectionFailure(Exception ex)
        {
            return new DotArenaConnectionFeedback(
                IsServerUnavailableError(ex) ? "服务器暂时不可用" : "登录失败",
                BuildFriendlyConnectionMessage(ex));
        }

        public static DotArenaMatchmakingViewState BuildMatchmakingViewState(MatchmakingStatusUpdate matchmakingStatus, bool cancelRequestPending)
        {
            var statusText = BuildMatchmakingStatusText(matchmakingStatus);

            return matchmakingStatus.State switch
            {
                MatchmakingState.Canceled => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Entry,
                    EntryMenuState.MultiplayerLobby,
                    statusText,
                    "已返回联机大厅",
                    clearPendingCancelRequest: true),
                MatchmakingState.Failed => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Entry,
                    EntryMenuState.MultiplayerLobby,
                    statusText,
                    "请重新开始匹配",
                    clearPendingCancelRequest: true),
                MatchmakingState.Matched => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Matchmaking,
                    EntryMenuState.Hidden,
                    statusText,
                    "匹配成功，正在进入对局",
                    clearPendingCancelRequest: true),
                MatchmakingState.Queued or MatchmakingState.Searching when cancelRequestPending => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Matchmaking,
                    EntryMenuState.Hidden,
                    "正在取消匹配",
                    "等待服务器确认取消",
                    clearPendingCancelRequest: false),
                _ => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Matchmaking,
                    EntryMenuState.Hidden,
                    statusText,
                    "正在为你寻找合适的对局",
                    clearPendingCancelRequest: false)
            };
        }

        private static string BuildFriendlyConnectionMessage(Exception ex)
        {
            if (IsServerUnavailableError(ex))
            {
                return "服务器正在启动或房间服务暂时异常，请稍后重试。";
            }

            if (IsNetworkConnectError(ex))
            {
                return "无法连接到服务器，请检查网络或确认服务端已启动。";
            }

            return "登录过程中发生错误，请稍后重试。";
        }

        private static bool IsServerUnavailableError(Exception ex)
        {
            return ex is InvalidOperationException invalidOperationException &&
                   invalidOperationException.Message.Contains("RPC failed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNetworkConnectError(Exception ex)
        {
            if (ex is TimeoutException)
            {
                return true;
            }

            var message = ex.Message;
            return message.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("连接", StringComparison.OrdinalIgnoreCase) && message.Contains("失败", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildMatchmakingStatusText(MatchmakingStatusUpdate matchmakingStatus)
        {
            return matchmakingStatus.State switch
            {
                MatchmakingState.Queued => "排队中",
                MatchmakingState.Searching => "匹配中",
                MatchmakingState.Matched => "匹配成功",
                MatchmakingState.Canceled => "已取消匹配",
                MatchmakingState.Failed => "匹配失败",
                _ => "等待匹配"
            };
        }
    }
}
