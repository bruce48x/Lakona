#nullable enable

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed partial class DotArenaSceneUiPresenter
    {
        private GameObject? FindSceneUiObject(string path)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            return target != null ? target.gameObject : null;
        }

        private void ApplySceneUiFonts()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _tmpFontAsset ??= LoadTmpFontAsset();
            if (_tmpFontAsset == null)
            {
                return;
            }

            foreach (var text in _sceneUiRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font == null)
                {
                    text.font = _tmpFontAsset;
                }
            }
        }

        private void ApplySceneUiTheme()
        {
            StylePanelImage(_hudPanel, Color.clear);
            StyleMatchRankingPanelImage();
            StylePanelImage(_debugPanel, UiPanelBackgroundColor);
            StylePanelImage(_entryPanel, UiPanelBackgroundColor);
            StylePanelImage(_matchmakingPanel, UiPanelBackgroundColor);
            StylePanelImage(_lobbyPanel, UiPanelBackgroundColor);
            StylePanelImage(_settlementPanel, UiPanelBackgroundColor);

            StyleText(_hudTitleText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_entryTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);

            StyleText(_hudStatusText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudPlayerText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudTickText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudModeText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudHintText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudEventText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudCountdownText, UiAccentTextColor, 18f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchRankingTitleText, UiAccentTextColor, 18f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchRankingHeaderText, UiMutedTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            foreach (var row in _matchRankingRows)
            {
                StyleText(row.RankText, UiSecondaryTextColor, 12f, false, TextAlignmentOptions.Left, TextOverflowModes.Ellipsis);
                StyleText(row.NameText, UiPrimaryTextColor, 12f, false, TextAlignmentOptions.Left, TextOverflowModes.Ellipsis);
                StyleText(row.MassText, UiSecondaryTextColor, 12f, false, TextAlignmentOptions.Right, TextOverflowModes.Ellipsis);
            }

            StyleText(_debugTitleText, UiAccentTextColor, 16f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_debugDetailText, UiSecondaryTextColor, 12f, true, TextAlignmentOptions.TopLeft, TextOverflowModes.Overflow);

            StyleText(_entryStatusText, UiPrimaryTextColor, 14f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchmakingTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchmakingDetailText, UiSecondaryTextColor, 13f, true, TextAlignmentOptions.Top, TextOverflowModes.Overflow);
            StyleText(_lobbyTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbySummaryText, UiSecondaryTextColor, 14f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyHighlightsText, UiAccentTextColor, 14f, true, TextAlignmentOptions.Center, TextOverflowModes.Overflow);
            StyleText(_lobbyQuickActionsText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_lobbyQuickActionButton1Text, UiPrimaryTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyQuickActionButton2Text, UiPrimaryTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyQuickActionButton3Text, UiPrimaryTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyQuickActionButton4Text, UiPrimaryTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyDetailText, UiSecondaryTextColor, 14f, true, TextAlignmentOptions.TopLeft, TextOverflowModes.Overflow);
            StyleText(_lobbyFooterText, UiMutedTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_multiplayerSubtitleText, UiPrimaryTextColor, 15f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_accountLabelText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_passwordLabelText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_accountPlaceholderText, UiMutedTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_passwordPlaceholderText, UiMutedTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);

            StyleButton(_singlePlayerButton);
            StyleButton(_invincibleSinglePlayerButton);
            StyleButton(_multiplayerButton);
            StyleButton(_matchButton);
            StyleButton(_guestLoginButton);
            StyleButton(_backButton);
            StyleButton(_matchmakingCancelButton);
            StyleButton(_lobbyPrimaryActionButton);
            StyleButton(_lobbySecondaryActionButton);
            StyleButton(_lobbyProfileButton);
            StyleButton(_lobbyTasksButton);
            StyleButton(_lobbyShopButton);
            StyleButton(_lobbyRecordsButton);
            StyleButton(_lobbyLeaderboardButton);
            StyleButton(_lobbySettingsButton);
            StyleButton(_lobbyQuickActionButton1);
            StyleButton(_lobbyQuickActionButton2);
            StyleButton(_lobbyQuickActionButton3);
            StyleButton(_lobbyQuickActionButton4);
            StyleButton(_settlementPrimaryButton);
            StyleButton(_settlementSecondaryButton);
            StyleText(_singlePlayerButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_invincibleSinglePlayerButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_multiplayerButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_guestLoginButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_backButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchmakingCancelButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyPrimaryActionButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbySecondaryActionButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_settlementTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_settlementDetailText, UiSecondaryTextColor, 13f, true, TextAlignmentOptions.Top, TextOverflowModes.Overflow);
            StyleText(_settlementPrimaryButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_settlementSecondaryButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);

            StyleInputField(_accountInputField);
            StyleInputField(_passwordInputField);
            ApplyButtonIcon(_lobbyLeaderboardButton, _leaderboardIconSprite);
        }

        private void StylePanelImage(GameObject? panel, Color color)
        {
            DotArenaUiStyleCatalog.ApplyPanelImage(panel, color, _uiPanelSprite);
        }

        private static void StyleText(TMP_Text? text, Color color, float fontSize, bool wrap, TextAlignmentOptions alignment, TextOverflowModes overflowMode)
        {
            DotArenaUiStyleCatalog.ApplyText(text, color, fontSize, wrap, alignment, overflowMode);
        }

        private void StyleButton(Button? button)
        {
            DotArenaUiStyleCatalog.ApplyButton(button, _uiButtonNormalSprite, _uiButtonPressedSprite);
        }

        private static void StyleInputField(TMP_InputField? inputField)
        {
            DotArenaUiStyleCatalog.ApplyInputField(inputField);
        }

        private static void EnsureInputFieldViewport(TMP_InputField? inputField)
        {
            if (inputField?.textViewport == null)
            {
                return;
            }

            var rect = inputField.textViewport;
            if (rect.rect.height >= 18f)
            {
                return;
            }

            rect.offsetMin = new Vector2(10f, 4f);
            rect.offsetMax = new Vector2(-10f, -4f);
        }

        private static TMP_FontAsset? LoadTmpFontAsset()
        {
            var projectFont = Resources.Load<TMP_FontAsset>(TmpFallbackFontAssetResourcePath);
            if (projectFont != null)
            {
                return projectFont;
            }

            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            var fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            return fallback ?? TMP_Settings.defaultFontAsset;
        }

        private void LoadSceneUiArtSprites()
        {
            _uiPanelSprite = null;
            _uiButtonNormalSprite = null;
            _uiButtonPressedSprite = null;
            _uiBackgroundSprite = null;
            _matchRankingPanelSprite = null;
            _leaderboardIconSprite = null;

#if UNITY_EDITOR
            _uiBackgroundSprite = TryLoadSceneUiSprite("UI background", "Assets/Art/Backgrounds/BG_Arena_Grid_Dark_01.png");
            _uiPanelSprite = TryLoadSceneUiSprite("UI panel", "Assets/Art/UI/UI_Panel_Dark_01.png");
            _matchRankingPanelSprite = TryLoadSceneUiSprite("match ranking panel", "Assets/Art/UI/UI_HUD_Rank_Panel_Translucent_01.png");
            _uiButtonNormalSprite = TryLoadSceneUiSprite("UI button normal", "Assets/Art/UI/UI_Button_Primary_Normal.png");
            _uiButtonPressedSprite = TryLoadSceneUiSprite("UI button pressed", "Assets/Art/UI/UI_Button_Primary_Pressed.png");
            _leaderboardIconSprite = TryLoadSceneUiSprite("leaderboard icon", "Assets/Art/Icons/Icon_Leaderboard_01.png");
#endif
        }

        private void StyleMatchRankingPanelImage()
        {
            DotArenaUiStyleCatalog.ApplyPanelSprite(
                _matchRankingPanel,
                _matchRankingPanelSprite,
                new Color(1f, 1f, 1f, 0.72f),
                raycastTarget: false);
        }

#if UNITY_EDITOR
        private static Sprite? TryLoadSceneUiSprite(string label, string assetPath)
        {
            var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                Debug.LogWarning($"[DotArena] {label} sprite not found: {assetPath}");
            }

            return sprite;
        }
