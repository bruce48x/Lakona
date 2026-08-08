#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SampleClient.Gameplay.Tests
{
    public sealed class DotArenaSceneUiPrefabTests
    {
        private const string SceneUiPrefabPath = "Assets/Prefabs/UI/SceneUI.prefab";
        private const string ModeSelectPanelPrefabPath = "Assets/Prefabs/UI/ModeSelectPanel.prefab";
        private const string LoginPanelPrefabPath = "Assets/Prefabs/UI/LoginPanel.prefab";
        private const string LobbyPanelPrefabPath = "Assets/Prefabs/UI/LobbyPanel.prefab";
        private const string MatchmakingPanelPrefabPath = "Assets/Prefabs/UI/MatchmakingPanel.prefab";
        private const string SettlementPanelPrefabPath = "Assets/Prefabs/UI/SettlementPanel.prefab";
        private const string RuntimeFontResourcePath = "Fonts & Materials/LiberationSans SDF";
        private const string TmpFontMaterialsPath = "Assets/TextMesh Pro/Resources/Fonts & Materials";
        private const string LegacyTmpFontsPath = "Assets/TextMesh Pro/Fonts";
        private static readonly string[] ImageAssetExtensions =
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
            ".tga",
            ".psd",
            ".bmp",
            ".gif",
            ".tif",
            ".tiff",
            ".exr",
            ".hdr",
            ".svg"
        };

        [Test]
        public void SceneUiPrefabContainsStablePresenterPaths()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            AssertPath(prefab!, "MenuBackground");
            AssertPath(prefab!, "OverlayLayer");
            AssertPath(prefab!, "OverlayLayer/TopStatusPanel");
            AssertPath(prefab!, "OverlayLayer/TopStatusPanel/CountdownText");
            AssertPath(prefab!, "HUDPanel");
            AssertPath(prefab!, "MatchRankingPanel");
            AssertPath(prefab!, "MatchRankingPanel/TitleText");
            AssertPath(prefab!, "MatchRankingPanel/HeaderText");
            AssertPath(prefab!, "MatchRankingPanel/Row1/RankText");
            AssertPath(prefab!, "MatchRankingPanel/Row10/MassText");
            AssertPath(prefab!, "ModeSelectPanel/SinglePlayerButton/Label");
            AssertPath(prefab!, "ModeSelectPanel/InvincibleSinglePlayerButton/Label");
            AssertPath(prefab!, "ModeSelectPanel/MultiplayerButton/Label");
            AssertPath(prefab!, "LoginPanel/TitleText");
            AssertPath(prefab!, "LoginPanel/StatusText");
            AssertPath(prefab!, "LoginPanel/AccountInput");
            AssertPath(prefab!, "LoginPanel/PasswordInput");
            AssertPath(prefab!, "LoginPanel/MatchButton/Label");
            AssertPath(prefab!, "LoginPanel/GuestLoginButton/Label");
            AssertPath(prefab!, "LoginPanel/BackButton/Label");
            AssertNoPath(prefab!, "MatchmakingPanel/TitleText");
            AssertPath(prefab!, "MatchmakingPanel/DetailText");
            AssertPath(prefab!, "MatchmakingPanel/CancelButton/Label");
            AssertPath(prefab!, "LobbyPanel/TitleText");
            AssertPath(prefab!, "LobbyPanel/ProfileButton/Label");
            AssertPath(prefab!, "LobbyPanel/LeaderboardButton/Label");
            AssertPath(prefab!, "LobbyPanel/ProfileContent/PlayerText");
            AssertPath(prefab!, "LobbyPanel/ProfileContent/WinsText");
            AssertPath(prefab!, "LobbyPanel/ProfileContent/VictoryPointsText");
            AssertPath(prefab!, "LobbyPanel/LeaderboardContent/PeriodText");
            AssertPath(prefab!, "LobbyPanel/LeaderboardContent/HeaderText");
            AssertPath(prefab!, "LobbyPanel/LeaderboardContent/Row10Text");
            AssertPath(prefab!, "LobbyPanel/PrimaryActionButton/Label");
            AssertPath(prefab!, "LobbyPanel/SecondaryActionButton/Label");
            AssertPath(prefab!, "SettlementPanel/TitleText");
            AssertPath(prefab!, "SettlementPanel/PrimaryButton/Label");
            AssertPath(prefab!, "SettlementPanel/SecondaryButton/Label");
        }

        [Test]
        public void SceneUiScreenPanelsAreStandalonePrefabs()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(ModeSelectPanelPrefabPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(LoginPanelPrefabPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPanelPrefabPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(MatchmakingPanelPrefabPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(SettlementPanelPrefabPath), Is.Not.Null);
        }

        [Test]
        public void SceneUiCountdownIsHiddenByDefault()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            var countdown = prefab!.transform.Find("OverlayLayer/TopStatusPanel/CountdownText");
            Assert.That(countdown, Is.Not.Null);
            Assert.That(countdown!.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void MatchmakingPanelPrefabUsesOneCenteredStatusText()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MatchmakingPanelPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            AssertNoPath(prefab!, "TitleText");
            AssertRect(prefab, "DetailText", new Vector2(0f, 30f), new Vector2(300f, 40f));
            AssertAnchorsAndPivot(prefab, "DetailText", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f));

            var detail = prefab.transform.Find("DetailText")!.GetComponent<TMP_Text>();
            Assert.That(detail.alignment, Is.EqualTo(TextAlignmentOptions.Center));
            Assert.That(detail.fontSize, Is.EqualTo(24f));
            Assert.That(detail.fontStyle, Is.EqualTo(FontStyles.Bold));
        }

        [Test]
        public void MatchmakingDetailContainsOnlyElapsedWaitingInformation()
        {
            var gameplayRoot = Path.Combine(Application.dataPath, "Scripts", "Gameplay");
            var composerSource = File.ReadAllText(Path.Combine(gameplayRoot, "DotArenaUiTextComposer.cs"));

            Assert.That(composerSource, Does.Contain("return $\"{elapsedText}\";"));
            Assert.That(composerSource, Does.Contain("return \"\";"));
            Assert.That(composerSource, Does.Not.Contain("You can cancel matchmaking at any time."));
            Assert.That(composerSource, Does.Not.Contain("Finding match"));
        }

        [Test]
        public void SceneUiPrefabContainsNestedScreenPanelInstances()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            AssertNestedPrefabInstance(prefab!, "ModeSelectPanel", ModeSelectPanelPrefabPath);
            AssertNestedPrefabInstance(prefab!, "LoginPanel", LoginPanelPrefabPath);
            AssertNestedPrefabInstance(prefab!, "LobbyPanel", LobbyPanelPrefabPath);
            AssertNestedPrefabInstance(prefab!, "MatchmakingPanel", MatchmakingPanelPrefabPath);
            AssertNestedPrefabInstance(prefab!, "SettlementPanel", SettlementPanelPrefabPath);
            AssertPath(prefab!, "ModeSelectPanel/SinglePlayerButton/Label");
            AssertPath(prefab!, "ModeSelectPanel/InvincibleSinglePlayerButton/Label");
            AssertPath(prefab!, "ModeSelectPanel/MultiplayerButton/Label");
            AssertPath(prefab!, "LoginPanel/AccountInput");
            AssertPath(prefab!, "LoginPanel/PasswordInput");
            AssertPath(prefab!, "LoginPanel/MatchButton/Label");
            AssertPath(prefab!, "LoginPanel/GuestLoginButton/Label");
            AssertPath(prefab!, "LoginPanel/BackButton/Label");
            AssertPath(prefab!, "LobbyPanel/TitleText");
            AssertPath(prefab!, "LobbyPanel/ProfileButton/Label");
            AssertPath(prefab!, "LobbyPanel/LeaderboardButton/Label");
            AssertPath(prefab!, "LobbyPanel/ProfileContent/VictoryPointsText");
            AssertPath(prefab!, "LobbyPanel/LeaderboardContent/Row1Text");
            AssertPath(prefab!, "LobbyPanel/LeaderboardContent/Row10Text");
            AssertPath(prefab!, "LobbyPanel/PrimaryActionButton/Label");
            AssertPath(prefab!, "LobbyPanel/SecondaryActionButton/Label");
            AssertPath(prefab!, "MatchmakingPanel/CancelButton/Label");
            AssertPath(prefab!, "SettlementPanel/PrimaryButton/Label");
            AssertPath(prefab!, "SettlementPanel/SecondaryButton/Label");
        }

        [Test]
        public void SceneUiPrefabDoesNotOwnLoginOrLobbyInternalsThroughEntryPanel()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab!.transform.Find("EntryPanel"), Is.Null);
            Assert.That(prefab!.transform.Find("EntryPanel/ModeSelectPanel"), Is.Null);
            Assert.That(prefab.transform.Find("EntryPanel/MultiplayerPanel"), Is.Null);
            Assert.That(prefab.transform.Find("EntryPanel/MultiplayerPanel/AccountInput"), Is.Null);
            Assert.That(prefab.transform.Find("EntryPanel/MultiplayerPanel/MatchButton"), Is.Null);
        }

        [Test]
        public void SceneUiPrefabContainsAuthoredInputShellsAndNoMissingScripts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(CountMissingScripts(prefab!), Is.Zero);
            AssertInputShell(prefab!, "LoginPanel/AccountInput");
            AssertInputShell(prefab!, "LoginPanel/PasswordInput");
        }

        [Test]
        public void SceneUiPrefabMatchesShellThemeAndNestedPanelPlacement()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            AssertGradientBackground(prefab!, "MenuBackground");
            AssertRect(prefab!, "ModeSelectPanel", new Vector2(0f, 0f), new Vector2(430f, 300f));
            AssertImageColor(prefab!, "ModeSelectPanel", Color.clear);
            AssertRect(prefab!, "LoginPanel", new Vector2(0f, 0f), new Vector2(430f, 430f));
            AssertStretchRect(prefab!, "LobbyPanel", new Vector2(54f, 42f), new Vector2(-54f, -42f));
            AssertImageColor(prefab!, "LoginPanel", new Color(0.98f, 1f, 1f, 0.88f));
            AssertGradientButton(prefab!, "ModeSelectPanel/SinglePlayerButton");
            AssertGradientButton(prefab!, "ModeSelectPanel/InvincibleSinglePlayerButton");
            AssertGradientButton(prefab!, "ModeSelectPanel/MultiplayerButton");
            AssertGradientButton(prefab!, "LoginPanel/MatchButton");
            AssertGradientButton(prefab!, "LoginPanel/GuestLoginButton");
            AssertGradientButton(prefab!, "LoginPanel/BackButton");
            AssertText(prefab!, "ModeSelectPanel/SinglePlayerButton/Label", "Single Player: Normal");
            AssertText(prefab!, "ModeSelectPanel/InvincibleSinglePlayerButton/Label", "Single Player: Invincible");
            AssertText(prefab!, "ModeSelectPanel/MultiplayerButton/Label", "Multiplayer");
            AssertText(prefab!, "LoginPanel/AccountLabel", "Account");
            AssertText(prefab!, "LoginPanel/PasswordLabel", "Password");
        }

        [Test]
        public void LoginPanelPrefabOwnsVisibleAuthLayout()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LoginPanelPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            AssertPath(prefab!, "TitleText");
            AssertPath(prefab!, "StatusText");
            AssertPath(prefab!, "AccountLabel");
            AssertInputShell(prefab!, "AccountInput");
            AssertPath(prefab!, "PasswordLabel");
            AssertInputShell(prefab!, "PasswordInput");
            AssertPath(prefab!, "MatchButton/Label");
            AssertPath(prefab!, "GuestLoginButton/Label");
            AssertPath(prefab!, "BackButton/Label");
            AssertChildRectInsideParent(prefab!, string.Empty, "TitleText");
            AssertChildRectInsideParent(prefab!, string.Empty, "StatusText");
            AssertGradientButton(prefab!, "MatchButton");
            AssertGradientButton(prefab!, "GuestLoginButton");
            AssertGradientButton(prefab!, "BackButton");
        }

        [Test]
        public void LobbyPanelPrefabOwnsAlignedActionLayout()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPanelPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            AssertPath(prefab!, "TitleText");
            AssertPath(prefab!, "ProfileButton/Label");
            AssertPath(prefab!, "LeaderboardButton/Label");
            AssertPath(prefab!, "ProfileContent/PlayerText");
            AssertPath(prefab!, "ProfileContent/WinsText");
            AssertPath(prefab!, "ProfileContent/VictoryPointsText");
            AssertPath(prefab!, "LeaderboardContent/PeriodText");
            AssertPath(prefab!, "LeaderboardContent/HeaderText");
            AssertPath(prefab!, "LeaderboardContent/EmptyText");
            AssertPath(prefab!, "LeaderboardContent/Row1Text");
            AssertPath(prefab!, "LeaderboardContent/Row10Text");
            AssertNoPath(prefab!, "SettingsButton");
            AssertNoPath(prefab!, "QuickActionsText");
            AssertNoPath(prefab!, "QuickActionButton1");
            AssertNoPath(prefab!, "QuickActionButton2");
            AssertNoPath(prefab!, "QuickActionButton3");
            AssertNoPath(prefab!, "QuickActionButton4");
            AssertPath(prefab!, "PrimaryActionButton/Label");
            AssertPath(prefab!, "SecondaryActionButton/Label");
            AssertAnchoredPosition(prefab!, "ProfileButton", new Vector2(-90f, -154f));
            AssertAnchoredPosition(prefab!, "LeaderboardButton", new Vector2(90f, -154f));
            Assert.That(prefab.transform.Find("ProfileContent")!.gameObject.activeSelf, Is.True);
            Assert.That(prefab.transform.Find("LeaderboardContent")!.gameObject.activeSelf, Is.False);
            var leaderboardContent = prefab.transform.Find("LeaderboardContent")!;
            Assert.That(leaderboardContent.GetComponent<DotArenaGradientGraphic>(), Is.Not.Null);
            Assert.That(leaderboardContent.GetComponent<Shadow>(), Is.Not.Null);
            Assert.That(leaderboardContent.GetComponent<DotArenaGradientGraphic>()!.raycastTarget, Is.False);
            Assert.That(leaderboardContent.GetComponent<DotArenaGradientGraphic>()!.CornerRadius, Is.EqualTo(20f));
            Assert.That(leaderboardContent.GetComponent<RectTransform>().sizeDelta, Is.EqualTo(new Vector2(800f, 260f)));
            var leaderboardHeaderBand = leaderboardContent.Find("HeaderBand");
            Assert.That(leaderboardHeaderBand, Is.Not.Null);
            Assert.That(leaderboardHeaderBand!.GetSiblingIndex(), Is.Zero);
            Assert.That(leaderboardHeaderBand.GetComponent<DotArenaGradientGraphic>(), Is.Not.Null);
            Assert.That(leaderboardHeaderBand.GetComponent<DotArenaGradientGraphic>()!.raycastTarget, Is.False);
            Assert.That(leaderboardContent.Find("HeaderText")!.GetComponent<TMP_Text>().richText, Is.True);
            Assert.That(leaderboardContent.Find("Row1Text")!.GetComponent<TMP_Text>().fontSize, Is.EqualTo(13f));
            Assert.That(leaderboardContent.Find("Row1Text")!.GetComponent<TMP_Text>().richText, Is.True);
            AssertChildRectInsideParent(prefab!, string.Empty, "PrimaryActionButton");
            AssertChildRectInsideParent(prefab!, string.Empty, "SecondaryActionButton");
            AssertGradientButton(prefab!, "PrimaryActionButton");
            AssertGradientButton(prefab!, "SecondaryActionButton");
        }

        [Test]
        public void LobbyProfileAndLeaderboardUseServerBackedSnapshotData()
        {
            var gameplayRoot = Path.Combine(Application.dataPath, "Scripts", "Gameplay");
            var sessionSource = File.ReadAllText(Path.Combine(gameplayRoot, "DotArenaGame.Session.cs"));
            var snapshotSource = File.ReadAllText(Path.Combine(gameplayRoot, "DotArenaGame.UiSnapshot.cs"));
            var refreshSource = File.ReadAllText(Path.Combine(gameplayRoot, "DotArenaSceneUiPresenter.Refresh.cs"));

            Assert.That(sessionSource, Does.Contain("reply.WinCount, reply.VictoryPoints"));
            Assert.That(snapshotSource, Does.Contain("ProfileVictoryPoints = Math.Max(0, _owner._localVictoryPoints)"));
            Assert.That(snapshotSource, Does.Contain("LeaderboardEntries = leaderboardEntries"));
            Assert.That(refreshSource, Does.Contain("_lobbyProfileVictoryPointsText"));
            Assert.That(refreshSource, Does.Contain("_lobbyLeaderboardRows"));
        }

        [Test]
        public void LobbyPanelPrefabKeepsActionButtonsVisibleAtRuntimeCompactHeight()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPanelPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            var instance = Object.Instantiate(prefab!);
            try
            {
                var rect = instance.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(1050f, 490f);

                AssertChildRectInsideParent(instance, string.Empty, "PrimaryActionButton");
                AssertChildRectInsideParent(instance, string.Empty, "SecondaryActionButton");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SceneUiPrefabButtonsUseEditorVisibleRoundedGradients()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            foreach (var button in prefab!.GetComponentsInChildren<Button>(true))
            {
                AssertGradientButton(prefab, GetTransformPathRelativeToRoot(prefab.transform, button.transform));
            }
        }

        [Test]
        public void SceneUiPrefabRuntimeHudDoesNotExposeDebugText()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab!.transform.Find("OverlayLayer/TopStatusPanel/StatusText"), Is.Null, "Runtime HUD status text is debug information.");
            Assert.That(prefab.transform.Find("HUDPanel/StatusText"), Is.Null, "Runtime HUD status text is debug information.");
            Assert.That(prefab.transform.Find("HUDPanel/TickText"), Is.Null, "World tick text is debug information.");
            Assert.That(prefab.transform.Find("HUDPanel/EventText"), Is.Null, "HUD event debug slot should not be player visible.");
            Assert.That(prefab.transform.Find("DebugPanel"), Is.Null, "Debug panel should not be shipped in the player-facing UI.");
        }

        [Test]
        public void PlayerFacingPresentationDoesNotExposeInternalIdsOrDuplicateWorldLabels()
        {
            var gameplayRoot = Path.Combine(Application.dataPath, "Scripts", "Gameplay");
            var viewSource = File.ReadAllText(Path.Combine(gameplayRoot, "DotArenaGame.Views.cs"));
            var overlaySource = File.ReadAllText(Path.Combine(gameplayRoot, "DotArenaPlayerOverlayPresenter.cs"));
            var synchronizerSource = File.ReadAllText(Path.Combine(gameplayRoot, "DotArenaWorldSynchronizer.cs"));
            var callbackSource = File.ReadAllText(Path.Combine(gameplayRoot, "DotArenaGame.Callbacks.cs"));
            var presenterSource = File.ReadAllText(Path.Combine(gameplayRoot, "DotArenaSceneUiPresenter.cs"));

            Assert.That(viewSource, Does.Not.Contain("NameBackdrop"));
            Assert.That(viewSource, Does.Not.Contain("MassBackdrop"));
            Assert.That(viewSource, Does.Not.Contain("NameLabel"));
            Assert.That(viewSource, Does.Not.Contain("MassLabel"));
            Assert.That(overlaySource, Does.Not.Contain("MassText"));
            Assert.That(overlaySource, Does.Contain("RectTransformUtility.ScreenPointToLocalPointInRectangle"));
            Assert.That(synchronizerSource, Does.Not.Contain("overlay.NameText.text = player.PlayerId"));
            Assert.That(callbackSource, Does.Not.Contain("realtimeConnection.Host"));
            Assert.That(callbackSource, Does.Not.Contain("Winner: {matchEnd.WinnerPlayerId}"));
            Assert.That(presenterSource, Does.Contain("private const int MatchRankingMaxRows = 10;"));
        }

        [Test]
        public void LiveRankingUsesAndContainsAllTenAuthoredRows()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Assert.That(prefab!.transform.Find("MatchRankingPanel")!.gameObject.activeSelf, Is.False);
            AssertRect(prefab, "MatchRankingPanel", new Vector2(-18f, 0f), new Vector2(246f, 410f));
            AssertAnchorsAndPivot(prefab, "MatchRankingPanel", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            Assert.That(prefab.transform.Find("MatchRankingPanel")!.GetComponent<Image>().raycastTarget, Is.False);
            AssertRect(prefab, "MatchRankingPanel/TitleText", new Vector2(0f, -16f), new Vector2(220f, 28f));
            AssertRect(prefab, "MatchRankingPanel/HeaderText", new Vector2(0f, -48f), new Vector2(224f, 20f));
            AssertAnchorsAndPivot(prefab, "MatchRankingPanel/TitleText", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            AssertAnchorsAndPivot(prefab, "MatchRankingPanel/HeaderText", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            AssertNoSiblingRectOverlap(prefab, "MatchRankingPanel", "TitleText", "HeaderText");

            for (var rank = 1; rank <= 10; rank++)
            {
                var rowPath = $"MatchRankingPanel/Row{rank}";
                AssertRect(prefab, rowPath, new Vector2(0f, -76f - ((rank - 1) * 29f)), new Vector2(224f, 24f));
                AssertAnchorsAndPivot(prefab, rowPath, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                Assert.That(prefab.transform.Find(rowPath)!.GetComponent<Image>().raycastTarget, Is.False, rowPath);
                AssertRect(prefab, $"{rowPath}/RankText", new Vector2(0f, 0f), new Vector2(40f, 22f));
                AssertRect(prefab, $"{rowPath}/NameText", new Vector2(40f, 0f), new Vector2(124f, 22f));
                AssertRect(prefab, $"{rowPath}/MassText", new Vector2(164f, 0f), new Vector2(60f, 22f));
                AssertAnchorsAndPivot(prefab, $"{rowPath}/RankText", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
                AssertAnchorsAndPivot(prefab, $"{rowPath}/NameText", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
                AssertAnchorsAndPivot(prefab, $"{rowPath}/MassText", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
                AssertNoSiblingRectOverlap(prefab, rowPath, "RankText", "NameText");
                AssertNoSiblingRectOverlap(prefab, rowPath, "NameText", "MassText");
            }

            var authoredLayout = CaptureMatchRankingLayout(prefab);

            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var gameplayAssembly = typeof(SampleClient.Gameplay.DotArenaGame).Assembly;
                var presenterType = gameplayAssembly.GetType("SampleClient.Gameplay.DotArenaSceneUiPresenter", throwOnError: true)!;
                var entryType = gameplayAssembly.GetType("SampleClient.Gameplay.DotArenaMatchRankingEntry", throwOnError: true)!;
                var presenter = System.Activator.CreateInstance(presenterType)!;
                var panel = instance.transform.Find("MatchRankingPanel");
                Assert.That(panel, Is.Not.Null);

                presenterType
                    .GetField("_matchRankingPanel", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(presenter, panel!.gameObject);
                presenterType
                    .GetMethod("BindMatchRankingPanelContents", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(presenter, null);

                AssertMatchRankingLayoutUnchanged(instance, authoredLayout);

                var entries = (IList)System.Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(entryType))!;
                for (var rank = 1; rank <= 10; rank++)
                {
                    entries.Add(System.Activator.CreateInstance(entryType, rank, $"Player {rank}", 1000f - rank, rank == 10)!);
                }

                presenterType
                    .GetMethod("RefreshMatchRankingRows", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(presenter, new object[] { entries, true });

                AssertChildRectInsideParent(instance, "MatchRankingPanel", "MatchRankingPanel/Row10");
                for (var rank = 8; rank <= 10; rank++)
                {
                    var row = panel.Find($"Row{rank}");
                    Assert.That(row, Is.Not.Null);
                    Assert.That(row!.gameObject.activeSelf, Is.True);
                    Assert.That(row.Find("RankText").GetComponent<TMP_Text>().text, Is.EqualTo($"#{rank}"));
                    Assert.That(row.Find("NameText").GetComponent<TMP_Text>().text, Is.EqualTo($"Player {rank}"));
                    Assert.That(row.Find("MassText").GetComponent<TMP_Text>().text, Is.Not.EqualTo("0"));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Dictionary<string, string> CaptureMatchRankingLayout(GameObject root)
        {
            var layout = new Dictionary<string, string>();
            AddRectState(layout, root, "MatchRankingPanel");
            AddRectState(layout, root, "MatchRankingPanel/TitleText");
            AddRectState(layout, root, "MatchRankingPanel/HeaderText");
            for (var rank = 1; rank <= 10; rank++)
            {
                var rowPath = $"MatchRankingPanel/Row{rank}";
                AddRectState(layout, root, rowPath);
                AddRectState(layout, root, $"{rowPath}/RankText");
                AddRectState(layout, root, $"{rowPath}/NameText");
                AddRectState(layout, root, $"{rowPath}/MassText");
            }

            return layout;
        }

        private static void AddRectState(IDictionary<string, string> layout, GameObject root, string path)
        {
            var rect = root.transform.Find(path)?.GetComponent<RectTransform>();
            Assert.That(rect, Is.Not.Null, path);
            layout.Add(path, CaptureRectState(rect!));
        }

        private static void AssertMatchRankingLayoutUnchanged(GameObject root, IReadOnlyDictionary<string, string> authoredLayout)
        {
            foreach (var pair in authoredLayout)
            {
                var rect = root.transform.Find(pair.Key)?.GetComponent<RectTransform>();
                Assert.That(rect, Is.Not.Null, pair.Key);
                Assert.That(CaptureRectState(rect!), Is.EqualTo(pair.Value), $"Runtime binding changed authored layout: {pair.Key}");
            }
        }

        private static string CaptureRectState(RectTransform rect)
        {
            return $"{rect.anchorMin.x:R},{rect.anchorMin.y:R};{rect.anchorMax.x:R},{rect.anchorMax.y:R};" +
                   $"{rect.pivot.x:R},{rect.pivot.y:R};{rect.anchoredPosition.x:R},{rect.anchoredPosition.y:R};" +
                   $"{rect.sizeDelta.x:R},{rect.sizeDelta.y:R}";
        }

        [Test]
        public void SceneUiPrefabTopStatusTextsShareOneNonOverlappingOverlayRegion()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            AssertRect(prefab!, "OverlayLayer/TopStatusPanel", new Vector2(0f, -12f), new Vector2(360f, 58f));
        }

        [Test]
        public void ProjectTmpFontIsLoadableForRuntimeCreatedText()
        {
            var font = Resources.Load<TMP_FontAsset>(RuntimeFontResourcePath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneUiPrefabPath);

            Assert.That(font, Is.Not.Null);
            Assert.That(prefab, Is.Not.Null);

            foreach (var text in prefab!.GetComponentsInChildren<TMP_Text>(true))
            {
                Assert.That(text.font, Is.SameAs(font), GetTransformPath(text.transform));
            }
        }

        [Test]
        public void TextMeshProSpriteAssetsAreDisabled()
        {
            var settings = TMP_Settings.instance;

            Assert.That(settings, Is.Not.Null);
            Assert.That(TMP_Settings.defaultSpriteAsset, Is.Null);
        }

        [Test]
        public void ClientUsesOnlyEnglishFonts()
        {
            var tmpFontGuids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { TmpFontMaterialsPath });
            var legacyFontGuids = AssetDatabase.FindAssets("t:Font", new[] { LegacyTmpFontsPath });

            foreach (var guid in tmpFontGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Assert.That(assetPath, Does.Contain("LiberationSans"), assetPath);
            }

            foreach (var guid in legacyFontGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Assert.That(assetPath, Does.Contain("LiberationSans"), assetPath);
            }
        }

        [Test]
        public void ClientAssetsDoNotContainImageFiles()
        {
            var rootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var imageFiles = Directory.EnumerateFiles(Application.dataPath, "*", SearchOption.AllDirectories)
                .Where(path => ImageAssetExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .Select(path => Path.GetRelativePath(rootPath, path).Replace('\\', '/'))
                .OrderBy(path => path)
                .ToArray();

            Assert.That(imageFiles, Is.Empty, string.Join("\n", imageFiles));
        }

        [Test]
        public void SceneUiPresenterDoesNotRepairLoginOrLobbyLayoutAtRuntime()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var gameplaySourcePath = Path.Combine(
                root,
                "Assets",
                "Scripts",
                "Gameplay");
            var presenterSourceFiles = Directory.EnumerateFiles(gameplaySourcePath, "DotArenaSceneUiPresenter*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path)
                .ToArray();
            var layoutSource = string.Join(
                "\n",
                presenterSourceFiles.Select(File.ReadAllText));

            Assert.That(presenterSourceFiles, Is.Not.Empty);
            Assert.That(layoutSource, Does.Not.Contain("EnsureEntryPanelLayout"));
            Assert.That(layoutSource, Does.Not.Contain("EnsureModeSelectButton"));
            Assert.That(layoutSource, Does.Not.Contain("EnsureMultiplayerAuthButton"));
            Assert.That(layoutSource, Does.Not.Contain("EnsureLobbyPanelContents"));
            Assert.That(layoutSource, Does.Not.Contain("QuickAction"));
            Assert.That(layoutSource, Does.Not.Contain("EnsureLobbyButtonElement"));
            Assert.That(layoutSource, Does.Not.Contain("ApplyLobbyActionLayout"));
            Assert.That(layoutSource, Does.Not.Contain("SceneUI/EntryPanel/MultiplayerPanel"));
        }

        private static void AssertPath(GameObject root, string path)
        {
            Assert.That(root.transform.Find(path), Is.Not.Null, path);
        }

        private static void AssertNoPath(GameObject root, string path)
        {
            Assert.That(root.transform.Find(path) == null, Is.True, path);
        }

        private static void AssertAnchoredPosition(GameObject root, string path, Vector2 expected)
        {
            var rect = root.transform.Find(path) as RectTransform;

            Assert.That(rect, Is.Not.Null, path);
            Assert.That(rect!.anchoredPosition, Is.EqualTo(expected), path);
        }

        private static void AssertNestedPrefabInstance(GameObject root, string path, string expectedPrefabPath)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(target!.gameObject);
            Assert.That(instanceRoot, Is.SameAs(target.gameObject), path);

            var source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot!);
            Assert.That(source, Is.Not.Null, path);
            Assert.That(AssetDatabase.GetAssetPath(source!), Is.EqualTo(expectedPrefabPath), path);
        }

        private static void AssertInputShell(GameObject root, string path)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            Assert.That(target!.GetComponent<Image>(), Is.Not.Null, path);
            Assert.That(target.Find("Text Area"), Is.Not.Null, $"{path}/Text Area");
            Assert.That(target.Find("Text Area/Placeholder")?.GetComponent<TMP_Text>(), Is.Not.Null, $"{path}/Text Area/Placeholder");
            Assert.That(target.Find("Text Area/Text")?.GetComponent<TMP_Text>(), Is.Not.Null, $"{path}/Text Area/Text");
        }

        private static void AssertRect(GameObject root, string path, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var rect = target!.GetComponent<RectTransform>();
            Assert.That(rect, Is.Not.Null, path);
            Assert.That(rect!.anchoredPosition.x, Is.EqualTo(anchoredPosition.x).Within(0.01f), $"{path} x");
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(anchoredPosition.y).Within(0.01f), $"{path} y");
            Assert.That(rect.sizeDelta.x, Is.EqualTo(sizeDelta.x).Within(0.01f), $"{path} width");
            Assert.That(rect.sizeDelta.y, Is.EqualTo(sizeDelta.y).Within(0.01f), $"{path} height");
        }

        private static void AssertAnchorsAndPivot(GameObject root, string path, Vector2 anchors, Vector2 pivot)
        {
            var rect = root.transform.Find(path)?.GetComponent<RectTransform>();

            Assert.That(rect, Is.Not.Null, path);
            Assert.That(rect!.anchorMin, Is.EqualTo(anchors), $"{path} anchorMin");
            Assert.That(rect.anchorMax, Is.EqualTo(anchors), $"{path} anchorMax");
            Assert.That(rect.pivot, Is.EqualTo(pivot), $"{path} pivot");
        }

        private static void AssertStretchRect(GameObject root, string path, Vector2 offsetMin, Vector2 offsetMax)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var rect = target!.GetComponent<RectTransform>();
            Assert.That(rect, Is.Not.Null, path);
            Assert.That(rect!.anchorMin, Is.EqualTo(Vector2.zero), $"{path} anchorMin");
            Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one), $"{path} anchorMax");
            Assert.That(rect.offsetMin.x, Is.EqualTo(offsetMin.x).Within(0.01f), $"{path} offsetMin x");
            Assert.That(rect.offsetMin.y, Is.EqualTo(offsetMin.y).Within(0.01f), $"{path} offsetMin y");
            Assert.That(rect.offsetMax.x, Is.EqualTo(offsetMax.x).Within(0.01f), $"{path} offsetMax x");
            Assert.That(rect.offsetMax.y, Is.EqualTo(offsetMax.y).Within(0.01f), $"{path} offsetMax y");
        }

        private static void AssertImageColor(GameObject root, string path, Color expected)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var image = target!.GetComponent<Image>();
            Assert.That(image, Is.Not.Null, path);
            Assert.That(image!.color.r, Is.EqualTo(expected.r).Within(0.001f), $"{path} r");
            Assert.That(image.color.g, Is.EqualTo(expected.g).Within(0.001f), $"{path} g");
            Assert.That(image.color.b, Is.EqualTo(expected.b).Within(0.001f), $"{path} b");
            Assert.That(image.color.a, Is.EqualTo(expected.a).Within(0.001f), $"{path} a");
        }

        private static void AssertGradientBackground(GameObject root, string path)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var gradient = target!.GetComponent<DotArenaGradientGraphic>();
            Assert.That(gradient, Is.Not.Null, path);
            AssertColor(gradient!.TopLeft, new Color(0.88f, 0.98f, 1f, 1f), $"{path} top left");
            AssertColor(gradient.TopRight, new Color(0.98f, 1f, 0.97f, 1f), $"{path} top right");
            AssertColor(gradient.BottomLeft, new Color(0.78f, 0.95f, 1f, 1f), $"{path} bottom left");
            AssertColor(gradient.BottomRight, new Color(1f, 0.91f, 0.86f, 1f), $"{path} bottom right");
            Assert.That(target.GetComponent<Image>(), Is.Null, $"{path} should use an editor-visible gradient graphic instead of Image");
        }

        private static void AssertGradientButton(GameObject root, string path)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var gradient = target!.GetComponent<DotArenaGradientGraphic>();
            Assert.That(gradient, Is.Not.Null, path);
            Assert.That(target.GetComponent<Image>(), Is.Null, $"{path} should use an editor-visible rounded gradient graphic instead of Image");

            var serialized = new SerializedObject(gradient!);
            var cornerRadius = serialized.FindProperty("_cornerRadius");
            Assert.That(cornerRadius, Is.Not.Null, $"{path} corner radius");
            Assert.That(cornerRadius!.floatValue, Is.GreaterThan(0f), $"{path} corner radius");

            var button = target.GetComponent<Button>();
            Assert.That(button, Is.Not.Null, path);
            Assert.That(button!.targetGraphic, Is.SameAs(gradient), $"{path} targetGraphic");
        }

        private static void AssertColor(Color actual, Color expected, string name)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f), $"{name} r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f), $"{name} g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f), $"{name} b");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f), $"{name} a");
        }

        private static void AssertChildRectInsideParent(GameObject root, string parentPath, string childPath)
        {
            var parentTransform = string.IsNullOrEmpty(parentPath) ? root.transform : root.transform.Find(parentPath);
            var childTransform = root.transform.Find(childPath);

            Assert.That(parentTransform, Is.Not.Null, parentPath);
            Assert.That(childTransform, Is.Not.Null, childPath);

            var parent = parentTransform!.GetComponent<RectTransform>();
            var child = childTransform!.GetComponent<RectTransform>();
            Assert.That(parent, Is.Not.Null, parentPath);
            Assert.That(child, Is.Not.Null, childPath);

            var parentRect = parent!.rect;
            var childRect = GetRectInParentSpace(parent, child!);
            const float tolerance = 0.01f;
            Assert.That(childRect.xMin + tolerance, Is.GreaterThanOrEqualTo(parentRect.xMin), $"{childPath} left");
            Assert.That(childRect.xMax - tolerance, Is.LessThanOrEqualTo(parentRect.xMax), $"{childPath} right");
            Assert.That(childRect.yMin + tolerance, Is.GreaterThanOrEqualTo(parentRect.yMin), $"{childPath} bottom");
            Assert.That(childRect.yMax - tolerance, Is.LessThanOrEqualTo(parentRect.yMax), $"{childPath} top");
        }

        private static Rect GetRectInParentSpace(RectTransform parent, RectTransform child)
        {
            var parentRect = parent.rect;
            var parentAnchorPoint = new Vector2(
                parentRect.xMin + (child.anchorMin.x * parentRect.width),
                parentRect.yMin + (child.anchorMin.y * parentRect.height));
            var childMin = parentAnchorPoint + child.anchoredPosition - Vector2.Scale(child.pivot, child.sizeDelta);
            var childMax = childMin + child.sizeDelta;
            return Rect.MinMaxRect(childMin.x, childMin.y, childMax.x, childMax.y);
        }

        private static void AssertNoSiblingRectOverlap(GameObject root, string parentPath, string firstName, string secondName)
        {
            var parentTransform = string.IsNullOrEmpty(parentPath) ? root.transform : root.transform.Find(parentPath);

            Assert.That(parentTransform, Is.Not.Null, parentPath);
            var parent = parentTransform!.GetComponent<RectTransform>();
            var first = parentTransform.Find(firstName)?.GetComponent<RectTransform>();
            var second = parentTransform.Find(secondName)?.GetComponent<RectTransform>();
            Assert.That(parent, Is.Not.Null, parentPath);
            Assert.That(first, Is.Not.Null, $"{parentPath}/{firstName}");
            Assert.That(second, Is.Not.Null, $"{parentPath}/{secondName}");

            var firstRect = GetRectInParentSpace(parent!, first!);
            var secondRect = GetRectInParentSpace(parent!, second!);
            Assert.That(firstRect.Overlaps(secondRect), Is.False, $"{parentPath}/{firstName} overlaps {secondName}");
        }

        private static void AssertText(GameObject root, string path, string expected)
        {
            var target = root.transform.Find(path);

            Assert.That(target, Is.Not.Null, path);
            var text = target!.GetComponent<TMP_Text>();
            Assert.That(text, Is.Not.Null, path);
            Assert.That(text!.text, Is.EqualTo(expected), path);
        }

        private static int CountMissingScripts(GameObject root)
        {
            var count = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            }

            return count;
        }

        private static string GetTransformPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }

        private static string GetTransformPathRelativeToRoot(Transform root, Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null && transform.parent != root)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }
    }
}
