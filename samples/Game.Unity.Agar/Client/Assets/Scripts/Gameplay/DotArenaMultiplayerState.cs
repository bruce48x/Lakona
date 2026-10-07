#nullable enable

using Shared.Interfaces;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaMultiplayerState
    {
        public SessionMode SessionMode { get; set; } = SessionMode.None;
        public string LocalPlayerId { get; set; } = string.Empty;
        public bool HasAuthenticatedProfile { get; set; }
        public string AuthenticatedPlayerId { get; set; } = string.Empty;
        public int LocalWinCount { get; set; }
        public int LocalVictoryPoints { get; set; }
        public PendingUiRequest PendingUiRequest { get; set; }
        public float MatchmakingStartedAt { get; set; } = -1f;
        public RealtimeConnectionInfo? LastRealtimeConnection { get; set; }

        public bool HasPendingUiRequest => PendingUiRequest != PendingUiRequest.None;
        public bool HasAuthenticatedMultiplayerProfile => SessionMode == SessionMode.Multiplayer && HasAuthenticatedProfile;

        public DotArenaAuthenticatedProfile CaptureAuthenticatedProfile()
        {
            return new DotArenaAuthenticatedProfile(HasAuthenticatedProfile, AuthenticatedPlayerId, LocalWinCount, LocalVictoryPoints);
        }

        public void ClearAuthenticatedProfile()
        {
            HasAuthenticatedProfile = false;
            AuthenticatedPlayerId = string.Empty;
            LocalWinCount = 0;
            LocalVictoryPoints = 0;
        }

        public void ApplyAuthenticatedProfile(string playerId, int winCount, int victoryPoints)
        {
            HasAuthenticatedProfile = true;
            AuthenticatedPlayerId = playerId;
            LocalWinCount = winCount < 0 ? 0 : winCount;
            LocalVictoryPoints = victoryPoints < 0 ? 0 : victoryPoints;
        }

        public void RestoreAuthenticatedProfile(DotArenaAuthenticatedProfile profile)
        {
            HasAuthenticatedProfile = profile.HasAuthenticatedProfile;
            AuthenticatedPlayerId = profile.PlayerId;
            LocalWinCount = profile.WinCount;
            LocalVictoryPoints = profile.VictoryPoints;
        }

        public void ApplyMultiplayerLogin(string playerId, int winCount, int victoryPoints)
        {
            LocalPlayerId = playerId;
            SessionMode = SessionMode.Multiplayer;
            ApplyAuthenticatedProfile(playerId, winCount, victoryPoints);
        }

        public void ClearSession()
        {
            SessionMode = SessionMode.None;
            LocalPlayerId = string.Empty;
        }

        public void ClearAll()
        {
            ClearSession();
            ClearAuthenticatedProfile();
            ClearRequestState();
        }

        public void ClearRequestState()
        {
            PendingUiRequest = PendingUiRequest.None;
            LastRealtimeConnection = null;
            MatchmakingStartedAt = -1f;
        }
    }

    internal readonly struct DotArenaAuthenticatedProfile
    {
        public DotArenaAuthenticatedProfile(bool hasAuthenticatedProfile, string playerId, int winCount, int victoryPoints)
        {
            HasAuthenticatedProfile = hasAuthenticatedProfile;
            PlayerId = playerId;
            WinCount = winCount;
            VictoryPoints = victoryPoints;
        }

        public bool HasAuthenticatedProfile { get; }
        public string PlayerId { get; }
        public int WinCount { get; }
        public int VictoryPoints { get; }
    }
}