#endif

        private void ApplyButtonIcon(Button? button, Sprite? iconSprite)
        {
            if (button == null || iconSprite == null)
            {
                return;
            }

            var iconTransform = button.transform.Find("Icon");
            GameObject iconObject;
            if (iconTransform == null)
            {
                iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(button.transform, false);
            }
            else
            {
                iconObject = iconTransform.gameObject;
            }

            var rect = (RectTransform)iconObject.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(10f, 0f);
            rect.sizeDelta = new Vector2(20f, 20f);

            var image = iconObject.GetComponent<Image>();
            image.sprite = iconSprite;
            image.type = Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;

            if (button.transform.Find("Label") is RectTransform labelRect)
            {
                labelRect.offsetMin = new Vector2(28f, 0f);
                labelRect.offsetMax = Vector2.zero;
            }
        }

        private TMP_Text? FindSceneUiText(string path)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            return target != null ? target.GetComponent<TMP_Text>() : null;
        }

        private Button? FindSceneUiButton(string path)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            return target != null ? target.GetComponent<Button>() : null;
        }

        private TMP_InputField? FindSceneUiInputField(string path)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            return target != null ? target.GetComponent<TMP_InputField>() : null;
        }

        private RectTransform? FindSceneUiRect(string path)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            return target != null ? target.GetComponent<RectTransform>() : null;
        }

        private static void SetText(TMP_Text? label, string value)
        {
            if (label == null || label.text == value)
            {
                return;
            }

            label.text = value;
        }
    }
}
