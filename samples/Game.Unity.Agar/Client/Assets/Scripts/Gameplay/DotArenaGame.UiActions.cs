#nullable enable

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        public void OnUiSinglePlayerSelected()
        {
            UiSurface.OnUiSinglePlayerSelected();
        }

        public void OnUiInvincibleSinglePlayerSelected()
        {
            UiSurface.OnUiInvincibleSinglePlayerSelected();
        }

        public void OnUiMultiplayerSelected()
        {
            UiSurface.OnUiMultiplayerSelected();
        }

        public void OnUiBackToModeSelect()
        {
            UiSurface.OnUiBackToModeSelect();
        }

        public void OnUiCancelMatchmakingRequested()
        {
            UiSurface.OnUiCancelMatchmakingRequested();
        }

        public void OnUiConnectRequested()
        {
            UiSurface.OnUiConnectRequested();
        }

        public void OnUiGuestLoginRequested()
        {
            UiSurface.OnUiGuestLoginRequested();
        }

        public void OnUiRematchRequested()
        {
            UiSurface.OnUiRematchRequested();
        }

        public void OnUiReturnToLobbyRequested()
        {
            UiSurface.OnUiReturnToLobbyRequested();
        }

        public void OnUiAccountChanged(string value)
        {
            UiSurface.OnUiAccountChanged(value);
        }

        public void OnUiPasswordChanged(string value)
        {
            UiSurface.OnUiPasswordChanged(value);
        }

        private void OnUiLobbyActionRequested(MetaTab tab, bool isPrimaryAction)
        {
            UiSurface.OnUiLobbyActionRequested(tab, isPrimaryAction);
        }
    }
}
