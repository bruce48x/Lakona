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
            StylePanelImage(_modeSelectPanel, Color.clear);
            StylePanelImage(_loginPanel, UiPanelBackgroundColor);
            StylePanelImage(_matchmakingPanel, UiPanelBackgroundColor);
            StylePanelImage(_lobbyPanel, UiPanelBackgroundColor);
            StylePanelImage(_settlementPanel, UiPanelBackgroundColor);

            StyleText(_hudTitleText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_entryTitleText, UiPrimaryTextColor, 30f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);

            StyleText(_hudModeText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudHintText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudCountdownText, UiAccentTextColor, 18f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchRankingTitleText, UiAccentTextColor, 18f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchRankingHeaderText, UiMutedTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            foreach (var row in _matchRankingRows)
            {
                StyleText(row.RankText, UiSecondaryTextColor, 12f, false, TextAlignmentOptions.Left, TextOverflowModes.Ellipsis);
                StyleText(row.NameText, UiPrimaryTextColor, 12f, false, TextAlignmentOptions.Left, TextOverflowModes.Ellipsis);
                StyleText(row.MassText, UiSecondaryTextColor, 12f, false, TextAlignmentOptions.Right, TextOverflowModes.Ellipsis);
            }

            StyleText(_entryStatusText, UiSecondaryTextColor, 13f, true, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchmakingTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchmakingDetailText, UiSecondaryTextColor, 13f, true, TextAlignmentOptions.Center, TextOverflowModes.Overflow);
            StyleText(_lobbyTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyProfilePlayerText, UiPrimaryTextColor, 18f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyProfileWinsText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyProfileVictoryPointsText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyLeaderboardPeriodText, UiAccentTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            if (_lobbyLeaderboardPeriodText != null)
            {
                _lobbyLeaderboardPeriodText.fontStyle = FontStyles.Bold;
            }

            StyleText(_lobbyLeaderboardHeaderText, Color.white, 12f, false, TextAlignmentOptions.Left, TextOverflowModes.Ellipsis);
            if (_lobbyLeaderboardHeaderText != null)
            {
                _lobbyLeaderboardHeaderText.fontStyle = FontStyles.Bold;
                _lobbyLeaderboardHeaderText.richText = true;
            }

            StyleText(_lobbyLeaderboardEmptyText, UiMutedTextColor, 14f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            if (_lobbyLeaderboardEmptyText != null)
            {
                _lobbyLeaderboardEmptyText.fontStyle = FontStyles.Italic;
            }

            foreach (var row in _lobbyLeaderboardRows)
            {
                StyleText(row, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Left, TextOverflowModes.Ellipsis);
                row.richText = true;
            }
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
            StyleButton(_settlementPrimaryButton);
            StyleButton(_settlementSecondaryButton);
            StyleText(_singlePlayerButtonText, Color.white, 15f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_invincibleSinglePlayerButtonText, Color.white, 15f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_multiplayerButtonText, Color.white, 15f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
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
            StyleLegacyInputField(_accountLegacyInputField);
            StyleLegacyInputField(_passwordLegacyInputField);
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
            DotArenaUiStyleCatalog.ApplyButton(button, _uiButtonSprite);
        }

        private static void StyleInputField(TMP_InputField? inputField)
        {
            DotArenaUiStyleCatalog.ApplyInputField(inputField);
        }

        private static void StyleLegacyInputField(InputField? inputField)
        {
            if (inputField == null)
            {
                return;
            }

            if (inputField.targetGraphic is Image image)
            {
                image.color = UiInputBackgroundColor;
                image.raycastTarget = true;
            }

            if (inputField.textComponent != null)
            {
                inputField.textComponent.color = UiPrimaryTextColor;
                inputField.textComponent.fontSize = 13;
                inputField.textComponent.alignment = TextAnchor.MiddleLeft;
                inputField.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
                inputField.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            }

            if (inputField.placeholder is Text placeholder)
            {
                placeholder.color = UiMutedTextColor;
                placeholder.fontSize = 13;
                placeholder.alignment = TextAnchor.MiddleLeft;
                placeholder.horizontalOverflow = HorizontalWrapMode.Overflow;
                placeholder.verticalOverflow = VerticalWrapMode.Truncate;
            }

            var colors = inputField.colors;
            colors.normalColor = UiInputBackgroundColor;
            colors.highlightedColor = new Color(0.8f, 0.95f, 0.98f, 1f);
            colors.pressedColor = new Color(0.76f, 0.91f, 0.95f, 1f);
            colors.disabledColor = new Color(0.78f, 0.84f, 0.86f, 0.7f);
            colors.selectedColor = colors.highlightedColor;
            colors.colorMultiplier = 1f;
            inputField.colors = colors;
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

        private static void EnsureLegacyInputFieldViewport(InputField? inputField)
        {
            if (inputField?.textComponent == null)
            {
                return;
            }

            var rect = inputField.textComponent.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(12f, 5f);
            rect.offsetMax = new Vector2(-12f, -5f);

            if (inputField.placeholder is Graphic placeholder)
            {
                var placeholderRect = placeholder.rectTransform;
                placeholderRect.anchorMin = Vector2.zero;
                placeholderRect.anchorMax = Vector2.one;
                placeholderRect.pivot = new Vector2(0.5f, 0.5f);
                placeholderRect.offsetMin = new Vector2(12f, 5f);
                placeholderRect.offsetMax = new Vector2(-12f, -5f);
            }
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

        private void EnsureRuntimeUiSprites()
        {
            _uiPanelSprite ??= DotArenaSpriteFactory.CreateRoundedRectSprite(64, 10f);
            _uiButtonSprite ??= DotArenaSpriteFactory.CreateRoundedRectSprite(64, 12f);
        }

        private void StyleMatchRankingPanelImage()
        {
            DotArenaUiStyleCatalog.ApplyPanelSprite(
                _matchRankingPanel,
                null,
                new Color(1f, 1f, 1f, 0.78f),
                raycastTarget: false);
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

        private TMP_InputField? FindOrCreateSceneUiInputField(string path, TMP_InputField.ContentType contentType)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            if (target == null)
            {
                WarnMissingAuthoredSceneUi(path);
                return null;
            }

            var inputField = target.GetComponent<TMP_InputField>();
            if (inputField == null)
            {
                inputField = target.gameObject.AddComponent<TMP_InputField>();
            }

            inputField.contentType = contentType;
            inputField.targetGraphic = target.GetComponent<Image>();
            inputField.textViewport = target.Find("Text Area") as RectTransform;
            inputField.textComponent = target.Find("Text Area/Text")?.GetComponent<TMP_Text>();
            inputField.placeholder = target.Find("Text Area/Placeholder")?.GetComponent<TMP_Text>();

            if (inputField.textViewport == null || inputField.textComponent == null || inputField.placeholder == null)
            {
                WarnMissingAuthoredSceneUi($"{path}/Text Area/Text or Placeholder");
                return null;
            }

            return inputField;
        }

        private InputField? FindSceneUiLegacyInputField(string path)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            return target != null ? target.GetComponent<InputField>() : null;
        }

        private InputField? FindOrCreateSceneUiLegacyInputField(string path, InputField.ContentType contentType)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            if (target == null)
            {
                WarnMissingAuthoredSceneUi(path);
                return null;
            }

            var inputField = target.GetComponent<InputField>();
            if (inputField == null)
            {
                inputField = target.gameObject.AddComponent<InputField>();
            }

            inputField.contentType = contentType;
            inputField.targetGraphic = target.GetComponent<Image>();
            inputField.textComponent = target.Find("Text Area/Text")?.GetComponent<Text>();
            inputField.placeholder = target.Find("Text Area/Placeholder")?.GetComponent<Text>();

            if (inputField.textComponent == null || inputField.placeholder == null)
            {
                WarnMissingAuthoredSceneUi($"{path}/Text Area/Text or Placeholder");
                return null;
            }

            return inputField;
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

        private static string BuildSceneUiPath(Transform transform)
        {
            var path = transform.name;
            var current = transform.parent;
            while (current != null && current.name != "DotArenaGame")
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private static void WarnMissingAuthoredSceneUi(string path)
        {
            Debug.LogWarning($"[DotArena] SceneUI prefab is missing required object or component: {path}");
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
