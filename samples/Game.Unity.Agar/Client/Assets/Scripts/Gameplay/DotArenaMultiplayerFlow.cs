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
                IsServerUnavailableError(ex) ? "Server temporarily unavailable" : "Login failed",
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
                    "Returned to multiplayer lobby",
                    clearPendingCancelRequest: true),
                MatchmakingState.Failed => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Entry,
                    EntryMenuState.MultiplayerLobby,
                    statusText,
                    "Start matchmaking again",
                    clearPendingCancelRequest: true),
                MatchmakingState.Matched => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Matchmaking,
                    EntryMenuState.Hidden,
                    statusText,
                    "Match found, entering game",
                    clearPendingCancelRequest: true),
                MatchmakingState.Queued or MatchmakingState.Searching when cancelRequestPending => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Matchmaking,
                    EntryMenuState.Hidden,
                    "Canceling matchmaking",
                    "Waiting for server cancellation confirmation",
                    clearPendingCancelRequest: false),
                _ => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Matchmaking,
                    EntryMenuState.Hidden,
                    statusText,
                    "Finding a suitable match",
                    clearPendingCancelRequest: false)
            };
        }

        private static string BuildFriendlyConnectionMessage(Exception ex)
        {
            if (IsServerUnavailableError(ex))
            {
                return "The server is starting or room service is temporarily unavailable. Try again later.";
            }

            if (IsNetworkConnectError(ex))
            {
                return "Could not connect to the server. Check the network or confirm the server is running.";
            }

            return "An error occurred during login. Try again later.";
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
                   message.Contains("connect", StringComparison.OrdinalIgnoreCase) && message.Contains("failed", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildMatchmakingStatusText(MatchmakingStatusUpdate matchmakingStatus)
        {
            return matchmakingStatus.State switch
            {
                MatchmakingState.Queued => "Queued",
                MatchmakingState.Searching => "Searching",
                MatchmakingState.Matched => "Matched",
                MatchmakingState.Canceled => "Matchmaking canceled",
                MatchmakingState.Failed => "Matchmaking failed",
                _ => "Waiting for matchmaking"
            };
        }
    }
}
